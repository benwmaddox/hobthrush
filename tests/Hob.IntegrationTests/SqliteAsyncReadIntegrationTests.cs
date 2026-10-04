using System.Net;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestSqliteAsyncQueryOne(Harness harness)
    {
        var unawaited = """
            module app::async_sqlite;
            struct Parameters { id: i32 }
            struct Row { id: i32 }
            fn read(db: DbRead) -> bool effects { db.read } {
                let loaded: Result<Option<self::app::async_sqlite::Row>, DbError> = db.query_one_async(
                    "SELECT id FROM sample WHERE id = $id",
                    self::app::async_sqlite::Parameters { id: 1 }
                );
                return true;
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-read-unawaited", unawaited, "E_ASYNC_CALL_UNAWAITED");

        var directTransaction = """
            module app::async_sqlite;
            struct Parameters { id: i32 }
            struct Row { id: i32 }
            async fn read(db: DbRead) -> Result<Option<self::app::async_sqlite::Row>, DbError> effects { db.read } {
                let loaded: Result<Option<self::app::async_sqlite::Row>, DbError> = await db.query_one_async(
                    "SELECT id FROM sample WHERE id = $id",
                    self::app::async_sqlite::Parameters { id: 1 }
                );
                return loaded;
            }
            async fn run(db: DbWrite, reader: DbRead) -> Result<Option<self::app::async_sqlite::Row>, DbError> effects { db.read, db.write } {
                with db.begin() as tx {
                    let loaded: Result<Option<self::app::async_sqlite::Row>, DbError> = await reader.query_one_async(
                        "SELECT id FROM sample WHERE id = $id",
                        self::app::async_sqlite::Parameters { id: 1 }
                    );
                    return loaded;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-read-direct-transaction", directTransaction, "E_DB_ASYNC_IN_TRANSACTION");

        var transitiveTransaction = """
            module app::async_sqlite;
            struct Parameters { id: i32 }
            struct Row { id: i32 }
            async fn read(db: DbRead) -> Result<Option<self::app::async_sqlite::Row>, DbError> effects { db.read } {
                let loaded: Result<Option<self::app::async_sqlite::Row>, DbError> = await db.query_one_async(
                    "SELECT id FROM sample WHERE id = $id",
                    self::app::async_sqlite::Parameters { id: 1 }
                );
                return loaded;
            }
            async fn run(db: DbWrite, reader: DbRead) -> Result<Option<self::app::async_sqlite::Row>, DbError> effects { db.read, db.write } {
                with db.begin() as tx {
                    let loaded: Result<Option<self::app::async_sqlite::Row>, DbError> = await self::app::async_sqlite::read(reader);
                    return loaded;
                }
            }
            """;
        await ExpectDiagnosticsAsync(harness, "sqlite-async-read-transitive-transaction", transitiveTransaction,
            "E_DB_ASYNC_IN_TRANSACTION");

        const string module = """
            module app::main;
            struct Parameters { id: Text }
            struct Row { id: Text }
            union ProbeReply { Found(self::app::main::Row), Missing, Failure(Text) }
            union HealthReply { Healthy }

            async fn probe(id: Text, db: DbRead) -> self::app::main::ProbeReply effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = await db.query_one_async(
                    "SELECT hob_test_pause($id) AS id WHERE $id <> 'missing'",
                    self::app::main::Parameters { id: id }
                );
                return match loaded {
                    Ok(value) => match value {
                        Some(row) => self::app::main::ProbeReply.Found(row),
                        None => self::app::main::ProbeReply.Missing
                    },
                    Err(error) => match error {
                        DbError.Statement => self::app::main::ProbeReply.Failure("statement"),
                        DbError.RowShape => self::app::main::ProbeReply.Failure("row-shape")
                    }
                };
            }

            fn health() -> self::app::main::HealthReply effects {} {
                return self::app::main::HealthReply.Healthy;
            }

            route GET "/" {
                handler: self::app::main::health;
                response Healthy: 200;
            }

            route GET "/probe/{id}" {
                path id: Text;
                handler: self::app::main::probe;
                response Found: 200 json self::app::main::Row;
                response Missing: 404;
                response Failure: 500 json Text;
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-async-read-runtime",
            "name = \"sqlite-async-read-runtime\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/probe.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = module,
                ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS sample (id TEXT NOT NULL);\n"
            });

        var testDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-test-hooks");
        Directory.CreateDirectory(testDirectory);
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_SQLITE_ASYNC_TEST_HOOKS"] = "true",
            ["HOB_SQLITE_ASYNC_TEST_REGISTRATION_BARRIER"] = "true",
            ["HOB_SQLITE_ASYNC_TEST_DIR"] = testDirectory,
            ["HOB_SQLITE_PATH"] = Path.Combine(testDirectory, "async.sqlite3")
        };
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-read-runtime", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        var heldIds = new[] { "hold-a", "hold-b", "hold-c", "hold-d" };
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);

            using (var registrationCancellation = new CancellationTokenSource())
            {
                var registrationRequest = client.GetAsync("/probe/register-cancel", HttpCompletionOption.ResponseHeadersRead,
                    registrationCancellation.Token);
                var registeredMarker = Path.Combine(testDirectory, "registered.entered");
                await WaitForFileAsync(registeredMarker, TimeSpan.FromSeconds(10));
                var registrationOperationId = await File.ReadAllTextAsync(registeredMarker);
                registrationCancellation.Cancel();
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", registrationOperationId),
                    TimeSpan.FromSeconds(10));
                await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-registered-" + registrationOperationId), "release");
                await AssertHttpRequestCanceledAsync(registrationRequest,
                    "Cancellation after callback registration should stop before the first SQL statement.");
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", registrationOperationId),
                    TimeSpan.FromSeconds(10));

                var registrationEvents = await File.ReadAllLinesAsync(Path.Combine(testDirectory, "events.log"));
                AssertTrue(!registrationEvents.Select(line => line.Split('\t'))
                        .Any(parts => parts.Length == 4 && parts[1] == "sql-active" && parts[2] == registrationOperationId),
                    "A cancellation observed after callback registration must not execute guest SQL.");
                var registrationCallbackQuiesced = SqliteAsyncEventSequence(registrationEvents, "callback-quiesced", registrationOperationId);
                var registrationProgressCleared = SqliteAsyncEventSequence(registrationEvents, "progress-handler-cleared", registrationOperationId);
                var registrationConnectionClosing = SqliteAsyncEventSequence(registrationEvents, "connection-closing", registrationOperationId);
                AssertTrue(registrationCallbackQuiesced < registrationProgressCleared && registrationProgressCleared < registrationConnectionClosing,
                    "Pre-execution cancellation must quiesce its callback and clear the progress handler before closing SQLite.");
            }

            var productionCompilerPath = Path.Combine(harness.RepositoryRoot, "src", "Hob", "bin", "Release", "net10.0", "hob.dll");
            AssertTrue(File.Exists(productionCompilerPath), "The standard production compiler build must exist for hook-exclusion verification.");
            var productionCompilerBytes = await File.ReadAllBytesAsync(productionCompilerPath);
            var productionCompilerText = Encoding.UTF8.GetString(productionCompilerBytes) + Encoding.Unicode.GetString(productionCompilerBytes);
            AssertTrue(!productionCompilerText.Contains("HOB_SQLITE_ASYNC_TEST_HOOKS", StringComparison.Ordinal),
                "The normal production compiler must not expose an environment switch that activates SQLite test hooks.");

            using var activeCancellation = new CancellationTokenSource();
            var activeRequest = client.GetAsync("/probe/hold-a", HttpCompletionOption.ResponseHeadersRead, activeCancellation.Token);
            var otherRequests = heldIds.Skip(1)
                .Select(id => client.GetAsync("/probe/" + id, HttpCompletionOption.ResponseHeadersRead))
                .ToArray();
            foreach (var id in heldIds)
            {
                await WaitForFileAsync(SqliteAsyncMarker(testDirectory, "sql-active", id), TimeSpan.FromSeconds(10));
                var operationId = await File.ReadAllTextAsync(SqliteAsyncMarker(testDirectory, "sql-active", id));
                await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "pause-entered", operationId),
                    TimeSpan.FromSeconds(10));
            }

            var activeEvents = await File.ReadAllLinesAsync(Path.Combine(testDirectory, "events.log"));
            var activeOperationId = activeEvents.Select(line => line.Split('\t'))
                .Single(parts => parts.Length == 4 && parts[1] == "sql-active" && parts[3] == "hold-a")[2];
            AssertEqual(4, activeEvents
                .Count(line => line.Split('\t') is [_, "sql-active", _, _]),
                "Four held queries should execute inside SQLite at the configured admission limit.");

            using var queuedCancellation = new CancellationTokenSource();
            var queuedRequest = client.GetAsync("/probe/queued-fifth", HttpCompletionOption.ResponseHeadersRead, queuedCancellation.Token);
            var queuedMarker = Path.Combine(testDirectory, "queued.entered");
            await WaitForFileAsync(queuedMarker, TimeSpan.FromSeconds(10));
            var queuedOperationId = (await File.ReadAllLinesAsync(Path.Combine(testDirectory, "events.log")))
                .Select(line => line.Split('\t'))
                .Single(parts => parts.Length == 4 && parts[1] == "queued")[2];
            queuedCancellation.Cancel();
            await AssertHttpRequestCanceledAsync(queuedRequest, "The fifth async SQLite request should cancel while waiting for a worker slot.");

            await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-b"), "release");
            using (var released = await otherRequests[0].WaitAsync(TimeSpan.FromSeconds(10)))
            {
                AssertEqual(HttpStatusCode.OK, released.StatusCode, "A held query should finish after its barrier is released.");
                AssertTrue((await released.Content.ReadAsStringAsync()).Contains("hold-b", StringComparison.Ordinal),
                    "The async query should preserve the typed row value after waiting for SQLite.");
            }
            var queuedOpenedMarker = Path.Combine(testDirectory, "opened." + queuedOperationId + ".marker");
            try
            {
                await WaitForFileAsync(queuedOpenedMarker, TimeSpan.FromMilliseconds(350));
                throw new InvalidOperationException("The canceled fifth query opened a SQLite connection after a worker slot became available.");
            }
            catch (TimeoutException)
            {
                AssertTrue(!File.Exists(queuedOpenedMarker),
                    "Canceling a queued request must prevent connection creation after capacity is released.");
            }

            await File.WriteAllTextAsync(Path.Combine(testDirectory, "callback-barrier-armed"), "armed");
            activeCancellation.Cancel();
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "interrupt", activeOperationId), TimeSpan.FromSeconds(10));
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-entered", activeOperationId), TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, "hold-a"), "release");
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "callback-dispose-started", activeOperationId), TimeSpan.FromSeconds(10));
            var heldCallbackEvents = await File.ReadAllLinesAsync(Path.Combine(testDirectory, "events.log"));
            AssertTrue(!heldCallbackEvents.Select(line => line.Split('\t'))
                    .Any(parts => parts.Length == 4 && parts[2] == activeOperationId
                        && parts[1] is "callback-quiesced" or "progress-handler-cleared" or "connection-closing"),
                "Connection teardown must wait while the native interrupt callback remains in flight.");
            await File.WriteAllTextAsync(Path.Combine(testDirectory, "release-callback-" + activeOperationId), "release");
            await AssertHttpRequestCanceledAsync(activeRequest, "The active HTTP request should be aborted while its SQLite statement is running.");
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "sqlite-interrupted", activeOperationId), TimeSpan.FromSeconds(10));
            await WaitForFileAsync(SqliteAsyncOperationMarker(testDirectory, "connection-closing", activeOperationId), TimeSpan.FromSeconds(10));

            var events = await File.ReadAllLinesAsync(Path.Combine(testDirectory, "events.log"));
            var callbackReturning = SqliteAsyncEventSequence(events, "callback-returning", activeOperationId);
            var callbackQuiesced = SqliteAsyncEventSequence(events, "callback-quiesced", activeOperationId);
            var progressCleared = SqliteAsyncEventSequence(events, "progress-handler-cleared", activeOperationId);
            var connectionClosing = SqliteAsyncEventSequence(events, "connection-closing", activeOperationId);
            AssertTrue(callbackReturning < callbackQuiesced && callbackQuiesced < progressCleared && progressCleared < connectionClosing,
                "The cancellation callback must quiesce, then clear the SQLite progress handler, before the connection closes.");

            using (var followup = await client.GetAsync("/probe/followup").WaitAsync(TimeSpan.FromSeconds(10)))
            {
                AssertEqual(HttpStatusCode.OK, followup.StatusCode,
                    "A later query should complete after cancellation releases the async SQLite worker slot.");
                AssertTrue((await followup.Content.ReadAsStringAsync()).Contains("followup", StringComparison.Ordinal),
                    "A later query should preserve its decoded row value.");
            }

            foreach (var id in new[] { "hold-c", "hold-d" })
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, id), "release");
            foreach (var request in otherRequests.Skip(1))
            {
                using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
                AssertEqual(HttpStatusCode.OK, response.StatusCode, "Remaining held queries should complete after their barriers are released.");
            }

            using (var missing = await client.GetAsync("/probe/missing"))
                AssertEqual(HttpStatusCode.NotFound, missing.StatusCode, "A SELECT that returns no row should remain Ok(None).");
            using (var rowShape = await client.GetAsync("/probe/row-shape"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, rowShape.StatusCode,
                    "A NULL value in a required Text column should map to DbError.RowShape.");
                AssertTrue((await rowShape.Content.ReadAsStringAsync()).Contains("row-shape", StringComparison.Ordinal),
                    "The checked route should distinguish a row-shape error from a statement failure.");
            }
            using (var statement = await client.GetAsync("/probe/statement"))
            {
                AssertEqual(HttpStatusCode.InternalServerError, statement.StatusCode,
                    "A SQLite function error should map to DbError.Statement.");
                AssertTrue((await statement.Content.ReadAsStringAsync()).Contains("statement", StringComparison.Ordinal),
                    "The checked route should preserve the typed statement-error branch.");
            }

            var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, audit.ExitCode, Describe(audit));
            using (var report = JsonDocument.Parse(audit.StandardOutput))
            {
                var dependencies = report.RootElement.GetProperty("foreign_dependencies").EnumerateArray().ToArray();
                AssertEqual(2, dependencies.Length, "Async SQLite audit must record both explicitly pinned generated dependencies.");
                AssertTrue(dependencies.Any(dependency => dependency.GetProperty("name").GetString() == "Microsoft.Data.Sqlite"
                    && dependency.GetProperty("version").GetString() == "10.0.12"), "The managed SQLite dependency must remain pinned.");
                AssertTrue(dependencies.Any(dependency => dependency.GetProperty("name").GetString() == "SQLitePCLRaw.core"
                    && dependency.GetProperty("version").GetString() == "2.1.12"), "The native interruption API dependency must be explicitly pinned.");
            }

            var productionBuild = await harness.InvokePackageDirectoryAsync("sqlite-async-read-production-build", packageRoot, "build");
            var productionArtifact = ParseBuiltArtifact(productionBuild, "Built executable: ");
            var dependencyManifestPath = Path.ChangeExtension(productionArtifact, ".deps.json");
            AssertTrue(File.Exists(dependencyManifestPath), "The production SQLite async artifact must include its dependency manifest.");
            using (var dependencyManifest = JsonDocument.Parse(await File.ReadAllTextAsync(dependencyManifestPath)))
            {
                var libraries = dependencyManifest.RootElement.GetProperty("libraries");
                foreach (var (name, version) in new[]
                         {
                             ("Microsoft.Data.Sqlite", "10.0.12"),
                             ("SQLitePCLRaw.core", "2.1.12")
                         })
                {
                    AssertTrue(libraries.TryGetProperty($"{name}/{version}", out _),
                        $"The production SQLite async artifact must pin {name}/{version} in its dependency manifest.");
                }
            }

            var receiptPath = Path.Combine(Path.GetDirectoryName(productionArtifact)!, "build-receipt.json");
            AssertTrue(File.Exists(receiptPath), "The production SQLite async build must retain its build receipt.");
            using (var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(receiptPath)))
            {
                var expectedDependencies = new[]
                {
                    (Name: "Microsoft.Data.Sqlite", Version: "10.0.12"),
                    (Name: "SQLitePCLRaw.core", Version: "2.1.12")
                };
                foreach (var dependencyArray in new[]
                         {
                             receipt.RootElement.GetProperty("foreign_dependencies"),
                             receipt.RootElement.GetProperty("toolchain").GetProperty("foreign_dependencies")
                         })
                {
                    var dependencies = dependencyArray.EnumerateArray().ToArray();
                    foreach (var expected in expectedDependencies)
                    {
                        AssertTrue(dependencies.Any(dependency =>
                                dependency.GetProperty("name").GetString() == expected.Name
                                && dependency.GetProperty("version").GetString() == expected.Version),
                            $"The production SQLite async build receipt must record {expected.Name}/{expected.Version}.");
                    }
                }
            }

            var productionAssemblyBytes = await File.ReadAllBytesAsync(productionArtifact);
            var productionAssembly = Encoding.UTF8.GetString(productionAssemblyBytes) + Encoding.Unicode.GetString(productionAssemblyBytes);
            foreach (var testOnlySymbol in new[]
                     {
                         "DatabaseAsyncTestBeforeExecution",
                         "DatabaseAsyncTestRecord",
                         "DatabaseAsyncTestPause",
                         "HOB_SQLITE_ASYNC_TEST_DIR"
                     })
            {
                AssertTrue(!productionAssembly.Contains(testOnlySymbol, StringComparison.Ordinal),
                    $"Production SQLite async output must not contain test-only hook '{testOnlySymbol}'.");
            }

            assertionsCompleted = true;
        }
        finally
        {
            foreach (var id in heldIds)
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(testDirectory, id), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The async SQLite fixture host did not stop within 10 seconds.");
                }
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }

        await TestSqliteAsyncRequestDeadline(harness, module);
    }

    private static async Task TestSqliteAsyncRequestDeadline(Harness harness, string module)
    {
        var deadlineDirectory = Path.Combine(harness.TemporaryRoot, "sqlite-async-deadline-hooks");
        Directory.CreateDirectory(deadlineDirectory);
        var packageRoot = await harness.WritePackageAsync(
            "sqlite-async-deadline-runtime",
            "name = \"sqlite-async-deadline-runtime\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\nsqlite_path = \"data/probe.sqlite3\"\nsqlite_schema = \"db/schema.sql\"\nrequest_timeout_ms = 1500\n\n[capabilities]\nnet.listen = \"allow\"\ndb.read = \"allow\"\ndb.write = \"allow\"\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = module,
                ["db/schema.sql"] = "CREATE TABLE IF NOT EXISTS sample (id TEXT NOT NULL);\n"
            });
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_SQLITE_ASYNC_TEST_HOOKS"] = "true",
            ["HOB_SQLITE_ASYNC_TEST_DIR"] = deadlineDirectory,
            ["HOB_SQLITE_PATH"] = Path.Combine(deadlineDirectory, "deadline.sqlite3")
        };
        var deadlineApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, deadlineApi.ExitCode, Describe(deadlineApi));
        using (var report = JsonDocument.Parse(deadlineApi.StandardOutput))
            AssertEqual(1500, report.RootElement.GetProperty("request_timeout_ms").GetInt32(),
                "The SQLite deadline package must expose its configured request timeout before runtime testing.");
        var port = GetUnusedLoopbackPort();
        var address = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(20) };
        using var process = harness.StartWebPackageProcessWithEnvironment(
            "sqlite-async-deadline-runtime", packageRoot, environment, "--urls", address.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var assertionsCompleted = false;
        string? activeOperationId = null;
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);
            await File.WriteAllTextAsync(Path.Combine(deadlineDirectory, "callback-barrier-armed"), "armed");
            var request = client.GetAsync("/probe/hold-deadline", HttpCompletionOption.ResponseHeadersRead);
            await WaitForFileAsync(SqliteAsyncMarker(deadlineDirectory, "sql-active", "hold-deadline"), TimeSpan.FromSeconds(10));
            activeOperationId = await File.ReadAllTextAsync(SqliteAsyncMarker(deadlineDirectory, "sql-active", "hold-deadline"));
            await WaitForFileAsync(SqliteAsyncOperationMarker(deadlineDirectory, "pause-entered", activeOperationId),
                TimeSpan.FromSeconds(10));
            try
            {
                await WaitForFileAsync(SqliteAsyncOperationMarker(deadlineDirectory, "interrupt", activeOperationId),
                    TimeSpan.FromSeconds(10));
            }
            catch (TimeoutException)
            {
                await File.WriteAllTextAsync(SqliteAsyncReleasePath(deadlineDirectory, "hold-deadline"), "release");
                using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
                var body = await response.Content.ReadAsStringAsync();
                var timeoutEvents = await File.ReadAllLinesAsync(Path.Combine(deadlineDirectory, "events.log"));
                throw new InvalidOperationException(
                    $"The request deadline did not interrupt active SQLite work; response={(int)response.StatusCode} {body}; events={string.Join(" | ", timeoutEvents)}");
            }
            await WaitForFileAsync(SqliteAsyncOperationMarker(deadlineDirectory, "callback-entered", activeOperationId),
                TimeSpan.FromSeconds(10));
            await File.WriteAllTextAsync(SqliteAsyncReleasePath(deadlineDirectory, "hold-deadline"), "release");
            await WaitForFileAsync(SqliteAsyncOperationMarker(deadlineDirectory, "callback-dispose-started", activeOperationId),
                TimeSpan.FromSeconds(10));
            var pendingEvents = await File.ReadAllLinesAsync(Path.Combine(deadlineDirectory, "events.log"));
            AssertTrue(!pendingEvents.Select(line => line.Split('\t'))
                    .Any(parts => parts.Length == 4 && parts[2] == activeOperationId
                        && parts[1] is "callback-quiesced" or "progress-handler-cleared" or "connection-closing"),
                "The request-deadline worker must wait for its in-flight native callback before connection teardown.");
            await File.WriteAllTextAsync(Path.Combine(deadlineDirectory, "release-callback-" + activeOperationId), "release");

            using (var response = await request.WaitAsync(TimeSpan.FromSeconds(10)))
            {
                AssertEqual(HttpStatusCode.GatewayTimeout, response.StatusCode,
                    "The configured web request deadline should return 504 after canceling its active SQLite read.");
                AssertTrue((await response.Content.ReadAsStringAsync()).Contains("request_timeout", StringComparison.Ordinal),
                    "The deadline response should retain the stable request-timeout body.");
            }

            var events = await File.ReadAllLinesAsync(Path.Combine(deadlineDirectory, "events.log"));
            var sqlActive = SqliteAsyncEventSequence(events, "sql-active", activeOperationId);
            var sqliteInterrupted = SqliteAsyncEventSequence(events, "sqlite-interrupted", activeOperationId);
            var interrupt = SqliteAsyncEventSequence(events, "interrupt", activeOperationId);
            var callbackEntered = SqliteAsyncEventSequence(events, "callback-entered", activeOperationId);
            var callbackDisposeStarted = SqliteAsyncEventSequence(events, "callback-dispose-started", activeOperationId);
            var callbackReturning = SqliteAsyncEventSequence(events, "callback-returning", activeOperationId);
            var callbackQuiesced = SqliteAsyncEventSequence(events, "callback-quiesced", activeOperationId);
            var progressCleared = SqliteAsyncEventSequence(events, "progress-handler-cleared", activeOperationId);
            var connectionClosing = SqliteAsyncEventSequence(events, "connection-closing", activeOperationId);
            AssertTrue(sqlActive < interrupt && interrupt < callbackEntered && callbackEntered < callbackDisposeStarted
                && callbackDisposeStarted < callbackReturning && callbackReturning < callbackQuiesced
                && callbackQuiesced < progressCleared && progressCleared < connectionClosing
                && connectionClosing < sqliteInterrupted,
                $"The HTTP deadline must interrupt an active SQLite statement and quiesce callbacks before connection teardown; " +
                $"the outer provider-error catch records SQLITE_INTERRUPT after the inner cleanup finally. " +
                $"Sequences: active={sqlActive}, interrupt={interrupt}, callback-entered={callbackEntered}, " +
                $"dispose-started={callbackDisposeStarted}, callback-returning={callbackReturning}, " +
                $"quiesced={callbackQuiesced}, progress-cleared={progressCleared}, closing={connectionClosing}. " +
                $"Events: {string.Join(" | ", events)}");
            assertionsCompleted = true;
        }
        finally
        {
            await File.WriteAllTextAsync(SqliteAsyncReleasePath(deadlineDirectory, "hold-deadline"), "release");
            if (activeOperationId is not null)
                await File.WriteAllTextAsync(Path.Combine(deadlineDirectory, "release-callback-" + activeOperationId), "release");
            client.Dispose();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: !assertionsCompleted);
                using var termination = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await process.WaitForExitAsync(termination.Token);
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException("The async SQLite deadline fixture host did not stop within 10 seconds.");
                }
            }

            await Task.WhenAll(stdoutTask, stderrTask);
            await AssertLoopbackPortReleasedAsync(port);
        }
    }

    private static string SqliteAsyncKey(string key) => Convert.ToHexString(Encoding.UTF8.GetBytes(key));

    private static string SqliteAsyncMarker(string directory, string phase, string key) =>
        Path.Combine(directory, phase + "." + SqliteAsyncKey(key) + ".marker");

    private static string SqliteAsyncOperationMarker(string directory, string phase, string operationId) =>
        Path.Combine(directory, phase + "." + operationId + ".marker");

    private static string SqliteAsyncReleasePath(string directory, string key) =>
        Path.Combine(directory, "release-" + SqliteAsyncKey(key));

    private static long SqliteAsyncEventSequence(IReadOnlyList<string> events, string phase, string operationId)
    {
        var matching = events.Select(line => line.Split('\t'))
            .SingleOrDefault(parts => parts.Length == 4 && parts[1] == phase && parts[2] == operationId)
            ?? throw new InvalidOperationException(
                $"Expected SQLite async event '{phase}' for operation {operationId}. Events: {string.Join(" | ", events)}");
        return long.Parse(matching[0], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task AssertHttpRequestCanceledAsync(Task<HttpResponseMessage> request, string message)
    {
        try
        {
            using var response = await request.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (HttpRequestException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }
}
