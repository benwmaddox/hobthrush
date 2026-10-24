using System.Net;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestSqliteAsyncExecute(Harness harness)
    {
        var phase = "event log sharing";
        try
        {
            await TestSqliteAsyncEventLogReadSharing(harness);
            phase = "compile diagnostics";
            await TestSqliteAsyncExecuteDiagnostics(harness);
            phase = "report contracts";
            await TestSqliteAsyncExecuteReports(harness);
            phase = "runtime cancellation, rollback, and capacity";
            await TestSqliteAsyncExecuteRuntime(harness);
            phase = "request deadline before commit";
            await TestSqliteAsyncExecutePreCommitDeadline(harness);
            phase = "cancellation after successful commit";
            await TestSqliteAsyncExecuteLateCancellation(harness, commitFailure: false);
            phase = "cancellation after failed commit";
            await TestSqliteAsyncExecuteLateCancellation(harness, commitFailure: true);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException($"Async SQLite execute case failed during {phase}: {error.Message}", error);
        }
    }

    private static async Task TestSqliteAsyncEventLogReadSharing(Harness harness)
    {
        var eventLogPath = Path.Combine(harness.TemporaryRoot, "sqlite-event-log-sharing.log");
        const string existing = "1\tbefore\t1\t";
        const string appended = "2\tafter\t1\t";
        await File.WriteAllTextAsync(eventLogPath, existing + Environment.NewLine);

        string contents;
        using (var reader = OpenSqliteAsyncEventLogReader(eventLogPath))
        {
            File.AppendAllText(eventLogPath, appended + Environment.NewLine);
            contents = await reader.ReadToEndAsync();
        }

        AssertEqual(existing + Environment.NewLine + appended + Environment.NewLine, contents,
            "A shared event-log reader must allow the provider to append while the reader remains open.");
        var lines = await ReadSqliteAsyncEventLogLinesAsync(eventLogPath);
        AssertEqual(2, lines.Length, "The shared event-log reader should retain both records after append.");
    }

    private static async Task TestSqliteAsyncExecuteDiagnostics(Harness harness)
    {
        const string unawaited = """
            module app::async_write;
            struct Parameters { id: i32 }
            fn write(db: DbWrite) -> bool effects { db.write } {
                let saved: Result<i32, DbError> = db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_write::Parameters { id: 1 }
                );
                return true;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-unawaited", unawaited, "E_ASYNC_CALL_UNAWAITED");

        const string directTransaction = """
            module app::async_write;
            struct Parameters { id: i32 }
            async fn save(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                let saved: Result<i32, DbError> = await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_write::Parameters { id: 1 }
                );
                return saved;
            }
            async fn run(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                with db.begin() as tx {
                    return await db.execute_async(
                        "INSERT INTO sample (id) VALUES ($id)",
                        self::app::async_write::Parameters { id: 1 }
                    );
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-direct-transaction", directTransaction,
            "E_DB_ASYNC_IN_TRANSACTION");

        const string transitiveTransaction = """
            module app::async_write;
            struct Parameters { id: i32 }
            async fn save(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                let saved: Result<i32, DbError> = await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_write::Parameters { id: 1 }
                );
                return saved;
            }
            async fn run(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                with db.begin() as tx {
                    return await self::app::async_write::save(db);
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-transitive-transaction", transitiveTransaction,
            "E_DB_ASYNC_IN_TRANSACTION");

        const string dynamicSql = """
            module app::async_write;
            struct Parameters { id: i32 }
            async fn save(db: DbWrite) -> Result<i32, DbError> effects { db.write } {
                let sql: Text = "INSERT INTO sample (id) VALUES ($id)";
                return await db.execute_async(sql, self::app::async_write::Parameters { id: 1 });
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-dynamic-sql", dynamicSql, "E_DB_SQL_LITERAL");

        foreach (var (name, sql) in new[]
                 {
                     ("sqlite-async-write-commit-sql", "COMMIT"),
                     ("sqlite-async-write-multiple-sql", "INSERT INTO sample (id) VALUES (1); DELETE FROM sample")
                 })
        {
            var source = "module app::async_write;\n"
                + "struct Parameters {}\n"
                + "async fn save(db: DbWrite) -> Result<i32, DbError> effects { db.write } {\n"
                + "    return await db.execute_async(" + JsonSerializer.Serialize(sql)
                + ", self::app::async_write::Parameters {});\n}\n";
            await ExpectDiagnosticsAsync(harness, name, source, "E_DB_WRITE_STATEMENT");
        }

        const string unsupportedParameters = """
            module app::async_write;
            struct Parameters { id: Bytes }
            async fn save(db: DbWrite, id: Bytes) -> Result<i32, DbError> effects { db.write } {
                return await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_write::Parameters { id: id }
                );
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-unsupported-parameters", unsupportedParameters,
            "E_DB_CODEC_UNSUPPORTED");

        const string exceededEffect = """
            module app::async_write;
            struct Parameters { id: i32 }
            async fn save(db: DbWrite) -> Result<i32, DbError> effects {} {
                return await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::async_write::Parameters { id: 1 }
                );
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-write-effect-bound", exceededEffect, "E_EFFECT_EXCEEDED");
    }

    private static async Task TestSqliteAsyncExecuteReports(Harness harness)
    {
        const string writeOnlySource = """
            module app::writer;
            pub struct Parameters { id: Text }
            pub async fn insert(db: DbWrite, id: Text) -> Result<i32, DbError> effects { db.write } {
                let saved: Result<i32, DbError> = await db.execute_async(
                    "INSERT INTO sample (id) VALUES ($id)",
                    self::app::writer::Parameters { id: id }
                );
                return saved;
            }
            """;
        var writeOnlyRoot = await harness.WritePackageAsync(
            "sqlite-async-write-only-library",
            LibraryPackageManifest("sqlite-async-write-only-library"),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/writer.hob"] = writeOnlySource });

        var writeOnlyCheck = await harness.InvokePackageDirectoryAsync("sqlite-async-write-only-check", writeOnlyRoot, "check", "--json");
        AssertEqual(0, writeOnlyCheck.ExitCode, Describe(writeOnlyCheck));
        var writeOnlyBuild = await harness.InvokePackageDirectoryAsync("sqlite-async-write-only-build", writeOnlyRoot, "build");
        AssertEqual(0, writeOnlyBuild.ExitCode, Describe(writeOnlyBuild));
        const string builtLibraryPrefix = "Built library: ";
        AssertTrue(writeOnlyBuild.StandardOutput.StartsWith(builtLibraryPrefix, StringComparison.Ordinal), Describe(writeOnlyBuild));
        var writeOnlyArtifact = writeOnlyBuild.StandardOutput[builtLibraryPrefix.Length..].Trim();
        AssertTrue(File.Exists(writeOnlyArtifact), $"Expected async write-only library output at {writeOnlyArtifact}.");
        AssertSqliteDependencyManifest(Path.ChangeExtension(writeOnlyArtifact, ".deps.json"),
            "A write-only async SQLite library");
        var writeOnlyAssemblyText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(writeOnlyArtifact))
            + Encoding.Unicode.GetString(await File.ReadAllBytesAsync(writeOnlyArtifact));
        AssertTrue(!writeOnlyAssemblyText.Contains("HOB_SQLITE_ASYNC_TEST_HOOKS", StringComparison.Ordinal)
                   && !writeOnlyAssemblyText.Contains("DatabaseAsyncTestRecord", StringComparison.Ordinal),
            "A production write-only library must not contain integration-only SQLite hook activation or code.");
        AssertSqliteReportDependencies(await harness.InvokeCompilerCommandAsync("audit", writeOnlyRoot, "--json"),
            "A write-only async SQLite audit");
        await AssertAsyncSqliteAuditOperationAsync(harness, writeOnlyRoot, "DbWrite.execute_async", "db.write");
        AssertSqliteReceiptDependencies(Path.Combine(Path.GetDirectoryName(writeOnlyArtifact)!, "build-receipt.json"),
            await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(writeOnlyArtifact)!, "build-receipt.json")),
            "A write-only async SQLite receipt");

        const string mixedRootSource = """
            module app::main;
            pub struct ReadParameters { id: Text }
            pub struct Row { id: Text }
            pub async fn save_and_find(db: DbWrite, reader: DbRead, id: Text) -> Result<Option<self::app::main::Row>, DbError> effects { db.write, db.read } {
                let saved: Result<i32, DbError> = await writer::app::writer::insert(db, id);
                return match saved {
                    Ok(count) => await reader.query_one_async(
                            "SELECT id FROM sample WHERE id = $id",
                            self::app::main::ReadParameters { id: id }
                        ),
                    Err(error) => Err(error)
                };
            }
            """;
        var mixedRoot = await harness.WritePackageGraphAsync(
            "sqlite-async-mixed-dependency",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    LibraryPackageManifest("sqlite-async-mixed-root") + "\n[dependencies]\nwriter = \"../writer\"\n",
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.hob"] = mixedRootSource
                    }),
                ["writer"] = new PackageFixture(
                    LibraryPackageManifest("sqlite-async-write-only-dependency"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/writer.hob"] = writeOnlySource
                    })
            });

        var lockResult = await harness.InvokePackageDirectoryAsync("sqlite-async-mixed-lock", mixedRoot, "lock");
        AssertEqual(0, lockResult.ExitCode, Describe(lockResult));
        AssertTrue(File.Exists(Path.Combine(mixedRoot, "hob.lock")), "The mixed SQLite package should receive a source dependency lock.");
        var mixedCheck = await harness.InvokePackageDirectoryAsync("sqlite-async-mixed-check", mixedRoot, "check", "--json");
        AssertEqual(0, mixedCheck.ExitCode, Describe(mixedCheck));
        var api = await harness.InvokeCompilerCommandAsync("inspect", "api", mixedRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        using (var apiJson = JsonDocument.Parse(api.StandardOutput))
            AssertEqual(13, apiJson.RootElement.GetProperty("schema_version").GetInt32(), "Adding async SQLite writes must preserve API schema 13.");
        var mixedAudit = await harness.InvokeCompilerCommandAsync("audit", mixedRoot, "--json");
        AssertEqual(0, mixedAudit.ExitCode, Describe(mixedAudit));
        AssertSqliteReportDependencies(mixedAudit, "A mixed async SQLite audit");
        AssertTrue(mixedAudit.StandardOutput.Contains("DbWrite.execute_async", StringComparison.Ordinal)
                   && mixedAudit.StandardOutput.Contains("DbRead.query_one_async", StringComparison.Ordinal),
            "A mixed audit must include trusted async write and read operations across the dependency graph.");
        using (var auditJson = JsonDocument.Parse(mixedAudit.StandardOutput))
            AssertEqual(11, auditJson.RootElement.GetProperty("schema_version").GetInt32(), "Adding async SQLite writes must preserve audit schema 11.");
        var mixedBuild = await harness.InvokePackageDirectoryAsync("sqlite-async-mixed-build", mixedRoot, "build");
        AssertEqual(0, mixedBuild.ExitCode, Describe(mixedBuild));
        var mixedArtifact = mixedBuild.StandardOutput[builtLibraryPrefix.Length..].Trim();
        AssertTrue(File.Exists(mixedArtifact), $"Expected mixed async SQLite library output at {mixedArtifact}.");
        AssertSqliteDependencyManifest(Path.ChangeExtension(mixedArtifact, ".deps.json"),
            "A mixed async SQLite library");
        var mixedReceiptPath = Path.Combine(Path.GetDirectoryName(mixedArtifact)!, "build-receipt.json");
        using (var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(mixedReceiptPath)))
            AssertEqual(4, receipt.RootElement.GetProperty("schema_version").GetInt32(), "Adding async SQLite writes must preserve build receipt schema 4.");
        AssertSqliteReceiptDependencies(mixedReceiptPath, await File.ReadAllTextAsync(mixedReceiptPath), "A mixed async SQLite receipt");

        var productionCompiler = Path.Combine(harness.RepositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll");
        AssertTrue(File.Exists(productionCompiler), "The standard production compiler must exist for async write hook exclusion checks.");
        var productionCompilerBytes = await File.ReadAllBytesAsync(productionCompiler);
        var productionCompilerText = Encoding.UTF8.GetString(productionCompilerBytes) + Encoding.Unicode.GetString(productionCompilerBytes);
        AssertTrue(!productionCompilerText.Contains("HOB_SQLITE_ASYNC_TEST_HOOKS", StringComparison.Ordinal),
            "The standard production compiler must not expose an environment switch that enables SQLite async test hooks.");
    }

    private static async Task TestSqliteAsyncExecuteRuntime(Harness harness)
    {
        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-write-hooks");
        Directory.CreateDirectory(testDirectory);
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-async-write-runtime",
            WriteRuntimeManifest("sqlite-async-write-runtime", requestTimeoutMs: 20000),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = AsyncWriteRuntimeModule,
                ["db/schema.sql"] = AsyncWriteRuntimeSchema
            });
        var environment = WriteTestEnvironment(testDirectory, Path.Combine(testDirectory, "writes.sqlite3"), registrationBarrier: true);
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-write-runtime", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        var heldIds = new[] { "hold-write-a", "hold-read-a", "hold-read-b", "hold-read-c" };
        string? abortOperationId = null;
        var phase = "web server startup";
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);

            phase = "registration cancellation";
            using (var registrationCancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/write/registration-cancel", HttpCompletionOption.ResponseHeadersRead,
                    registrationCancellation.Token);
                var registeredPath = Path.Combine(testDirectory, "registered.entered");
                var operationId = await WaitForNonEmptyFileContentAsync(registeredPath, TimeSpan.FromSeconds(10));
                registrationCancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-registered-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Cancellation immediately after callback registration should stop before guest SQL.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", operationId), TimeSpan.FromSeconds(10));
                AssertTrue(!await IsSamplePresentAsync(client, "registration-cancel"),
                    "Cancellation before SQL should leave no persisted row.");
                var registrationEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!registrationEvents.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == "sql-active" && parts[2] == operationId),
                    "Cancellation after registration must not execute the guest's SQL statement.");
                AssertTrue(SqliteAsyncEventSequence(registrationEvents, "callback-quiesced", operationId)
                           < SqliteAsyncEventSequence(registrationEvents, "progress-handler-cleared", operationId)
                           && SqliteAsyncEventSequence(registrationEvents, "progress-handler-cleared", operationId)
                           < SqliteAsyncEventSequence(registrationEvents, "connection-closing", operationId),
                    "Registration cancellation must quiesce and clear native callbacks before connection teardown.");
            }

            phase = "normal committed write and read";
            using (var created = await client.GetAsync("/write/normal"))
            {
                AssertEqual(HttpStatusCode.OK, created.StatusCode, "An awaited SQLite write should return its affected-row result.");
                AssertEqual("1", await created.Content.ReadAsStringAsync(), "A single inserted row should report one affected row.");
            }
            using (var found = await client.GetAsync("/read/normal"))
            {
                AssertEqual(HttpStatusCode.OK, found.StatusCode, "A committed async write should be visible to a later async read.");
                AssertTrue((await found.Content.ReadAsStringAsync()).Contains("normal", StringComparison.Ordinal),
                    "The async write should preserve its bound parameter value.");
            }

            phase = "deferred commit failure rollback";
            using (var failedCommit = await client.GetAsync("/deferred/orphan"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, failedCommit.StatusCode,
                    "A deferred foreign-key failure at internal commit should map to DbError.Statement.");
                AssertTrue((await failedCommit.Content.ReadAsStringAsync()).Contains("statement", StringComparison.Ordinal),
                    "The failed internal commit should preserve the typed statement error.");
            }
            AssertEqual(HttpStatusCode.NotFound, (await client.GetAsync("/child/orphan")).StatusCode,
                "A failed internal commit must roll the child write back.");
            using (var audit = await client.GetAsync("/audit/orphan"))
            {
                AssertEqual(HttpStatusCode.OK, audit.StatusCode, "The audit count route should remain available after rollback.");
                AssertEqual("0", await audit.Content.ReadAsStringAsync(),
                    "The failed child write must also roll back its trigger side effect.");
            }

            phase = "statement failure rollback";
            using (var failedStatement = await client.GetAsync("/write/statement-failure"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, failedStatement.StatusCode,
                    "A SQLite statement error should map to DbError.Statement.");
                AssertTrue((await failedStatement.Content.ReadAsStringAsync()).Contains("statement", StringComparison.Ordinal),
                    "The statement error should retain its typed failure response.");
            }
            using (var missingStatementRow = await client.GetAsync("/read/statement-failure"))
                AssertEqual(HttpStatusCode.NotFound, missingStatementRow.StatusCode,
                    "The internal transaction must roll back an insert whose trigger aborts the statement.");
            using (var statementAudit = await client.GetAsync("/audit/statement-failure"))
                AssertEqual("0", await statementAudit.Content.ReadAsStringAsync(),
                    "The internal transaction must roll back trigger side effects when a DML statement fails.");

            phase = "precommit cancellation";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "precommit-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/write/precommit-cancel", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
                var eventsPath = Path.Combine(testDirectory, "events.log");
                var operationId = await WaitForSqlOperationAsync(testDirectory, "precommit-cancel");
                await WaitForEventAsync(eventsPath, "before-commit", operationId);
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request,
                    "Cancellation at the deterministic precommit barrier should abort the HTTP request.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", operationId), TimeSpan.FromSeconds(10));
                AssertTrue(!await IsSamplePresentAsync(client, "precommit-cancel"),
                    "Cancellation before the commit boundary must roll the async write back.");
                var events = await ReadSqliteAsyncEventLogLinesAsync(eventsPath);
                AssertTrue(SqliteAsyncEventSequence(events, "callback-quiesced", operationId)
                           < SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           && SqliteAsyncEventSequence(events, "progress-handler-cleared", operationId)
                           < SqliteAsyncEventSequence(events, "connection-closing", operationId),
                    "Cancellation teardown must quiesce callbacks, clear the progress handler, dispose the transaction, then close the connection.");
            }
            File.Delete(Path.Combine(testDirectory, "precommit-barrier-armed"));

            phase = "in-flight callback cancellation";
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "callback-barrier-armed"), "armed");
            using (var cancellation = new CancellationTokenSource())
            {
                var request = client.GetAsync("/write/hold-abort", HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
                var operationId = await WaitForSqlOperationAsync(testDirectory, "hold-abort");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "pause-entered", operationId), TimeSpan.FromSeconds(10));
                cancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-entered", operationId), TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-abort"), "release");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-dispose-started", operationId), TimeSpan.FromSeconds(10));
                var pendingEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!pendingEvents.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[2] == operationId
                            && parts[1] is "callback-quiesced" or "progress-handler-cleared" or "connection-closing"),
                    "The write worker must wait while its native interrupt callback is held in flight.");
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-callback-" + operationId), "release");
                await AssertHttpRequestCanceledAsync(request, "An active SQLite write should observe RequestAborted and roll back.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", operationId), TimeSpan.FromSeconds(10));
                AssertTrue(!await IsSamplePresentAsync(client, "hold-abort"),
                    "An interrupted active DML statement must not persist its row.");
                abortOperationId = operationId;
            }

            phase = "mixed four-request admission";
            var activeRequests = new[]
            {
                client.GetAsync("/write/hold-write-a"),
                client.GetAsync("/read-gate/hold-read-a"),
                client.GetAsync("/read-gate/hold-read-b"),
                client.GetAsync("/read-gate/hold-read-c")
            };
            var activeOperationIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var id in heldIds)
            {
                var operationId = await WaitForSqlOperationAsync(testDirectory, id);
                activeOperationIds.Add(id, operationId);
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "pause-entered", operationId), TimeSpan.FromSeconds(10));
            }
            var activeEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
            var heldOperationIds = activeOperationIds.Values.ToHashSet(StringComparer.Ordinal);
            AssertEqual(4, heldOperationIds.Count,
                "One async write and three async reads should be admitted as four distinct mixed SQLite workers.");
            AssertTrue(heldOperationIds.All(operationId => SqliteAsyncEventSequence(activeEvents, "sql-active", operationId) > 0),
                "Each of the four mixed operations should reach its own SQLite statement concurrently.");

            phase = "queued fifth request cancellation and settlement";
            using (var queuedCancellation = new CancellationTokenSource())
            {
                var queuedRequest = client.GetAsync("/write/queued-fifth", HttpCompletionOption.ResponseHeadersRead,
                    queuedCancellation.Token);
                var queuedOperationId = await WaitForNonEmptyFileContentAsync(
                    Path.Combine(testDirectory, "queued.entered"), TimeSpan.FromSeconds(10));
                queuedCancellation.Cancel();
                phase = "client cancellation of queued request";
                await AssertHttpRequestCanceledAsync(queuedRequest,
                    "A fifth mixed read/write operation should cancel while waiting for shared worker capacity.");
                phase = "server settlement of queued connection wait";
                await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), "connection-wait-canceled", queuedOperationId);

                phase = "held writer release and completion";
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-write-a"), "release");
                using (var released = await activeRequests[0].WaitAsync(TimeSpan.FromSeconds(10)))
                    AssertEqual(HttpStatusCode.OK, released.StatusCode, "A held async write should commit after release.");
                phase = "capacity recovery read";
                using (var recovered = await client.GetAsync("/read/normal").WaitAsync(TimeSpan.FromSeconds(10)))
                    AssertEqual(HttpStatusCode.OK, recovered.StatusCode, "A later read should acquire capacity after a write releases its slot.");
                phase = "canceled request never opened a connection";
                var settledEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!settledEvents.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == "opened" && parts[2] == queuedOperationId),
                    "A canceled request queued behind writes and reads must never open a SQLite connection.");
            }

            phase = "remaining mixed worker recovery";
            foreach (var id in heldIds.Where(id => id != "hold-write-a"))
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, id), "release");
            foreach (var request in activeRequests.Skip(1))
            {
                using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "Every remaining mixed read/write operation should finish after its barrier releases.");
            }
            var finalEvents = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
            AssertTrue(activeOperationIds.Values.All(id => SqliteAsyncEventSequence(finalEvents, "connection-closing", id) > 0),
                "Every admitted read or write must finish teardown before returning its shared worker slot.");

            assertionsCompleted = true;
        }
        catch (Exception error)
        {
            var eventsPath = Path.Combine(testDirectory, "events.log");
            var eventSummary = File.Exists(eventsPath)
                ? string.Join(" | ", await ReadSqliteAsyncEventLogLinesAsync(eventsPath))
                : "(no event log)";
            throw new InvalidOperationException(
                $"Async SQLite write runtime failed during {phase}: {error.Message}. Events: {eventSummary}", error);
        }
        finally
        {
            foreach (var id in heldIds.Append("hold-abort"))
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, id), "release");
            if (abortOperationId is not null)
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-callback-" + abortOperationId), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(termination.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The async SQLite write fixture host did not stop within 10 seconds."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static async Task TestSqliteAsyncExecutePreCommitDeadline(Harness harness)
    {
        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-write-precommit-deadline");
        Directory.CreateDirectory(testDirectory);
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-async-write-precommit-deadline",
            WriteRuntimeManifest("sqlite-async-write-precommit-deadline", requestTimeoutMs: 2500),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = AsyncWriteRuntimeModule,
                ["db/schema.sql"] = AsyncWriteRuntimeSchema
            });
        var environment = WriteTestEnvironment(testDirectory, Path.Combine(testDirectory, "precommit.sqlite3"));
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-write-precommit-deadline", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        const string id = "deadline-before-commit";
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "precommit-barrier-armed"), "armed");
            var request = client.GetAsync("/write/" + id, HttpCompletionOption.ResponseHeadersRead);
            var operationId = await WaitForSqlOperationAsync(testDirectory, id);
            var eventsPath = Path.Combine(testDirectory, "events.log");
            await WaitForEventAsync(eventsPath, "before-commit", operationId);
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", operationId), TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-before-commit-" + operationId), "release");

            using (var timedOut = await request.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                AssertEqual(HttpStatusCode.GatewayTimeout, timedOut.StatusCode,
                    "A request deadline before the commit boundary should return the stable 504 response.");
                AssertTrue((await timedOut.Content.ReadAsStringAsync()).Contains("request_timeout", StringComparison.Ordinal),
                    "The precommit deadline should preserve the stable request-timeout response body.");
            }
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "provider-result-canceled", operationId), TimeSpan.FromSeconds(10));
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", operationId), TimeSpan.FromSeconds(10));
            AssertTrue(!await IsSamplePresentAsync(client, id), "A deadline before commit_started must roll back the write.");
            using (var followup = await client.GetAsync("/read/normal").WaitAsync(TimeSpan.FromSeconds(10)))
                AssertEqual(HttpStatusCode.NotFound, followup.StatusCode,
                    "The worker capacity should be available to a later read after precommit cancellation.");
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
                catch (OperationCanceledException) { throw new TimeoutException("The precommit-deadline SQLite write host did not stop within 10 seconds."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static async Task TestSqliteAsyncExecuteLateCancellation(Harness harness, bool commitFailure)
    {
        var label = commitFailure ? "commit-error" : "commit-ok";
        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-write-late-" + label);
        Directory.CreateDirectory(testDirectory);
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-async-write-late-" + label,
            WriteRuntimeManifest("sqlite-async-write-late-" + label, requestTimeoutMs: 2500),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = AsyncWriteRuntimeModule,
                ["db/schema.sql"] = AsyncWriteRuntimeSchema
            });
        var environment = WriteTestEnvironment(testDirectory, Path.Combine(testDirectory, "late.sqlite3"));
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-write-late-" + label, packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        var id = commitFailure ? "orphan-late" : "late-committed";
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "commit-barrier-armed"), "armed");
            var path = commitFailure ? "/deferred/" + id : "/write/" + id;
            using var lateCancellation = new CancellationTokenSource();
            var request = client.GetAsync(path, HttpCompletionOption.ResponseHeadersRead,
                commitFailure ? CancellationToken.None : lateCancellation.Token);
            var operationId = await WaitForEventOperationAsync(testDirectory, "commit-started", TimeSpan.FromSeconds(10));
            if (!commitFailure)
                lateCancellation.Cancel();
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt-skipped", operationId),
                TimeSpan.FromSeconds(commitFailure ? 10 : 10));

            var releasePath = Path.Combine(testDirectory, "release-commit-" + operationId);
            var eventsPath = Path.Combine(testDirectory, "events.log");
            await File.WriteAllTextAsync(releasePath, "release");
            try
            {
                await WaitForEventAsync(eventsPath, "commit-barrier-released", operationId, TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException error)
            {
                var eventLines = File.Exists(eventsPath) ? await ReadSqliteAsyncEventLogLinesAsync(eventsPath) : [];
                var stdoutSnapshot = stdoutTask.IsCompletedSuccessfully ? await stdoutTask : "<not complete>";
                var stderrSnapshot = stderrTask.IsCompletedSuccessfully ? await stderrTask : "<not complete>";
                throw new TimeoutException(
                    $"Late SQLite commit barrier did not observe its release: operationId={JsonSerializer.Serialize(operationId)} "
                    + $"operationIdLength={operationId.Length} releaseExists={File.Exists(releasePath)} "
                    + $"processExited={process.HasExited} stdout={JsonSerializer.Serialize(stdoutSnapshot)} "
                    + $"stderr={JsonSerializer.Serialize(stderrSnapshot)} events={string.Join(" | ", eventLines)}",
                    error);
            }

            if (commitFailure)
            {
                using var timedOut = await request.WaitAsync(TimeSpan.FromSeconds(10));
                AssertEqual(HttpStatusCode.GatewayTimeout, timedOut.StatusCode,
                    "The request deadline may end the HTTP response after the SQLite commit boundary.");
                AssertTrue((await timedOut.Content.ReadAsStringAsync()).Contains("request_timeout", StringComparison.Ordinal),
                    "The deadline response should preserve the stable timeout body.");
            }
            else
                await AssertHttpRequestCanceledAsync(request,
                    "A client abort after commit_started should cancel HTTP delivery while the provider still completes.");

            var providerPhase = commitFailure ? "provider-result-error" : "provider-result-ok";
            await WaitForEventAsync(Path.Combine(testDirectory, "events.log"), providerPhase, operationId,
                TimeSpan.FromSeconds(10));
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", operationId), TimeSpan.FromSeconds(10));
            var events = await ReadSqliteAsyncEventLogLinesAsync(Path.Combine(testDirectory, "events.log"));
            AssertTrue(SqliteAsyncEventSequence(events, "commit-started", operationId)
                       < SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                       && SqliteAsyncEventSequence(events, "interrupt-skipped", operationId)
                       < SqliteAsyncEventSequence(events, providerPhase, operationId),
                "A late cancellation callback must run after commit_started and leave the authoritative provider outcome intact.");

            if (commitFailure)
            {
                AssertEqual(HttpStatusCode.NotFound, (await client.GetAsync("/child/" + id)).StatusCode,
                    "A commit failure after late cancellation must return Err and roll back the child row.");
                using var audit = await client.GetAsync("/audit/" + id);
                AssertEqual("0", await audit.Content.ReadAsStringAsync(),
                    "A failed commit must also roll back its trigger side effect after a late deadline.");
            }
            else
            {
                AssertTrue(await IsSamplePresentAsync(client, id),
                    "A successful commit after a client abort must persist even though HTTP delivery was canceled.");
            }

            assertionsCompleted = true;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-commit-all"), "release");
            foreach (var operationId in Directory.Exists(testDirectory)
                         ? Directory.GetFiles(testDirectory, "commit-started.*.marker").Select(path => Path.GetFileName(path)!.Split('.')[1])
                         : [])
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-commit-" + operationId), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await process.WaitForExitAsync(termination.Token); }
                catch (OperationCanceledException) { throw new TimeoutException("The late-cancellation SQLite write host did not stop within 10 seconds."); }
            }
            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static async Task AssertAsyncSqliteAuditOperationAsync(Harness harness, string packageRoot, string operation, string effect)
    {
        var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        using var document = JsonDocument.Parse(audit.StandardOutput);
        AssertTrue(document.RootElement.GetProperty("trusted_claims").EnumerateArray().Any(claim =>
                claim.GetProperty("operation").GetString() == operation
                && claim.GetProperty("effects").EnumerateArray().Any(value => value.GetString() == effect)),
            $"The audit should include {operation} with effect {effect}.");
    }

    private static void AssertSqliteReportDependencies(ProcessResult result, string subject)
    {
        AssertEqual(0, result.ExitCode, Describe(result));
        using var document = JsonDocument.Parse(result.StandardOutput);
        AssertSqliteDependencyArray(document.RootElement.GetProperty("foreign_dependencies"), subject);
    }

    private static void AssertSqliteDependencyManifest(string path, string subject)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var libraries = document.RootElement.GetProperty("libraries");
        AssertTrue(libraries.TryGetProperty("Microsoft.Data.Sqlite/10.0.12", out _),
            subject + " should include the pinned managed SQLite provider.");
        AssertTrue(libraries.TryGetProperty("SQLitePCLRaw.core/2.1.12", out _),
            subject + " should include the pinned native interrupt dependency.");
    }

    private static void AssertSqliteReceiptDependencies(string path, string text, string subject)
    {
        AssertTrue(File.Exists(path), subject + " should be written with the successful build.");
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        AssertEqual(4, root.GetProperty("schema_version").GetInt32(), subject + " must preserve receipt schema 4.");
        var dependencies = root.GetProperty("foreign_dependencies");
        var toolchainDependencies = root.GetProperty("toolchain").GetProperty("foreign_dependencies");
        AssertSqliteDependencyArray(dependencies, subject);
        AssertSqliteDependencyArray(toolchainDependencies, subject + " toolchain");
    }

    private static void AssertSqliteDependencyArray(JsonElement dependencies, string subject)
    {
        var actual = dependencies.EnumerateArray().ToArray();
        AssertEqual(2, actual.Length, subject + " should contain exactly two generated SQLite dependencies.");
        var expected = new[]
        {
            (Name: "Microsoft.Data.Sqlite", Version: "10.0.12"),
            (Name: "SQLitePCLRaw.core", Version: "2.1.12")
        };
        for (var index = 0; index < expected.Length; index++)
        {
            AssertEqual(expected[index].Name, actual[index].GetProperty("name").GetString(),
                subject + " should preserve generated dependency order.");
            AssertEqual(expected[index].Version, actual[index].GetProperty("version").GetString(),
                subject + " should use the pinned dependency version.");
            AssertEqual("nuget", actual[index].GetProperty("ecosystem").GetString(),
                subject + " should identify NuGet as the dependency ecosystem.");
            AssertEqual("generated_build", actual[index].GetProperty("reason").GetString(),
                subject + " should identify generated-build provenance.");
        }
    }

    private static async Task<bool> IsSamplePresentAsync(HttpClient client, string id)
    {
        using var response = await client.GetAsync("/read/" + Uri.EscapeDataString(id));
        return response.StatusCode == HttpStatusCode.OK;
    }

    private static async Task<string> WaitForSqlOperationAsync(string directory, string key)
    {
        var marker = SqliteAsyncMarker(directory, "sql-active", key);
        return await WaitForNonEmptyFileContentAsync(marker, TimeSpan.FromSeconds(10));
    }

    private static async Task<string> WaitForEventOperationAsync(string directory, string phase, TimeSpan timeout)
    {
        var eventPath = Path.Combine(directory, "events.log");
        await WaitForEventAsync(eventPath, phase, operationId: null, timeout);
        return await LatestOperationIdAsync(eventPath, phase);
    }

    private static async Task WaitForEventAsync(string path, string phase, string? operationId, TimeSpan? timeout = null)
    {
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));
        try
        {
            while (true)
            {
                if (File.Exists(path))
                {
                    var matches = (await ReadSqliteAsyncEventLogLinesAsync(path, cancellation.Token))
                        .Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == phase
                            && (operationId is null || parts[2] == operationId));
                    if (matches) return;
                }
                await Task.Delay(20, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            var events = File.Exists(path) ? await ReadSqliteAsyncEventLogLinesAsync(path) : [];
            var observed = string.Join(" | ", events);
            throw new TimeoutException($"Timed out waiting for SQLite async event '{phase}'"
                + (operationId is null ? string.Empty : $" for operation {operationId}")
                + $". Events: {observed}");
        }
    }

    private static async Task<string> LatestOperationIdAsync(string path, string phase)
    {
        var events = await ReadSqliteAsyncEventLogLinesAsync(path);
        return events.Select(line => line.Split('\t'))
            .Where(parts => parts.Length == 4 && parts[1] == phase)
            .Select(parts => parts[2])
            .LastOrDefault()
            ?? throw new InvalidOperationException($"Expected SQLite async event '{phase}'. Events: {string.Join(" | ", events)}");
    }

    private static string WriteRuntimeManifest(string name, int requestTimeoutMs) =>
        $"name = \"{name}\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n"
        + "sqlite_path = \"data/writes.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n"
        + $"request_timeout_ms = {requestTimeoutMs}\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n";

    private static Dictionary<string, string> WriteTestEnvironment(string testDirectory, string databasePath, bool registrationBarrier = false)
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_SQLITE_ASYNC_TEST_HOOKS"] = "true",
            ["HOB_SQLITE_ASYNC_TEST_DIR"] = testDirectory,
            ["HOB_SQLITE_PATH"] = databasePath
        };
        if (registrationBarrier)
            environment["HOB_SQLITE_ASYNC_TEST_REGISTRATION_BARRIER"] = "true";
        return environment;
    }

    private const string AsyncWriteRuntimeSchema = """
        CREATE TABLE IF NOT EXISTS sample (id TEXT NOT NULL PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS parent (id TEXT NOT NULL PRIMARY KEY);
        CREATE TABLE IF NOT EXISTS child (parent_id TEXT NOT NULL REFERENCES parent(id) DEFERRABLE INITIALLY DEFERRED);
        CREATE TABLE IF NOT EXISTS audit (id TEXT NOT NULL);
        CREATE TRIGGER IF NOT EXISTS sample_failure AFTER INSERT ON sample WHEN NEW.id = 'statement-failure' BEGIN
            INSERT INTO audit (id) VALUES (NEW.id);
            SELECT RAISE(ABORT, 'forced statement failure');
        END;
        CREATE TRIGGER IF NOT EXISTS child_audit AFTER INSERT ON child BEGIN
            INSERT INTO audit (id) VALUES (NEW.parent_id);
        END;
        """;

    private const string AsyncWriteRuntimeModule = """
        module app::main;
        struct Parameters { id: Text }
        struct Row { id: Text }
        struct CountRow { count: i32 }
        union WriteReply { Written(i32), Failure(Text) }
        union ReadReply { Found(self::app::main::Row), Missing, Failure(Text) }
        union CountReply { Counted(i32), Failure(Text) }
        union HealthReply { Healthy }

        fn health() -> self::app::main::HealthReply effects {} {
            return self::app::main::HealthReply.Healthy;
        }

        async fn write(id: Text, db: DbWrite) -> self::app::main::WriteReply effects { db.write } {
            let saved: Result<i32, DbError> = await db.execute_async(
                "INSERT INTO sample (id) VALUES (hob_test_pause($id))",
                self::app::main::Parameters { id: id }
            );
            return match saved {
                Ok(count) => self::app::main::WriteReply.Written(count),
                Err(error) => match error {
                    DbError.Statement => self::app::main::WriteReply.Failure("statement"),
                    DbError.RowShape => self::app::main::WriteReply.Failure("row-shape")
                }
            };
        }

        async fn deferred_write(id: Text, db: DbWrite) -> self::app::main::WriteReply effects { db.write } {
            let saved: Result<i32, DbError> = await db.execute_async(
                "INSERT INTO child (parent_id) VALUES ($id)",
                self::app::main::Parameters { id: id }
            );
            return match saved {
                Ok(count) => self::app::main::WriteReply.Written(count),
                Err(error) => match error {
                    DbError.Statement => self::app::main::WriteReply.Failure("statement"),
                    DbError.RowShape => self::app::main::WriteReply.Failure("row-shape")
                }
            };
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

        async fn child(id: Text, db: DbRead) -> self::app::main::ReadReply effects { db.read } {
            let loaded: Result<Option<self::app::main::Row>, DbError> = await db.query_one_async(
                "SELECT parent_id AS id FROM child WHERE parent_id = $id",
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
        route GET "/write/{id}" {
            path id: Text;
            handler: self::app::main::write;
            response Written: 200 json i32;
            response Failure: 500 json Text;
        }
        route GET "/deferred/{id}" {
            path id: Text;
            handler: self::app::main::deferred_write;
            response Written: 200 json i32;
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
        route GET "/child/{id}" {
            path id: Text;
            handler: self::app::main::child;
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
