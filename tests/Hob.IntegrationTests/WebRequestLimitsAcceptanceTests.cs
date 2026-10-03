using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private const string WebRequestLimitsSource = """
        module app::main;

        struct EchoRequest { text: Text }
        union EchoReply { Accepted, Completed }
        union HealthReply { Healthy }

        fn echo(request: self::app::main::EchoRequest, logger: Logger) -> self::app::main::EchoReply effects { log.write } {
            let logged: bool = logger.info("request.body.accepted", "echo handler entered");
            return self::app::main::EchoReply.Accepted;
        }

        fn health() -> self::app::main::HealthReply effects {} {
            return self::app::main::HealthReply.Healthy;
        }

        async fn fetch(path: Text, client: HttpClient) -> self::app::main::EchoReply effects { net.client } {
            let result: Result<HttpResponse, HttpError> = await client.get_text_async(path);
            return match result {
                Ok(response) => self::app::main::EchoReply.Completed,
                Err(error) => self::app::main::EchoReply.Completed
            };
        }

        route POST "/echo" {
            body: self::app::main::EchoRequest;
            handler: self::app::main::echo;
            response Accepted: 204;
            response Completed: 200;
        }

        route GET "/" {
            handler: self::app::main::health;
            response Healthy: 200;
        }

        route GET "/fetch" {
            query path: Text;
            handler: self::app::main::fetch;
            response Accepted: 204;
            response Completed: 200;
        }
        """;

    private static async Task TestConfigurableWebRequestLimits(Harness harness)
    {
        await using var upstream = new RawHttpServer();

        static string WebManifest(string name, string settings = "", string? origin = null, bool withDependency = false)
        {
            var manifest = $"name = \"{name}\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n";
            if (origin is not null)
                manifest += $"http_origin = \"{origin}\"\n";
            manifest += settings;
            manifest += "\n[capabilities]\nnet.listen = \"allow\"\nlog.write = \"allow\"\n";
            if (origin is not null)
                manifest += "net.client = \"allow\"\n";
            if (withDependency)
                manifest += "\n[dependencies]\ncore = \"../core\"\n";
            return manifest;
        }

        static Dictionary<string, string> WebSources() => new(StringComparer.Ordinal)
        {
            ["src/app/main.hob"] = WebRequestLimitsSource
        };

        async Task<JsonElement> ReadApiAsync(string name, string packageRoot)
        {
            var result = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
            AssertEqual(0, result.ExitCode, Describe(result));
            using var document = JsonDocument.Parse(result.StandardOutput);
            AssertInspectApiPropertyOrder(document.RootElement);
            AssertEqual(13, document.RootElement.GetProperty("schema_version").GetInt32(),
                $"{name} should use inspect API schema version 13.");
            return document.RootElement.Clone();
        }

        async Task<JsonElement> ReadAuditAsync(string name, string packageRoot)
        {
            var result = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertEqual(0, result.ExitCode, Describe(result));
            using var document = JsonDocument.Parse(result.StandardOutput);
            AssertAuditPropertyOrder(document.RootElement);
            AssertEqual(11, document.RootElement.GetProperty("schema_version").GetInt32(),
                $"{name} should use audit schema version 11.");
            return document.RootElement.Clone();
        }

        static void AssertSettings(JsonElement report, int? bodyLimit, int? timeoutMs, string reportName)
        {
            var body = report.GetProperty("max_request_body_bytes");
            var timeout = report.GetProperty("request_timeout_ms");
            if (bodyLimit is null)
            {
                AssertEqual(JsonValueKind.Null, body.ValueKind, $"{reportName} must report a null body limit for a non-web root.");
                AssertEqual(JsonValueKind.Null, timeout.ValueKind, $"{reportName} must report a null request deadline for a non-web root.");
                return;
            }

            AssertEqual(bodyLimit.Value, body.GetInt32(), $"{reportName} reported the wrong effective body limit.");
            AssertEqual(timeoutMs!.Value, timeout.GetInt32(), $"{reportName} reported the wrong effective request deadline.");
        }

        async Task AssertManifestRejectedAsync(string name, string manifest, IReadOnlyDictionary<string, string> sources, string expectedMessage)
        {
            var packageRoot = await harness.WritePackageAsync(name, manifest, sources);
            var check = await harness.InvokePackageDirectoryAsync(name + "-check", packageRoot, "check", "--json");
            AssertEqual(1, check.ExitCode, Describe(check));
            var diagnostics = ParseDiagnosticSnapshots(check.StandardOutput);
            AssertTrue(diagnostics.Any(diagnostic => diagnostic.Code == "E_MANIFEST"),
                $"{name} must fail as a manifest error before build/listen. {check.StandardOutput}");
            AssertTrue(diagnostics.Any(diagnostic => diagnostic.Message.Contains(expectedMessage, StringComparison.Ordinal)),
                $"{name} should report '{expectedMessage}'. {check.StandardOutput}");
        }

        var defaultWebRoot = await harness.WritePackageAsync(
            "web-request-limits-defaults",
            WebManifest("web-request-limits-defaults", origin: upstream.Origin),
            WebSources());
        var defaultCheck = await harness.InvokePackageDirectoryAsync("web-request-limits-defaults-check", defaultWebRoot, "check", "--json");
        AssertEqual(0, defaultCheck.ExitCode, Describe(defaultCheck));
        AssertSettings(await ReadApiAsync("default web API", defaultWebRoot), 1_048_576, 30_000, "default web API");
        AssertSettings(await ReadAuditAsync("default web audit", defaultWebRoot), 1_048_576, 30_000, "default web audit");

        foreach (var (name, settings, bodyLimit, timeoutMs) in new[]
                 {
                     ("minimum", "max_request_body_bytes = 1\nrequest_timeout_ms = 1\n", 1, 1),
                     ("maximum", "max_request_body_bytes = 16777216\nrequest_timeout_ms = 300000\n", 16_777_216, 300_000)
                 })
        {
            var packageRoot = await harness.WritePackageAsync(
                "web-request-limits-" + name,
                WebManifest("web-request-limits-" + name, settings, upstream.Origin),
                WebSources());
            var check = await harness.InvokePackageDirectoryAsync("web-request-limits-" + name + "-check", packageRoot, "check", "--json");
            AssertEqual(0, check.ExitCode, Describe(check));
            AssertSettings(await ReadApiAsync(name + " web API", packageRoot), bodyLimit, timeoutMs, name + " web API");
            AssertSettings(await ReadAuditAsync(name + " web audit", packageRoot), bodyLimit, timeoutMs, name + " web audit");
        }

        var invalidRootValues = new (string Key, string Value, string Name)[]
        {
            ("max_request_body_bytes", "0", "body-zero"),
            ("max_request_body_bytes", "01", "body-leading-zero"),
            ("max_request_body_bytes", "+1", "body-sign"),
            ("max_request_body_bytes", "1_0", "body-separator"),
            ("max_request_body_bytes", "\"1\"", "body-quoted"),
            ("max_request_body_bytes", "١", "body-nonascii-digit"),
            ("max_request_body_bytes", "2147483648", "body-overflow"),
            ("max_request_body_bytes", "16777217", "body-over-bound"),
            ("request_timeout_ms", "0", "timeout-zero"),
            ("request_timeout_ms", "01", "timeout-leading-zero"),
            ("request_timeout_ms", "300001", "timeout-over-bound"),
            ("request_timeout_ms", "2147483648", "timeout-overflow")
        };
        foreach (var (key, value, name) in invalidRootValues)
        {
            await AssertManifestRejectedAsync(
                "web-request-limits-invalid-" + name,
                WebManifest("web-request-limits-invalid-" + name, $"{key} = {value}\n", upstream.Origin),
                WebSources(),
                key);
        }

        await AssertManifestRejectedAsync(
            "web-request-limits-duplicate",
            WebManifest("web-request-limits-duplicate", "max_request_body_bytes = 01\nmax_request_body_bytes = 12\n", upstream.Origin),
            WebSources(),
            "Duplicate manifest key 'max_request_body_bytes'");

        const string cliManifestPrefix = "name = \"request-limit-cli\"\nversion = \"0.1.0\"\nkind = \"cli\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n";
        const string cliSource = "module app::main;\nfn main() -> i32 effects {} { return 0; }\n";
        await AssertManifestRejectedAsync(
            "web-request-limits-on-cli",
            cliManifestPrefix + "max_request_body_bytes = 12\nrequest_timeout_ms = 120\n",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = cliSource },
            "only valid for web packages");

        await AssertManifestRejectedAsync(
            "web-request-limits-on-library",
            "name = \"request-limit-library\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\nrequest_timeout_ms = 120\n",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/lib.hob"] = "module app::lib;\npub fn value() -> i32 effects {} { return 1; }\n"
            },
            "only valid for web packages");

        foreach (var (kind, source) in new[] { ("cli", cliSource), ("lib", "module app::lib;\npub fn value() -> i32 effects {} { return 1; }\n") })
        {
            var name = "web-request-limits-nonweb-" + kind;
            var manifest = kind == "cli"
                ? cliManifestPrefix
                : "name = \"request-limit-library\"\nversion = \"0.1.0\"\nkind = \"lib\"\nsource_root = \"src\"\n";
            var packageRoot = await harness.WritePackageAsync(
                name,
                manifest,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [kind == "cli" ? "src/app/main.hob" : "src/app/lib.hob"] = source
                });
            var check = await harness.InvokePackageDirectoryAsync(name + "-check", packageRoot, "check", "--json");
            AssertEqual(0, check.ExitCode, Describe(check));
            AssertSettings(await ReadApiAsync(name + " API", packageRoot), null, null, name + " API");
            AssertSettings(await ReadAuditAsync(name + " audit", packageRoot), null, null, name + " audit");
        }

        var lockRoot = await harness.WritePackageGraphAsync(
            "web-request-limits-lock-freshness",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(
                    WebManifest("web-request-limits-lock-freshness", "max_request_body_bytes = 13\n", withDependency: true),
                    WebSources()),
                ["core"] = new PackageFixture(
                    LibraryPackageManifest("request-limit-lock-core"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/core.hob"] = "module core;\npub fn value() -> i32 effects {} { return 1; }\n"
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "web-request-limits-lock-create", lockRoot, "lock"));
        var changedManifest = await File.ReadAllTextAsync(Path.Combine(lockRoot, "hob.toml"));
        await File.WriteAllTextAsync(
            Path.Combine(lockRoot, "hob.toml"),
            changedManifest.Replace("max_request_body_bytes = 13", "max_request_body_bytes = 14", StringComparison.Ordinal));
        var staleLock = await harness.InvokePackageDirectoryAsync("web-request-limits-lock-stale", lockRoot, "check", "--json");
        AssertEqual(1, staleLock.ExitCode, Describe(staleLock));
        AssertTrue(ParseDiagnosticSnapshots(staleLock.StandardOutput).Any(diagnostic => diagnostic.Code == "E_LOCK"),
            $"Changing a request limit must stale the package lock. {staleLock.StandardOutput}");

        var runtimeRoot = await harness.WritePackageAsync(
            "web-request-limits-runtime",
            WebManifest("web-request-limits-runtime", "max_request_body_bytes = 13\nrequest_timeout_ms = 350\n", upstream.Origin),
            WebSources());
        var runtimeCheck = await harness.InvokePackageDirectoryAsync("web-request-limits-runtime-check", runtimeRoot, "check", "--json");
        AssertEqual(0, runtimeCheck.ExitCode, Describe(runtimeCheck));
        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(5) };
        using var webProcess = harness.StartWebPackageProcess(
            "web-request-limits-runtime", runtimeRoot, "--urls", baseAddress.ToString());
        var stdoutTask = webProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = webProcess.StandardError.ReadToEndAsync();
        try
        {
            await WaitForWebServerAsync(webProcess, client, stdoutTask, stderrTask);

            async Task<(HttpStatusCode Status, string Body)> SendJsonAsync(string json, bool chunked)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
                {
                    Version = HttpVersion.Version11,
                    VersionPolicy = HttpVersionPolicy.RequestVersionExact,
                    Content = chunked
                        ? new UnknownLengthJsonContent(Encoding.UTF8.GetBytes(json))
                        : new StringContent(json, Encoding.UTF8, "application/json")
                };
                if (chunked)
                {
                    request.Headers.TransferEncodingChunked = true;
                    AssertEqual(null, request.Content.Headers.ContentLength,
                        "The chunked request fixture must not publish a Content-Length.");
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                return (response.StatusCode, await response.Content.ReadAsStringAsync());
            }

            var underLimit = await SendJsonAsync("{\"text\":\"a\"}", chunked: false);
            AssertEqual(HttpStatusCode.NoContent, underLimit.Status, "A POST body below the byte cap should reach the handler.");
            var exactMultibyteLimit = await SendJsonAsync("{\"text\":\"é\"}", chunked: false);
            AssertEqual(HttpStatusCode.NoContent, exactMultibyteLimit.Status,
                "A multibyte UTF-8 POST body exactly at the byte cap should reach the handler.");
            var overLimit = await SendJsonAsync("{\"text\":\"éx\"}", chunked: false);
            AssertEqual(HttpStatusCode.RequestEntityTooLarge, overLimit.Status,
                "A POST body over the byte cap must be rejected before handler entry.");
            AssertEqual("{\"error\":\"payload_too_large\"}", overLimit.Body,
                "Oversized POST bodies should retain the stable JSON error envelope.");
            var chunkedExact = await SendJsonAsync("{\"text\":\"é\"}", chunked: true);
            AssertEqual(HttpStatusCode.NoContent, chunkedExact.Status,
                "A chunked POST body exactly at the byte cap should be accepted without Content-Length.");
            var chunkedOver = await SendJsonAsync("{\"text\":\"éx\"}", chunked: true);
            AssertEqual(HttpStatusCode.RequestEntityTooLarge, chunkedOver.Status,
                "An oversized chunked POST body should be rejected while streaming.");

            using (var stalledBody = new TcpClient())
            {
                await stalledBody.ConnectAsync(IPAddress.Loopback, port);
                var stream = stalledBody.GetStream();
                var requestHead = Encoding.ASCII.GetBytes(
                    $"POST /echo HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n");
                var partialJson = Encoding.UTF8.GetBytes("{\"text\":");
                await stream.WriteAsync(requestHead);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{partialJson.Length:X}\r\n"));
                await stream.WriteAsync(partialJson);
                await stream.WriteAsync(Encoding.ASCII.GetBytes("\r\n"));
                await stream.FlushAsync();
                var response = await ReadRawDeadlineResponseAsync(stream, TimeSpan.FromSeconds(4));
                AssertEqual(504, response.Status, "A stalled chunked POST body must be covered by the route deadline.");
                AssertEqual("{\"error\":\"request_timeout\"}", response.Body,
                    "Body-read timeouts should use the existing JSON error envelope.");
                AssertTrue(response.Headers.ContainsKey("X-Request-Id"),
                    "A body-read timeout should retain the request-id response header.");
            }

            using (var response = await client.GetAsync("/fetch?path=%2Fhold-cancel"))
            {
                AssertEqual(HttpStatusCode.GatewayTimeout, response.StatusCode,
                    "An awaited handler operation exceeding the configured deadline should return 504.");
                AssertEqual("{\"error\":\"request_timeout\"}", await response.Content.ReadAsStringAsync(),
                    "A timed-out handler should use the stable request_timeout JSON body.");
                AssertTrue(response.Headers.Contains("X-Request-Id"),
                    "A timed-out handler should retain the request-id response header.");
            }
            await upstream.WaitForClientDisconnectAsync("/hold-cancel").WaitAsync(TimeSpan.FromSeconds(3));

            using (var disconnect = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)))
            {
                var disconnected = false;
                try
                {
                    using var ignored = await client.GetAsync("/fetch?path=%2Fhold-cancel-managed", disconnect.Token);
                }
                catch (OperationCanceledException) when (disconnect.IsCancellationRequested)
                {
                    disconnected = true;
                }
                AssertTrue(disconnected, "The client-disconnect fixture must cancel its inbound HTTP request.");
            }
            await upstream.WaitForClientDisconnectAsync("/hold-cancel-managed").WaitAsync(TimeSpan.FromSeconds(3));
            using (var health = await client.GetAsync("/"))
                AssertEqual(HttpStatusCode.OK, health.StatusCode,
                    "A client disconnect must not turn into a server timeout or fault the web host.");
        }
        finally
        {
            if (!webProcess.HasExited)
            {
                webProcess.Kill(entireProcessTree: true);
                await webProcess.WaitForExitAsync();
            }
        }

        var serverErrors = await stderrTask.WaitAsync(TimeSpan.FromSeconds(5));
        AssertTrue(!serverErrors.Contains("Unhandled request fault", StringComparison.Ordinal),
            $"A client disconnect must not be logged as a generic server fault. {serverErrors}");
        var acceptedBodyLogs = serverErrors.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.Contains("\"event\":\"request.body.accepted\"", StringComparison.Ordinal))
            .ToArray();
        AssertEqual(3, acceptedBodyLogs.Length,
            "Only the three accepted under/exact-limit POST requests may enter the route handler.");
    }

    private sealed class UnknownLengthJsonContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes.AsMemory()).AsTask();

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) =>
            stream.WriteAsync(bytes.AsMemory(), cancellationToken).AsTask();
    }

    private sealed record RawDeadlineResponse(int Status, string Body, IReadOnlyDictionary<string, string> Headers);

    private static async Task<RawDeadlineResponse> ReadRawDeadlineResponseAsync(Stream stream, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        var headerBytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(), cancellation.Token);
            if (read == 0)
                throw new EndOfStreamException("The web host closed before sending a timeout response.");
            headerBytes.Add(one[0]);
            var count = headerBytes.Count;
            if (count >= 4 && headerBytes[count - 4] == (byte)'\r' && headerBytes[count - 3] == (byte)'\n'
                && headerBytes[count - 2] == (byte)'\r' && headerBytes[count - 1] == (byte)'\n')
                break;
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var statusParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var status = int.Parse(statusParts[1], CultureInfo.InvariantCulture);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var separator = line.IndexOf(':');
            if (separator > 0)
                headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        byte[] bodyBytes;
        if (headers.TryGetValue("Content-Length", out var lengthText))
        {
            var length = int.Parse(lengthText, CultureInfo.InvariantCulture);
            bodyBytes = await ReadExactAsync(stream, length, cancellation.Token);
        }
        else if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding)
                 && transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            using var body = new MemoryStream();
            while (true)
            {
                var sizeLine = await ReadAsciiLineAsync(stream, cancellation.Token);
                var size = int.Parse(sizeLine.Split(';', 2)[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (size == 0)
                {
                    while (await ReadAsciiLineAsync(stream, cancellation.Token) is { Length: > 0 }) { }
                    break;
                }
                var chunk = await ReadExactAsync(stream, size, cancellation.Token);
                body.Write(chunk);
                _ = await ReadExactAsync(stream, 2, cancellation.Token);
            }
            bodyBytes = body.ToArray();
        }
        else
        {
            using var body = new MemoryStream();
            var buffer = new byte[256];
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(), cancellation.Token);
                if (read == 0) break;
                body.Write(buffer, 0, read);
            }
            bodyBytes = body.ToArray();
        }

        return new RawDeadlineResponse(status, Encoding.UTF8.GetString(bodyBytes), headers);
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int count, CancellationToken cancellationToken)
    {
        var result = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset, count - offset), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The web host closed in the middle of an HTTP response.");
            offset += read;
        }
        return result;
    }

    private static async Task<string> ReadAsciiLineAsync(Stream stream, CancellationToken cancellationToken)
    {
        var bytes = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(), cancellationToken);
            if (read == 0)
                throw new EndOfStreamException("The web host closed in the middle of a chunked response.");
            if (one[0] == (byte)'\n')
            {
                if (bytes.Count != 0 && bytes[^1] == (byte)'\r')
                    bytes.RemoveAt(bytes.Count - 1);
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
            bytes.Add(one[0]);
        }
    }
}
