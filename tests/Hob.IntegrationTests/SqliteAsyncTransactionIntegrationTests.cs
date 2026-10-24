using System.Net;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestSqliteAsyncTransaction(Harness harness)
    {
        await TestSqliteAsyncTransactionDiagnostics(harness);
        var packageRoot = await WriteSqliteAsyncTransactionPackageAsync(harness, "sqlite-async-transaction-runtime", 20000);
        await TestSqliteAsyncTransactionContracts(harness, packageRoot);
        await TestSqliteAsyncTransactionDeadline(harness);
        await TestSqliteAsyncTransactionRuntime(harness, packageRoot);
    }

    private static async Task TestSqliteAsyncTransactionDiagnostics(Harness harness)
    {
        const string unawaited = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn write(db: DbWrite) -> bool effects { db.write } {
                with db.begin_async() as tx {
                    let saved: Result<i32, DbError> = tx.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_transaction::Parameters { id: 1 }
                    );
                    let committed: Result<bool, DbError> = tx.commit_async();
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-unawaited", unawaited,
            "E_ASYNC_CALL_UNAWAITED");

        const string directStandalone = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn write(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                with await db.begin_async() as tx {
                    return await db.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_transaction::Parameters { id: 1 }
                    );
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-direct-standalone", directStandalone,
            "E_DB_ASYNC_IN_TRANSACTION");

        const string transitiveStandalone = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn standalone(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                return await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_transaction::Parameters { id: 1 }
                );
            }
            async fn write(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let ignored: Result<i32, DbError> = await tx.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_transaction::Parameters { id: 2 }
                    );
                    let saved: Result<i32, DbError> = await self::app::async_transaction::standalone(db);
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-transitive-standalone", transitiveStandalone,
            "E_DB_ASYNC_IN_TRANSACTION");

        const string transitiveNestedTransaction = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn open_transaction(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let saved: Result<i32, DbError> = await tx.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_transaction::Parameters { id: 3 }
                    );
                    let committed: Result<bool, DbError> = await tx.commit_async();
                    return true;
                }
            }
            async fn outer(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let nested: bool = await self::app::async_transaction::open_transaction(db);
                    return nested;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-transitive-nested-transaction",
            transitiveNestedTransaction, "E_DB_ASYNC_IN_TRANSACTION");

        const string directNestedTransaction = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn write(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as outer {
                    let initial: Result<i32, DbError> = await outer.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_transaction::Parameters { id: 4 }
                    );
                    with await db.begin_async() as inner {
                        let nested: Result<i32, DbError> = await inner.execute_async(
                            "INSERT INTO sample (id) VALUES ($id)",
                            self::app::async_transaction::Parameters { id: 5 }
                        );
                        let committed: Result<bool, DbError> = await inner.commit_async();
                    }
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-direct-nested-transaction",
            directNestedTransaction, "E_DB_ASYNC_IN_TRANSACTION");

        const string escape = """
            module app::async_transaction;
            async fn write(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let escaped: Transaction = tx;
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-resource-escape", escape,
            "E_RESOURCE_ESCAPE");

        const string dynamicSql = """
            module app::async_transaction;
            struct Parameters { id: i32 }
            async fn write(db: DbWrite, sql: Text) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let saved: Result<i32, DbError> = await tx.execute_async(
                        sql,
                        self::app::async_transaction::Parameters { id: 1 }
                    );
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-dynamic-sql", dynamicSql,
            "E_DB_SQL_LITERAL");

        const string unsupportedSql = """
            module app::async_transaction;
            struct Parameters {}
            async fn write(db: DbWrite) -> bool effects { db.write } {
                with await db.begin_async() as tx {
                    let saved: Result<i32, DbError> = await tx.execute_async(
                        "COMMIT",
                        self::app::async_transaction::Parameters {}
                    );
                    return true;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-transaction-control-sql", unsupportedSql,
            "E_DB_TRANSACTION_STATEMENT");
    }

    private static async Task TestSqliteAsyncTransactionContracts(Harness harness, string packageRoot)
    {
        var check = await harness.InvokePackageDirectoryAsync("sqlite-async-transaction-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));

        var api = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        using (var apiJson = JsonDocument.Parse(api.StandardOutput))
            AssertEqual(13, apiJson.RootElement.GetProperty("schema_version").GetInt32(),
                "Async SQLite transactions must preserve API schema 13.");

        var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertSqliteReportDependencies(audit, "An async SQLite transaction audit");
        using (var auditJson = JsonDocument.Parse(audit.StandardOutput))
            AssertEqual(11, auditJson.RootElement.GetProperty("schema_version").GetInt32(),
                "Async SQLite transactions must preserve audit schema 11.");
        foreach (var operation in new[] { "DbWrite.begin_async", "Transaction.execute_async", "Transaction.commit_async" })
            await AssertAsyncSqliteAuditOperationAsync(harness, packageRoot, operation, "db.write");

        var build = await harness.InvokePackageDirectoryAsync("sqlite-async-transaction-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var artifact = ParseBuiltArtifact(build, "Built executable: ");
        AssertSqliteDependencyManifest(Path.ChangeExtension(artifact, ".deps.json"),
            "An async SQLite transaction executable");
        var receiptPath = Path.Combine(Path.GetDirectoryName(artifact)!, "build-receipt.json");
        AssertSqliteReceiptDependencies(receiptPath, await File.ReadAllTextAsync(receiptPath),
            "An async SQLite transaction receipt");

        var assemblyBytes = await File.ReadAllBytesAsync(artifact);
        var assemblyText = Encoding.UTF8.GetString(assemblyBytes) + Encoding.Unicode.GetString(assemblyBytes);
        AssertTrue(!assemblyText.Contains("DatabaseAsyncTestRecord", StringComparison.Ordinal),
            "A normal async transaction build must not include integration-only SQLite hooks.");
    }

    private static async Task TestSqliteAsyncTransactionRuntime(Harness harness, string packageRoot)
    {
        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-transaction-hooks");
        Directory.CreateDirectory(testDirectory);
        var environment = WriteTestEnvironment(testDirectory, Path.Combine(testDirectory, "transactions.sqlite3"),
            registrationBarrier: true);
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-transaction-runtime", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        var heldIdleIds = new[] { "idle-one", "idle-two", "idle-three" };
        var idleOperationIds = new List<string>();
        string? cancelOperationId = null;
        var heldSqlIds = new List<string>();
        var phase = "web server startup";
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);

            phase = "cancellation during transaction begin";
            using (var beginCancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/transaction/begin-cancel", HttpCompletionOption.ResponseHeadersRead,
                    beginCancellation.Token);
                var operationId = await WaitForNonEmptyFileContentAsync(
                    Path.Combine(testDirectory, "registered.entered"), TimeSpan.FromSeconds(10));
                beginCancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-registered-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Cancellation while opening an async transaction should unwind begin and propagate request cancellation.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closed", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
                var events = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!events.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == "transaction-opened" && parts[2] == operationId),
                    "Cancellation at the begin-registration barrier must not return a transaction owner.");
                AssertTrue(SqliteAsyncEventSequence(events, "callback-quiesced", operationId)
                           < SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           && SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           < SqliteAsyncEventSequence(events, "connection-closing", operationId)
                           && SqliteAsyncEventSequence(events, "connection-closed", operationId)
                           < SqliteAsyncEventSequence(events, "connection-slot-released", operationId),
                    "A failed begin must quiesce callbacks and close its connection before releasing the lifetime slot.");
            }

            phase = "normal transaction commit";
            using (var normal = await client.GetAsync("/transaction/normal"))
            {
                AssertEqual(HttpStatusCode.OK, normal.StatusCode, "A valid async transaction should commit.");
            }
            AssertTrue(await IsSamplePresentAsync(client, "normal"),
                "A row written in a committed async transaction should be visible to a later query.");

            phase = "early return rollback";
            using (var early = await client.GetAsync("/transaction/early-return"))
                AssertEqual(HttpStatusCode.InternalServerError, early.StatusCode,
                    "Returning early from an async transaction should reach the failure response.");
            AssertTrue(!await IsSamplePresentAsync(client, "early-return"),
                "An early return before commit must roll its transaction back.");

            phase = "statement failure rollback";
            using (var failedStatement = await client.GetAsync("/transaction/statement-failure"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, failedStatement.StatusCode,
                    "A SQLite statement fault should preserve the typed transaction error response.");
                AssertTrue((await failedStatement.Content.ReadAsStringAsync()).Contains("statement", StringComparison.Ordinal),
                    "A failed transaction statement should map to DbError.Statement.");
            }
            AssertTrue(!await IsSamplePresentAsync(client, "statement-failure"),
                "A failed statement must leave no row in the transaction.");
            using (var statementAudit = await client.GetAsync("/audit/statement-failure"))
                AssertEqual("0", await statementAudit.Content.ReadAsStringAsync(),
                    "A failed transaction statement must roll back its trigger side effect.");

            phase = "runtime fault rollback";
            using (var fault = await client.GetAsync("/transaction/runtime-fault"))
                AssertEqual(HttpStatusCode.InternalServerError, fault.StatusCode,
                    "A runtime fault inside an async transaction should fail the web request.");
            AssertTrue(!await IsSamplePresentAsync(client, "runtime-fault"),
                "A runtime fault before commit must roll back the earlier async transaction write.");

            phase = "deferred commit failure";
            using (var failedCommit = await client.GetAsync("/deferred/orphan"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, failedCommit.StatusCode,
                    "A deferred constraint failure at async commit should map to DbError.Statement.");
                AssertTrue((await failedCommit.Content.ReadAsStringAsync()).Contains("statement", StringComparison.Ordinal),
                    "A failed async commit should preserve the typed statement error.");
            }
            AssertEqual(HttpStatusCode.NotFound, (await client.GetAsync("/child/orphan")).StatusCode,
                "A failed async commit must roll back the transaction's deferred child row.");
            using (var orphanAudit = await client.GetAsync("/audit/orphan"))
                AssertEqual("0", await orphanAudit.Content.ReadAsStringAsync(),
                    "A failed async commit must roll back trigger effects as well as the child row.");

            phase = "precommit cancellation";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "precommit-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/transaction/precommit-cancel", HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token);
                var operationId = await WaitForSqlOperationAsync(testDirectory, "precommit-cancel");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "before-commit", operationId);
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Cancellation at the async transaction precommit barrier must win before commit_started.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "rollback-completed", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
                AssertTrue(!await IsSamplePresentAsync(client, "precommit-cancel"),
                    "Cancellation before async commit_started must roll back transaction writes.");
            }
            File.Delete(Path.Combine(testDirectory, "precommit-barrier-armed"));

            phase = "active transaction cancellation";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "callback-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/transaction/hold-tx-abort", HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token);
                var operationId = await WaitForSqlOperationAsync(testDirectory, "hold-tx-abort");
                cancelOperationId = operationId;
                heldSqlIds.Add("hold-tx-abort");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "pause-entered", operationId), TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-entered", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-tx-abort"), "release");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-dispose-started", operationId), TimeSpan.FromSeconds(10));
                var pendingEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!HasSqliteAsyncEvent(pendingEvents, "callback-quiesced", operationId)
                           && !HasSqliteAsyncEvent(pendingEvents, "progress-handler-cleared", operationId)
                           && !HasSqliteAsyncEvent(pendingEvents, "connection-closing", operationId),
                    "Async transaction disposal must wait for an in-flight native callback before clearing hooks or closing the connection.");
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-callback-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Cancellation during transaction execution should propagate after rollback and cleanup.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
                var events = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(SqliteAsyncEventSequence(events, "callback-quiesced", operationId)
                           < SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           && SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           < SqliteAsyncEventSequence(events, "connection-closing", operationId)
                           && SqliteAsyncEventSequence(events, "connection-closed", operationId)
                           < SqliteAsyncEventSequence(events, "connection-slot-released", operationId),
                    "An interrupted async transaction must finish callback and connection cleanup before releasing its slot.");
                AssertTrue(!await IsSamplePresentAsync(client, "hold-tx-abort"),
                    "An interrupted transaction statement must not persist its row.");
            }

            phase = "late commit success";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "commit-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/transaction/late-committed", HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token);
                var operationId = await WaitForSqlOperationAsync(testDirectory, "late-committed");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "commit-started", operationId);
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt-skipped", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-commit-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "A client abort after commit_started may cancel response delivery while commit completes.");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "provider-result-ok", operationId);
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
                var events = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(SqliteAsyncEventSequence(events, "commit-started", operationId)
                           < SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                           && SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                           < SqliteAsyncEventSequenceWithKey(events, "provider-result-ok", operationId, "commit"),
                    "Once async commit_started wins, cancellation must not replace successful provider commit.");
                AssertTrue(await IsSamplePresentAsync(client, "late-committed"),
                    "A successful async commit remains authoritative after late cancellation.");
            }
            File.Delete(Path.Combine(testDirectory, "commit-barrier-armed"));

            phase = "late commit failure";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "commit-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/deferred/orphan-late", HttpCompletionOption.ResponseHeadersRead,
                    cancellation.Token);
                var operationId = await WaitForSqlOperationAsync(testDirectory, "orphan-late");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "commit-started", operationId);
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt-skipped", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-commit-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Late cancellation should not replace the deferred provider commit failure.");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "provider-result-error", operationId);
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
                var events = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(SqliteAsyncEventSequence(events, "commit-started", operationId)
                           < SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                           && SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                           < SqliteAsyncEventSequence(events, "provider-result-error", operationId),
                    "A deferred provider error after async commit_started remains authoritative after cancellation.");
                AssertEqual(HttpStatusCode.NotFound, (await client.GetAsync("/child/orphan-late")).StatusCode,
                    "A deferred async commit failure must roll back after late cancellation.");
            }
            File.Delete(Path.Combine(testDirectory, "commit-barrier-armed"));

            phase = "mixed idle and active connection capacity";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "transaction-idle-barrier-armed"), "armed");
            var idleRequests = heldIdleIds.Select(id => client.GetAsync("/transaction/" + id)).ToArray();
            idleOperationIds.AddRange(await WaitForSqliteAsyncTransactionIdleOperationIdsAsync(
                Path.Combine(testDirectory, "events.log"), heldIdleIds.Length));

            var activeRead = client.GetAsync("/read-gate/hold-capacity-read", HttpCompletionOption.ResponseHeadersRead);
            heldSqlIds.Add("hold-capacity-read");
            var activeReadOperationId = await WaitForSqlOperationAsync(testDirectory, "hold-capacity-read");
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "pause-entered", activeReadOperationId), TimeSpan.FromSeconds(10));

            using (var queuedCancellation = new CancellationTokenSource())
            {
                var queuedRequest = client.GetAsync("/read/fifth", HttpCompletionOption.ResponseHeadersRead,
                    queuedCancellation.Token);
                var queuedOperationId = await WaitForEventOperationAsync(testDirectory, "connection-queued", TimeSpan.FromSeconds(10));
                queuedCancellation.Cancel();
                await AssertHttpRequestCanceledAsync(queuedRequest,
                    "The fifth SQLite connection must cancel while waiting behind three idle transactions and one active query.");
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "connection-wait-canceled", queuedOperationId);

                foreach (var operationId in idleOperationIds)
                    await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-transaction-idle-" + operationId), "release");
                foreach (var request in idleRequests)
                {
                    using var response = await request.WaitAsync(TimeSpan.FromSeconds(15));
                    AssertEqual(HttpStatusCode.OK, response.StatusCode,
                        "An idle transaction owner should execute and commit while the fourth lifetime slot is an active standalone query.");
                }
                var settledEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(idleOperationIds.All(operationId =>
                        HasSqliteAsyncEvent(settledEvents, "provider-result-ok", operationId)
                        && SqliteAsyncEventSequence(settledEvents, "connection-closed", operationId)
                           < SqliteAsyncEventSequence(settledEvents, "connection-slot-released", operationId)),
                    "Each idle transaction must execute, close its connection, and only then release its lifetime slot without reacquiring one for operations.");
                AssertTrue(!settledEvents.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == "opened" && parts[2] == queuedOperationId),
                    "A canceled fifth async SQLite operation must never open a connection.");
            }

            await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-capacity-read"), "release");
            using (var readResponse = await activeRead.WaitAsync(TimeSpan.FromSeconds(10)))
                AssertEqual(HttpStatusCode.OK, readResponse.StatusCode,
                    "The active standalone query should finish after its deterministic SQL barrier is released.");
            using (var recovered = await client.GetAsync("/read/normal").WaitAsync(TimeSpan.FromSeconds(10)))
                AssertEqual(HttpStatusCode.OK, recovered.StatusCode,
                    "A later async query should succeed after transaction and standalone connection slots are released.");
            foreach (var id in heldIdleIds)
                AssertTrue(await IsSamplePresentAsync(client, id), "Every admitted idle transaction should persist after commit.");

            assertionsCompleted = true;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Async SQLite transaction test failed during {phase}: {error.Message}", error);
        }
        finally
        {
            foreach (var operationId in idleOperationIds)
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-transaction-idle-" + operationId), "release");
            foreach (var id in heldSqlIds)
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, id), "release");
            if (cancelOperationId is not null)
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-callback-" + cancelOperationId), "release");
            foreach (var marker in new[] { "precommit-barrier-armed", "commit-barrier-armed", "transaction-idle-barrier-armed" })
                File.Delete(Path.Combine(testDirectory, marker));
            foreach (var operationId in Directory.GetFiles(testDirectory, "before-commit.*.marker")
                         .Select(path => Path.GetFileName(path)!.Split('.')[1]))
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");
            foreach (var operationId in Directory.GetFiles(testDirectory, "commit-started.*.marker")
                         .Select(path => Path.GetFileName(path)!.Split('.')[1]))
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-commit-" + operationId), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(termination.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The async SQLite transaction fixture host did not stop within 10 seconds."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static async Task<string> WriteSqliteAsyncTransactionPackageAsync(Harness harness, string name, int requestTimeoutMs) =>
        await harness.WritePackageAsync(
            name,
            WriteRuntimeManifest(name, requestTimeoutMs),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = AsyncTransactionRuntimeModule,
                ["db/schema.sql"] = AsyncWriteRuntimeSchema
            });

    private static async Task TestSqliteAsyncTransactionDeadline(Harness harness)
    {
        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-transaction-deadline");
        Directory.CreateDirectory(testDirectory);
        var packageRoot = await WriteSqliteAsyncTransactionPackageAsync(harness,
            "sqlite-async-transaction-deadline", requestTimeoutMs: 2500);
        var environment = WriteTestEnvironment(testDirectory, Path.Combine(testDirectory, "transactions.sqlite3"));
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-transaction-deadline", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "precommit-barrier-armed"), "armed");
            var request = client.GetAsync("/transaction/deadline-before-commit", HttpCompletionOption.ResponseHeadersRead);
            var operationId = await WaitForSqlOperationAsync(testDirectory, "deadline-before-commit");
            await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "before-commit", operationId);
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");
            using (var timedOut = await request.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                AssertEqual(HttpStatusCode.GatewayTimeout, timedOut.StatusCode,
                    "A web request deadline before async commit_started should return the stable 504 response.");
                AssertTrue((await timedOut.Content.ReadAsStringAsync()).Contains("request_timeout", StringComparison.Ordinal),
                    "The transaction deadline should preserve the stable request-timeout response body.");
            }
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-slot-released", operationId), TimeSpan.FromSeconds(10));
            AssertTrue(!await IsSamplePresentAsync(client, "deadline-before-commit"),
                "A web deadline before async commit_started must roll back transaction writes.");
            using (var recovered = await client.GetAsync("/read/deadline-before-commit").WaitAsync(TimeSpan.FromSeconds(10)))
                AssertEqual(HttpStatusCode.NotFound, recovered.StatusCode,
                    "A later query should succeed after deadline-driven transaction cleanup releases its slot.");
            assertionsCompleted = true;
        }
        finally
        {
            foreach (var operationId in Directory.GetFiles(testDirectory, "before-commit.*.marker")
                         .Select(path => Path.GetFileName(path)!.Split('.')[1]))
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(termination.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The async SQLite transaction deadline host did not stop within 10 seconds."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static async Task<string[]> WaitForSqliteAsyncTransactionIdleOperationIdsAsync(string eventLogPath, int count)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            if (File.Exists(eventLogPath))
            {
                var events = await ReadSqliteAsyncEventLogLinesAsync(eventLogPath, cancellation.Token);
                var operationIds = events.Select(line => line.Split('\t'))
                    .Where(parts => parts.Length == 4 && parts[1] == "transaction-idle-entered")
                    .Select(parts => parts[2])
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (operationIds.Length == count) return operationIds;
            }
            await Task.Delay(20, cancellation.Token);
        }
    }

    private static bool HasSqliteAsyncEvent(IEnumerable<string> events, string phase, string operationId) =>
        events.Select(line => line.Split('\t'))
            .Any(parts => parts.Length == 4 && parts[1] == phase && parts[2] == operationId);

    private static long SqliteAsyncEventSequenceWithKey(
        IReadOnlyList<string> events,
        string phase,
        string operationId,
        string key) =>
        long.Parse(events.Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 4 && parts[1] == phase && parts[2] == operationId && parts[3] == key)
            .Select(parts => parts[0])
            .Single(), System.Globalization.CultureInfo.InvariantCulture);

    private const string AsyncTransactionRuntimeModule = """
        module app::main;
        struct Parameters { id: Text }
        struct Row { id: Text }
        struct CountRow { count: i32 }
        union TransactionReply { Completed, Failure(Text) }
        union ReadReply { Found(self::app::main::Row), Missing, Failure(Text) }
        union CountReply { Counted(i32), Failure(Text) }
        union HealthReply { Healthy }

        fn health() -> self::app::main::HealthReply effects {} {
            return self::app::main::HealthReply.Healthy;
        }

        async fn transaction(id: Text, db: DbWrite) -> self::app::main::TransactionReply effects { db.write } {
            with await db.begin_async() as tx {
                let written: Result<i32, DbError> = await tx.execute_async(
                    "INSERT INTO sample (id) VALUES (hob_test_pause($id))",
                    self::app::main::Parameters { id: id }
                );
                if id == "early-return" {
                    return self::app::main::TransactionReply.Failure("early");
                }
                if id == "runtime-fault" {
                    let overflow: i32 = 2147483647 + 1;
                }
                let committed: Result<bool, DbError> = await tx.commit_async();
                return match committed {
                    Ok(value) => self::app::main::TransactionReply.Completed,
                    Err(error) => match error {
                        DbError.Statement => self::app::main::TransactionReply.Failure("statement"),
                        DbError.RowShape => self::app::main::TransactionReply.Failure("row-shape")
                    }
                };
            }
        }

        async fn deferred(id: Text, db: DbWrite) -> self::app::main::TransactionReply effects { db.write } {
            with await db.begin_async() as tx {
                let written: Result<i32, DbError> = await tx.execute_async(
                    "INSERT INTO child (parent_id) VALUES (hob_test_pause($id))",
                    self::app::main::Parameters { id: id }
                );
                let committed: Result<bool, DbError> = await tx.commit_async();
                return match committed {
                    Ok(value) => self::app::main::TransactionReply.Completed,
                    Err(error) => match error {
                        DbError.Statement => self::app::main::TransactionReply.Failure("statement"),
                        DbError.RowShape => self::app::main::TransactionReply.Failure("row-shape")
                    }
                };
            }
        }

        async fn read(id: Text, db: DbRead) -> self::app::main::ReadReply effects { db.read } {
            let loaded: Result<Option<self::app::main::Row>, DbError> = await db.query_one_async(
                "SELECT id FROM sample WHERE id = $id",
                self::app::main::Parameters { id: id }
            );
            return match loaded {
                Ok(value) => match value {
                    Some(row) => self::app::main::ReadReply.Found(row),
                    None => self::app::main::ReadReply.Missing
                },
                Err(error) => match error {
                    DbError.Statement => self::app::main::ReadReply.Failure("statement"),
                    DbError.RowShape => self::app::main::ReadReply.Failure("row-shape")
                }
            };
        }

        async fn read_gate(id: Text, db: DbRead) -> self::app::main::ReadReply effects { db.read } {
            let loaded: Result<Option<self::app::main::Row>, DbError> = await db.query_one_async(
                "SELECT hob_test_pause($id) AS id",
                self::app::main::Parameters { id: id }
            );
            return match loaded {
                Ok(value) => match value {
                    Some(row) => self::app::main::ReadReply.Found(row),
                    None => self::app::main::ReadReply.Missing
                },
                Err(error) => match error {
                    DbError.Statement => self::app::main::ReadReply.Failure("statement"),
                    DbError.RowShape => self::app::main::ReadReply.Failure("row-shape")
                }
            };
        }

        async fn audit(id: Text, db: DbRead) -> self::app::main::CountReply effects { db.read } {
            let loaded: Result<Option<self::app::main::CountRow>, DbError> = await db.query_one_async(
                "SELECT count(*) AS count FROM audit WHERE id = $id",
                self::app::main::Parameters { id: id }
            );
            return match loaded {
                Ok(value) => match value {
                    Some(row) => self::app::main::CountReply.Counted(row.count),
                    None => self::app::main::CountReply.Counted(0)
                },
                Err(error) => match error {
                    DbError.Statement => self::app::main::CountReply.Failure("statement"),
                    DbError.RowShape => self::app::main::CountReply.Failure("row-shape")
                }
            };
        }

        route GET "/" {
            handler: self::app::main::health;
            response Healthy: 200;
        }
        route GET "/transaction/{id}" {
            path id: Text;
            handler: self::app::main::transaction;
        response Completed: 200;
            response Failure: 500 json Text;
        }
        route GET "/deferred/{id}" {
            path id: Text;
            handler: self::app::main::deferred;
            response Completed: 200;
            response Failure: 500 json Text;
        }
        route GET "/read/{id}" {
            path id: Text;
            handler: self::app::main::read;
            response Found: 200 json self::app::main::Row;
            response Missing: 404;
            response Failure: 500 json Text;
        }
        route GET "/read-gate/{id}" {
            path id: Text;
            handler: self::app::main::read_gate;
            response Found: 200 json self::app::main::Row;
            response Missing: 404;
            response Failure: 500 json Text;
        }
        route GET "/audit/{id}" {
            path id: Text;
            handler: self::app::main::audit;
            response Counted: 200 json i32;
            response Failure: 500 json Text;
        }
        """;
}
