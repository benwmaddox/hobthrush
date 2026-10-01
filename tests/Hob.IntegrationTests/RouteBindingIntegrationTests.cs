using System.Collections;
using System.Net;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static Task TestRouteBindingCompiler(Harness harness)
    {
        var source = """
            module app::main;
            struct Request { value: Text }
            union Reply { Found(Text), Empty }

            fn query(id: i32, part: Text, path: Text, limit: Option<i32>, db: DbRead, writer: DbWrite) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Found(path);
            }
            fn create(request: self::app::main::Request, id: i32, note: Option<Text>, db: DbRead, writer: DbWrite) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Empty;
            }

            route GET "/records/{id}/parts/{part}" {
                path id: i32;
                path part: Text;
                query path: Text;
                query limit: Option<i32>;
                handler: self::app::main::query;
                response Found: 200 json Text;
                response Empty: 204;
            }

            route POST "/records/{id}" {
                body: self::app::main::Request;
                path id: i32;
                query note: Option<Text>;
                handler: self::app::main::create;
                response Found: 200 json Text;
                response Empty: 204;
            }
            """;

        var check = CheckRoutePackageWithWebContext(harness, [("app::main", source)]);
        AssertEqual(0, check.DiagnosticCodes.Length,
            $"Path and query bindings with GET/POST handler and capability arguments should typecheck. [{string.Join(", ", check.DiagnosticCodes)}]");
        var program = check.Program ?? throw new InvalidOperationException("The typed route package should produce checked IR.");
        var routes = ((IEnumerable)program.GetType().GetProperty("Routes")!.GetValue(program)!).Cast<object>().ToArray();
        AssertEqual(2, routes.Length, "Both parameterized routes should be retained in checked IR.");

        var get = routes.Single(route => (string)route.GetType().GetProperty("Method")!.GetValue(route)! == "GET");
        AssertEqual("self::app::main::query", get.GetType().GetProperty("HandlerReference")!.GetValue(get)?.ToString(),
            "The checked route must preserve its fully qualified handler reference.");
        AssertRouteBinding(get, 0, "Path", "id", "i32", false, 0);
        AssertRouteBinding(get, 1, "Path", "part", "Text", false, 1);
        AssertRouteBinding(get, 2, "Query", "path", "Text", false, 2);
        AssertRouteBinding(get, 3, "Query", "limit", "Option<i32>", true, 3);
        AssertCapabilityBinding(get, 0, "db", "DbRead", 4);
        AssertCapabilityBinding(get, 1, "writer", "DbWrite", 5);

        var post = routes.Single(route => (string)route.GetType().GetProperty("Method")!.GetValue(route)! == "POST");
        AssertRouteBinding(post, 0, "Path", "id", "i32", false, 1);
        AssertRouteBinding(post, 1, "Query", "note", "Option<Text>", true, 2);
        AssertCapabilityBinding(post, 0, "db", "DbRead", 3);
        AssertCapabilityBinding(post, 1, "writer", "DbWrite", 4);
        return Task.CompletedTask;
    }

    private static async Task TestRouteBindingDiagnostics(Harness harness)
    {
        const string header = """
            module app::main;
            struct Request { value: Text }
            union Reply { Found(Text), Empty }
            fn get() -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_i32(id: i32) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_text(value: Text) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_optional(value: Option<i32>) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_ordered(first: i32, second: Text) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_reversed(first: Text, second: i32) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_capability_first(db: DbRead, id: i32, q: Option<i32>) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn get_capabilities_reversed(id: i32, q: Option<i32>, writer: DbWrite, db: DbRead) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn post(body: self::app::main::Request, id: i32, q: Option<i32>, db: DbRead, writer: DbWrite) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            fn post_reversed(body: self::app::main::Request, id: i32, db: DbRead, q: Option<i32>, writer: DbWrite) -> self::app::main::Reply effects {} { return self::app::main::Reply.Found("ok"); }
            """;

        static string Route(string body) => "route " + body;
        static string Get(string path, string items, string handler = "self::app::main::get") =>
            Route($"GET \"{path}\" {{ {items} handler: {handler}; response Found: 200 json Text; response Empty: 204; }}");

        var cases = new (string Name, string Route, string Code)[]
        {
            ("missing-path-binding", Get("/items/{id}", "", "self::app::main::get"), "E_ROUTE_BINDING"),
            ("unused-path-binding", Get("/items", "path id: i32;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("path-spelling-conflict", Get("/items/{id}", "path ID: i32;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("repeated-path-placeholder", Get("/items/{id}/{id}", "path id: i32;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("duplicate-query-binding", Get("/items", "query q: i32; query q: i32;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("case-conflicting-binding-kinds", Get("/items/{id}", "path id: i32; query ID: i32;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("unsupported-path-bool", Get("/items/{id}", "path id: bool;", "self::app::main::get_i32"), "E_ROUTE_BINDING"),
            ("unsupported-path-option", Get("/items/{id}", "path id: Option<i32>;", "self::app::main::get_optional"), "E_ROUTE_BINDING"),
            ("unsupported-query-bool", Get("/items", "query q: bool;", "self::app::main::get"), "E_ROUTE_BINDING"),
            ("unsupported-query-option-bool", Get("/items", "query q: Option<bool>;", "self::app::main::get"), "E_ROUTE_BINDING"),
            ("wrong-binding-type", Get("/items/{id}", "path id: i32;", "self::app::main::get_text"), "E_ROUTE_HANDLER"),
            ("missing-binding-parameter", Get("/items/{id}", "path id: i32;", "self::app::main::get"), "E_ROUTE_HANDLER"),
            ("binding-parameter-order", Get("/items/{first}/{second}", "path first: i32; path second: Text;", "self::app::main::get_reversed"), "E_ROUTE_HANDLER"),
            ("capability-before-bindings", Get("/items/{id}", "path id: i32; query q: Option<i32>;", "self::app::main::get_capability_first"), "E_ROUTE_HANDLER"),
            ("capability-order-after-bindings", Get("/items/{id}", "path id: i32; query q: Option<i32>;", "self::app::main::get_capabilities_reversed"), "E_ROUTE_HANDLER"),
            ("post-binding-order-after-body", Route("POST \"/items/{id}\" { body: self::app::main::Request; path id: i32; query q: Option<i32>; handler: self::app::main::post_reversed; response Found: 200 json Text; response Empty: 204; }"), "E_ROUTE_HANDLER")
        };

        foreach (var (name, route, expectedCode) in cases)
        {
            var result = CheckRoutePackageWithWebContext(harness, [("app::main", header + "\n" + route)]);
            AssertTrue(result.DiagnosticCodes.Contains(expectedCode, StringComparer.Ordinal),
                $"Expected {expectedCode} for {name}; got [{string.Join(", ", result.DiagnosticCodes)}].");
        }

        const string equalSpecificity = """
            route GET "/items/{id}" { path id: i32; handler: self::app::main::get_i32; response Found: 200 json Text; response Empty: 204; }
            route GET "/items/{slug}" { path slug: i32; handler: self::app::main::get_i32; response Found: 200 json Text; response Empty: 204; }
            """;
        var overlap = CheckRoutePackageWithWebContext(harness, [("app::main", header + "\n" + equalSpecificity)]);
        AssertTrue(overlap.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "Overlapping dynamic routes at equal literal specificity must be rejected.");

        const string literalSpecificity = """
            route GET "/items/{id}" { path id: i32; handler: self::app::main::get_i32; response Found: 200 json Text; response Empty: 204; }
            route GET "/items/special" { handler: self::app::main::get; response Found: 200 json Text; response Empty: 204; }
            """;
        var specific = CheckRoutePackageWithWebContext(harness, [("app::main", header + "\n" + literalSpecificity)]);
        AssertEqual(0, specific.DiagnosticCodes.Length,
            $"A literal route may overlap a less-specific placeholder route. [{string.Join(", ", specific.DiagnosticCodes)}]");

        const string crossMethodPlaceholderMismatch = """
            route GET "/records/{id}" { path id: i32; handler: self::app::main::get_i32; response Found: 200 json Text; response Empty: 204; }
            route POST "/records/{slug}" { body: self::app::main::Request; path slug: i32; query q: Option<i32>; handler: self::app::main::post; response Found: 200 json Text; response Empty: 204; }
            """;
        var differentNames = CheckRoutePackageWithWebContext(harness, [("app::main", header + "\n" + crossMethodPlaceholderMismatch)]);
        AssertTrue(differentNames.DiagnosticCodes.Contains("E_ROUTE_DECL", StringComparer.Ordinal),
            "Same-shape routes for different methods must use the same placeholder names for OpenAPI grouping.");

        const string malformedPrefix = "module harness::bad_route;\nunion Reply { Empty }\n";
        foreach (var (name, path) in new[]
                 {
                     ("missing-close", "/items/{id"),
                     ("stray-close", "/items/id}"),
                     ("embedded-placeholder", "/items/pre{id}"),
                     ("invalid-placeholder-name", "/items/{id-name}"),
                     ("empty-segment", "/items//{id}")
                 })
        {
            var source = malformedPrefix + $"route GET \"{path}\" {{ handler: self::harness::bad_route::missing; response Empty: 204; }}";
            var diagnostics = await ExpectDiagnosticsAsync(harness, $"malformed-route-template-{name}", source, "E_ROUTE_DECL");
            AssertEqual("E_ROUTE_DECL", diagnostics[0].Code,
                $"Malformed route template {path} should retain a route declaration diagnostic.");
        }
    }

    private static async Task TestRouteBindingRuntime(Harness harness)
    {
        const string source = """
            module app::main;
            struct Request { value: Text }
            union Reply { Number(i32), TextValue(Text), Empty }

            fn ready() -> self::app::main::Reply effects {} { return self::app::main::Reply.TextValue("ready"); }
            fn path_echo(value: i32) -> self::app::main::Reply effects {} { return self::app::main::Reply.Number(value); }
            fn text_path_echo(value: Text) -> self::app::main::Reply effects {} { return self::app::main::Reply.TextValue(value); }
            fn literal() -> self::app::main::Reply effects {} { return self::app::main::Reply.Number(91); }
            fn required_query(count: i32) -> self::app::main::Reply effects {} { return self::app::main::Reply.Number(count); }
            fn optional_count(count: Option<i32>) -> self::app::main::Reply effects {} {
                return match count {
                    Some(value) => self::app::main::Reply.Number(value),
                    None => self::app::main::Reply.Empty,
                };
            }
            fn optional_text(name: Option<Text>) -> self::app::main::Reply effects {} {
                return match name {
                    Some(value) => self::app::main::Reply.TextValue(value),
                    None => self::app::main::Reply.Empty,
                };
            }
            fn post_echo(request: self::app::main::Request, id: i32, note: Option<Text>) -> self::app::main::Reply effects {} {
                return self::app::main::Reply.Number(id);
            }

            route GET "/" {
                handler: self::app::main::ready;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/required/{value}" {
                path value: i32;
                handler: self::app::main::path_echo;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/text/{value}" {
                path value: Text;
                handler: self::app::main::text_path_echo;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/items/{id}" {
                path id: i32;
                handler: self::app::main::path_echo;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/required/fixed" {
                handler: self::app::main::literal;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/required-query" {
                query count: i32;
                handler: self::app::main::required_query;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/optional-count" {
                query count: Option<i32>;
                handler: self::app::main::optional_count;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route GET "/optional-text" {
                query name: Option<Text>;
                handler: self::app::main::optional_text;
                response Number: 200 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            route POST "/items/{id}" {
                body: self::app::main::Request;
                path id: i32;
                query note: Option<Text>;
                handler: self::app::main::post_echo;
                response Number: 201 json i32;
                response TextValue: 200 json Text;
                response Empty: 204;
            }
            """;
        const string manifest = "name = \"route-binding-runtime\"\nversion = \"0.1.0\"\nkind = \"web\"\nsource_root = \"src\"\nentry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";
        var packageRoot = await harness.WritePackageAsync(
            "route-binding-runtime",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.hob"] = source });

        var check = await harness.InvokePackageDirectoryAsync("route-binding-runtime-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var firstBuild = await harness.InvokePackageDirectoryAsync("route-binding-runtime-build-first", packageRoot, "build");
        var firstArtifact = AssertBuiltWebApplication(firstBuild, packageRoot);
        var firstOpenApiPath = Path.Combine(Path.GetDirectoryName(firstArtifact)!, "openapi.json");
        var firstOpenApiBytes = await File.ReadAllBytesAsync(firstOpenApiPath);
        using (var document = JsonDocument.Parse(firstOpenApiBytes))
        {
            var paths = document.RootElement.GetProperty("paths");
            var pathParameters = paths.GetProperty("/required/{value}").GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
            AssertEqual(1, pathParameters.Length, "The path binding should be represented in OpenAPI.");
            AssertOpenApiParameter(pathParameters[0], "value", "path", true, "integer", "int32");

            var requiredQuery = paths.GetProperty("/required-query").GetProperty("get").GetProperty("parameters").EnumerateArray().Single();
            AssertOpenApiParameter(requiredQuery, "count", "query", true, "integer", "int32");
            var optionalQuery = paths.GetProperty("/optional-count").GetProperty("get").GetProperty("parameters").EnumerateArray().Single();
            AssertOpenApiParameter(optionalQuery, "count", "query", false, "integer", "int32");

            var sharedPath = paths.GetProperty("/items/{id}");
            var sharedGet = sharedPath.GetProperty("get");
            AssertOpenApiParameter(sharedGet.GetProperty("parameters").EnumerateArray().Single(), "id", "path", true, "integer", "int32");
            var post = sharedPath.GetProperty("post");
            var postParameters = post.GetProperty("parameters").EnumerateArray().ToArray();
            AssertEqual(2, postParameters.Length, "POST path and query bindings should both appear in OpenAPI.");
            AssertOpenApiParameter(postParameters[0], "id", "path", true, "integer", "int32");
            AssertOpenApiParameter(postParameters[1], "note", "query", false, "string", null);
            AssertTrue(post.TryGetProperty("requestBody", out _),
                "The POST body should remain documented alongside its route parameters.");
        }

        var secondBuild = await harness.InvokePackageDirectoryAsync("route-binding-runtime-build-second", packageRoot, "build");
        var secondArtifact = AssertBuiltWebApplication(secondBuild, packageRoot);
        var secondOpenApiPath = Path.Combine(Path.GetDirectoryName(secondArtifact)!, "openapi.json");
        var secondOpenApiBytes = await File.ReadAllBytesAsync(secondOpenApiPath);
        AssertTrue(firstOpenApiBytes.SequenceEqual(secondOpenApiBytes),
            "Equivalent builds with path, required query, and optional query bindings must emit byte-for-byte deterministic OpenAPI.");

        var port = GetUnusedLoopbackPort();
        var baseAddress = new Uri($"http://127.0.0.1:{port}");
        using var client = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(4) };
        using var process = harness.StartWebPackageProcess("route-binding-runtime-run", packageRoot, "--urls", baseAddress.ToString().TrimEnd('/'));
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await WaitForWebServerAsync(process, client, stdoutTask, stderrTask);

            await AssertTextResponseAsync(client, "/text/hello%20world", "hello world");
            await AssertTextResponseAsync(client, "/text/%CE%BB%20%E2%98%83", "λ ☃");
            // Kestrel accepts raw /text/%ZZ as a Text route value; malformed escape rejection is host behavior, so no 400 assertion is made.
            await AssertIntegerResponseAsync(client, "/required/-2147483648", -2147483648);
            await AssertIntegerResponseAsync(client, "/required/2147483647", 2147483647);
            await AssertIntegerResponseAsync(client, "/required/fixed", 91);
            foreach (var path in new[]
                     {
                         "/required/not-an-integer",
                         "/required/%2B1",
                         "/required/%201",
                         "/required/2147483648",
                         "/required/-2147483649"
                     })
                await AssertRouteBindingBadRequestAsync(client, path);

            await AssertIntegerResponseAsync(client, "/required-query?count=-2147483648", -2147483648);
            await AssertIntegerResponseAsync(client, "/required-query?count=2147483647", 2147483647);
            foreach (var path in new[]
                     {
                         "/required-query",
                         "/required-query?count=",
                         "/required-query?count=1&count=2",
                         "/required-query?count=%2B1",
                         "/required-query?count=%201",
                         "/required-query?count=1%20",
                         "/required-query?count=2147483648",
                         "/required-query?count=-2147483649",
                         "/required-query?count=1.0"
                     })
                await AssertRouteBindingBadRequestAsync(client, path);

            using (var missingOptional = await client.GetAsync("/optional-count"))
            {
                AssertEqual(HttpStatusCode.NoContent, missingOptional.StatusCode,
                    "An absent Option<i32> query parameter should become None.");
                AssertEqual(0, (await missingOptional.Content.ReadAsByteArrayAsync()).Length,
                    "An absent optional query should preserve the mapped no-content response.");
            }
            await AssertIntegerResponseAsync(client, "/optional-count?count=0", 0);
            await AssertIntegerResponseAsync(client, "/optional-count?count=-2147483648", -2147483648);
            foreach (var path in new[]
                     {
                         "/optional-count?count=",
                         "/optional-count?count=bad",
                         "/optional-count?count=1&count=2",
                         "/optional-count?count=2147483648"
                     })
                await AssertRouteBindingBadRequestAsync(client, path);

            using (var missingOptionalText = await client.GetAsync("/optional-text"))
                AssertEqual(HttpStatusCode.NoContent, missingOptionalText.StatusCode,
                    "An absent Option<Text> query parameter should become None.");
            using (var emptyOptionalText = await client.GetAsync("/optional-text?name="))
            {
                AssertEqual(HttpStatusCode.OK, emptyOptionalText.StatusCode,
                    "A present empty Option<Text> query parameter should become Some(\"\").");
                AssertEqual("\"\"", await emptyOptionalText.Content.ReadAsStringAsync(),
                    "Some(empty text) should serialize as an empty JSON string.");
            }
            using (var unicodeOptionalText = await client.GetAsync("/optional-text?name=%CE%BB%20%E2%98%83"))
            {
                using var unicodeBody = JsonDocument.Parse(await unicodeOptionalText.Content.ReadAsStringAsync());
                AssertEqual("λ ☃", unicodeBody.RootElement.GetString(),
                    "Text query bindings should preserve decoded Unicode values.");
            }
            await AssertRouteBindingBadRequestAsync(client, "/optional-text?name=one&name=two");

            using (var post = await client.PostAsync("/items/42?note=", JsonBody("{\"value\":\"payload\"}")))
            {
                AssertEqual(HttpStatusCode.Created, post.StatusCode,
                    "POST should decode path/query bindings after its JSON request body.");
                AssertEqual("42", await post.Content.ReadAsStringAsync(),
                    "POST handler arguments should retain body-then-binding order.");
            }
            await AssertRouteBindingBadRequestAsync(client, "/items/not-an-integer?note=x", HttpMethod.Post, "{\"value\":\"payload\"}");
        }
        finally
        {
            var exitedBeforeCleanup = process.HasExited;
            if (!exitedBeforeCleanup)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
            await process.WaitForExitAsync();
            if (exitedBeforeCleanup && process.ExitCode != 0)
            {
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                throw new InvalidOperationException($"The route binding web host exited unexpectedly ({process.ExitCode}). stdout=<{stdout}> stderr=<{stderr}>");
            }
        }
    }

    private static void AssertRouteBinding(object route, int position, string kind, string name, string type, bool optional, int parameterIndex)
    {
        var bindings = ((IEnumerable)route.GetType().GetProperty("Bindings")!.GetValue(route)!).Cast<object>().ToArray();
        AssertTrue(bindings.Length > position, $"The checked route is missing binding {position}.");
        var binding = bindings[position];
        AssertEqual(kind, binding.GetType().GetProperty("Kind")!.GetValue(binding)?.ToString(), "Route binding kind mismatch.");
        AssertEqual(name, binding.GetType().GetProperty("WireName")!.GetValue(binding)?.ToString(), "Route binding wire name mismatch.");
        var typeValue = binding.GetType().GetProperty("Type")!.GetValue(binding)!;
        AssertEqual(type, typeValue.GetType().GetProperty("DisplayName")!.GetValue(typeValue)?.ToString(), "Route binding type mismatch.");
        AssertEqual(optional, (bool)binding.GetType().GetProperty("IsOptional")!.GetValue(binding)!, "Route binding optionality mismatch.");
        AssertEqual(parameterIndex, (int)binding.GetType().GetProperty("HandlerParameterIndex")!.GetValue(binding)!, "Route binding handler index mismatch.");
    }

    private static void AssertCapabilityBinding(object route, int position, string name, string kind, int parameterIndex)
    {
        var capabilities = ((IEnumerable)route.GetType().GetProperty("Capabilities")!.GetValue(route)!).Cast<object>().ToArray();
        AssertTrue(capabilities.Length > position, $"The checked route is missing capability parameter {position}.");
        var capability = capabilities[position];
        AssertEqual(name, capability.GetType().GetProperty("ParameterName")!.GetValue(capability)?.ToString(), "Route capability parameter name mismatch.");
        AssertEqual(kind, capability.GetType().GetProperty("Kind")!.GetValue(capability)?.ToString(), "Route capability kind mismatch.");
        AssertEqual(parameterIndex, (int)capability.GetType().GetProperty("HandlerParameterIndex")!.GetValue(capability)!, "Route capability handler index mismatch.");
    }

    private static void AssertOpenApiParameter(JsonElement parameter, string name, string location, bool required, string schemaType, string? format)
    {
        AssertJsonPropertyOrder(parameter, "name,in,required,schema");
        AssertEqual(name, parameter.GetProperty("name").GetString(), "OpenAPI route parameter name mismatch.");
        AssertEqual(location, parameter.GetProperty("in").GetString(), "OpenAPI route parameter location mismatch.");
        AssertEqual(required, parameter.GetProperty("required").GetBoolean(), "OpenAPI route parameter requiredness mismatch.");
        var schema = parameter.GetProperty("schema");
        AssertEqual(schemaType, schema.GetProperty("type").GetString(), "OpenAPI route parameter schema type mismatch.");
        if (format is null)
            AssertTrue(!schema.TryGetProperty("format", out _), "Text route parameters should not advertise an integer format.");
        else
            AssertEqual(format, schema.GetProperty("format").GetString(), "OpenAPI route parameter schema format mismatch.");
    }

    private static async Task AssertTextResponseAsync(HttpClient client, string path, string expected)
    {
        using var response = await client.GetAsync(path);
        AssertEqual(HttpStatusCode.OK, response.StatusCode, $"Expected a decoded Text path response for {path}.");
        AssertEqual(JsonSerializer.Serialize(expected), await response.Content.ReadAsStringAsync(),
            $"Decoded Text path value mismatch for {path}.");
    }

    private static async Task AssertIntegerResponseAsync(HttpClient client, string path, int expected)
    {
        using var response = await client.GetAsync(path);
        AssertEqual(HttpStatusCode.OK, response.StatusCode, $"Expected a parsed i32 response for {path}.");
        AssertEqual(expected.ToString(System.Globalization.CultureInfo.InvariantCulture),
            await response.Content.ReadAsStringAsync(), $"Parsed i32 value mismatch for {path}.");
    }

    private static async Task AssertRouteBindingBadRequestAsync(
        HttpClient client,
        string path,
        HttpMethod? method = null,
        string? jsonBody = null)
    {
        using var request = new HttpRequestMessage(method ?? HttpMethod.Get, path);
        if (jsonBody is not null)
            request.Content = JsonBody(jsonBody);
        using var response = await client.SendAsync(request);
        AssertEqual(HttpStatusCode.BadRequest, response.StatusCode, $"Malformed route values for {path} must return 400.");
        AssertEqual("application/json", response.Content.Headers.ContentType?.MediaType,
            "Malformed route values must use the stable JSON error response.");
        AssertEqual("{\"error\":\"invalid_request\"}", await response.Content.ReadAsStringAsync(),
            "Malformed route values must not expose request values or runtime details.");
    }
}
