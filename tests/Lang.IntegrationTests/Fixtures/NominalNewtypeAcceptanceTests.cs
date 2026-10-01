using System.Runtime.InteropServices;
using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestNominalNewtypes(Harness harness)
    {
        async Task<string> ReadFixtureAsync(string name) => await File.ReadAllTextAsync(
            Path.Combine(harness.RepositoryRoot, "fixtures", name));

        var scalarSource = await ReadFixtureAsync("98-valid-newtype-scalar.lang");
        AssertRunOutput("23" + Environment.NewLine,
            await harness.InvokeAsync("newtype-scalar-values", "run", scalarSource));

        var genericSource = await ReadFixtureAsync("99-valid-newtype-generic-values.lang");
        AssertRunOutput("20" + Environment.NewLine,
            await harness.InvokeAsync("newtype-generic-values", "run", genericSource));

        var guardedSource = await ReadFixtureAsync("100-valid-newtype-guarded-recursion.lang");
        AssertRunOutput("100" + Environment.NewLine,
            await harness.InvokeAsync("newtype-guarded-recursion", "run", guardedSource));
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The guarded-newtype NativeAOT test requires Windows x64 or Linux x64.");
        var guardedAotBuild = await harness.InvokeWithTimeoutAsync(
            "newtype-guarded-recursion-aot",
            "build",
            guardedSource,
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, guardedAotBuild.ExitCode, Describe(guardedAotBuild));
        var guardedAotArtifact = ParseBuiltArtifact(guardedAotBuild, "Built native executable: ");
        AssertRunOutput("100" + Environment.NewLine,
            await ExecuteNativeAsync(guardedAotArtifact, TimeSpan.FromSeconds(30)));

        var implicitSource = await ReadFixtureAsync("101-invalid-newtype-implicit-assignment.lang");
        var implicitDiagnostics = await ExpectDiagnosticsAsync(
            harness,
            "newtype-implicit-boundaries",
            implicitSource,
            Enumerable.Repeat("E_TYPE_MISMATCH", 7).ToArray());
        AssertNewtypeDiagnosticSequence(implicitSource, implicitDiagnostics,
        [
            ("E_TYPE_MISMATCH", "7", 1),
            ("E_TYPE_MISMATCH", "8", 1),
            ("E_TYPE_MISMATCH", "user", 3),
            ("E_TYPE_MISMATCH", "==", 1),
            ("E_TYPE_MISMATCH", "==", 2),
            ("E_TYPE_MISMATCH", "<", 1),
            ("E_TYPE_MISMATCH", "+", 1),
        ]);

        var resourceSource = await ReadFixtureAsync("102-invalid-newtype-resource.lang");
        var resourceDiagnostics = await ExpectDiagnosticsAsync(
            harness,
            "newtype-stored-resource-representations",
            resourceSource,
            Enumerable.Repeat("E_RESOURCE_ESCAPE", 15).ToArray());
        AssertNewtypeDiagnosticSequence(resourceSource, resourceDiagnostics,
        [
            ("E_RESOURCE_ESCAPE", "FsRead", 1),
            ("E_RESOURCE_ESCAPE", "FsWrite", 1),
            ("E_RESOURCE_ESCAPE", "HttpClient", 1),
            ("E_RESOURCE_ESCAPE", "DbRead", 1),
            ("E_RESOURCE_ESCAPE", "DbWrite", 2),
            ("E_RESOURCE_ESCAPE", "Transaction", 1),
            ("E_RESOURCE_ESCAPE", "Config", 1),
            ("E_RESOURCE_ESCAPE", "Secrets", 1),
            ("E_RESOURCE_ESCAPE", "Logger", 1),
            ("E_RESOURCE_ESCAPE", "ProcessRunner", 1),
            ("E_RESOURCE_ESCAPE", "Option", 1),
            ("E_RESOURCE_ESCAPE", "self", 2),
            ("E_RESOURCE_ESCAPE", "List", 1),
            ("E_RESOURCE_ESCAPE", "Map", 1),
            ("E_RESOURCE_ESCAPE", "self", 3),
        ]);

        var cycleSource = await ReadFixtureAsync("103-invalid-newtype-direct-cycle.lang");
        var cycleDiagnostics = await ExpectDiagnosticsAsync(
            harness,
            "newtype-direct-value-cycles",
            cycleSource,
            "E_TYPE_MISMATCH",
            "E_TYPE_MISMATCH",
            "E_TYPE_MISMATCH");
        AssertNewtypeDiagnosticSequence(cycleSource, cycleDiagnostics,
        [
            ("E_TYPE_MISMATCH", "self", 5),
            ("E_TYPE_MISMATCH", "self", 1),
            ("E_TYPE_MISMATCH", "self", 3),
        ]);

        var genericDeclarationSource = await ReadFixtureAsync("104-invalid-newtype-generic-declaration.lang");
        var genericDeclarationDiagnostics = await ExpectDiagnosticsAsync(
            harness,
            "newtype-generic-declaration-rejected",
            genericDeclarationSource,
            "E_UNSUPPORTED");
        AssertEqual(1, genericDeclarationDiagnostics.Length,
            "A generic newtype declaration should produce one exact unsupported diagnostic.");
        AssertEqual("E_UNSUPPORTED", genericDeclarationDiagnostics[0].Code,
            "Generic newtype declarations should use E_UNSUPPORTED.");
        AssertNewtypeRangeAtLiteral(genericDeclarationSource, genericDeclarationDiagnostics[0], "<", 1);

        const string memberHeader = """
            module fixture::newtype_members;
            pub newtype UserId = i32;
            """;
        async Task AssertSingleNewtypeSourceDiagnosticAsync(
            string caseName,
            string source,
            string expectedCode,
            string token,
            int occurrence = 1)
        {
            var diagnostics = await ExpectDiagnosticsAsync(harness, caseName, source, expectedCode);
            AssertNewtypeDiagnosticSequence(source, diagnostics, [(expectedCode, token, occurrence)]);
        }

        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-wrap-arity",
            memberHeader + "\npub fn bad() -> i32 effects {} { let id: self::fixture::newtype_members::UserId = self::fixture::newtype_members::UserId.wrap(1, 2); return 0; }\n",
            "E_TYPE_MISMATCH",
            "wrap");
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-wrap-argument-type",
            memberHeader + "\npub fn bad() -> i32 effects {} { let id: self::fixture::newtype_members::UserId = self::fixture::newtype_members::UserId.wrap(\"wrong\"); return 0; }\n",
            "E_TYPE_MISMATCH",
            "\"wrong\"");
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-unknown-static-member",
            memberHeader + "\npub fn bad() -> i32 effects {} { let id: self::fixture::newtype_members::UserId = self::fixture::newtype_members::UserId.other(1); return 0; }\n",
            "E_TYPE_MISMATCH",
            "other");
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-unknown-projector",
            memberHeader + "\npub fn bad(value: self::fixture::newtype_members::UserId) -> i32 effects {} { return value.count; }\n",
            "E_FIELD_UNKNOWN",
            "count");
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-value-is-not-callable",
            memberHeader + "\npub fn bad(value: self::fixture::newtype_members::UserId) -> i32 effects {} { return value.value(); }\n",
            "E_TYPE_MISMATCH",
            "value",
            3);
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-type-arity",
            memberHeader + "\npub fn bad(value: self::fixture::newtype_members::UserId<i32>) -> i32 effects {} { return 0; }\n",
            "E_TYPE_MISMATCH",
            "self");
        await AssertSingleNewtypeSourceDiagnosticAsync(
            "newtype-brace-construction-rejected",
            memberHeader + "\npub fn bad() -> i32 effects {} { return self::fixture::newtype_members::UserId { value: 1 }.value; }\n",
            "E_TYPE_MISMATCH",
            "self");

        const string privateAccessManifest = """
            name = "newtype-private-access"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"
            """;
        const string privateIdsSource = """
            module app::ids;
            newtype PrivateId = i32;
            """;
        const string privateUseSource = """
            module app::main;
            fn inaccessible(value: self::app::ids::PrivateId) -> i32 effects {} {
                return value.value;
            }
            """;
        var privateAccessRoot = await harness.WritePackageAsync(
            "newtype-cross-module-private-access",
            privateAccessManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/ids.lang"] = privateIdsSource,
                ["src/app/main.lang"] = privateUseSource
            });
        var privateAccess = await harness.InvokePackageDirectoryAsync(
            "newtype-cross-module-private-check", privateAccessRoot, "check", "--json");
        var privateAccessDiagnostics = ParseDiagnosticSnapshots(privateAccess.StandardOutput);
        AssertEqual(1, privateAccess.ExitCode, Describe(privateAccess));
        AssertEqual(string.Empty, privateAccess.StandardError, Describe(privateAccess));
        AssertEqual(1, privateAccessDiagnostics.Length, Describe(privateAccess));
        AssertEqual("E_ACCESS_PRIVATE", privateAccessDiagnostics[0].Code, Describe(privateAccess));
        AssertEqual(Path.GetFullPath(Path.Combine(privateAccessRoot, "src", "app", "main.lang")),
            Path.GetFullPath(privateAccessDiagnostics[0].File), Describe(privateAccess));
        AssertRangeAtToken(privateUseSource, privateAccessDiagnostics[0], "self", 1);

        const string privateDependencyRootManifest = """
            name = "newtype-private-dependency-access"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            remote = "../remote"
            """;
        const string privateDependencySource = """
            module ids;
            newtype PrivateId = i32;
            """;
        const string privateDependencyUseSource = """
            module app::main;
            pub fn inaccessible(value: remote::ids::PrivateId) -> i32 effects {} {
                return value.value;
            }
            """;
        var privateDependencyRoot = await harness.WritePackageGraphAsync(
            "newtype-private-dependency-access",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(privateDependencyRootManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/app/main.lang"] = privateDependencyUseSource
                    }),
                ["remote"] = new PackageFixture(LibraryPackageManifest("newtype-private-dependency"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["src/ids.lang"] = privateDependencySource
                    })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-private-dependency-lock", privateDependencyRoot, "lock"));
        var privateDependencyCheck = await harness.InvokePackageDirectoryAsync(
            "newtype-private-dependency-check", privateDependencyRoot, "check", "--json");
        var privateDependencyDiagnostics = ParseDiagnosticSnapshots(privateDependencyCheck.StandardOutput);
        AssertEqual(1, privateDependencyCheck.ExitCode, Describe(privateDependencyCheck));
        AssertEqual(string.Empty, privateDependencyCheck.StandardError, Describe(privateDependencyCheck));
        AssertEqual(1, privateDependencyDiagnostics.Length, Describe(privateDependencyCheck));
        AssertEqual("E_ACCESS_PRIVATE", privateDependencyDiagnostics[0].Code, Describe(privateDependencyCheck));
        AssertEqual(Path.GetFullPath(Path.Combine(privateDependencyRoot, "src", "app", "main.lang")),
            Path.GetFullPath(privateDependencyDiagnostics[0].File), Describe(privateDependencyCheck));
        AssertRangeAtToken(privateDependencyUseSource, privateDependencyDiagnostics[0], "remote", 1);

        const string privateExposureSource = """
            module app::main;
            newtype PrivateId = i32;
            pub newtype PublicId = self::app::main::PrivateId;
            """;
        var privateExposure = await ExpectDiagnosticsAsync(
            harness, "newtype-public-representation-visibility", privateExposureSource, "E_TYPE_VISIBILITY");
        AssertNewtypeDiagnosticSequence(privateExposureSource, privateExposure,
            [("E_TYPE_VISIBILITY", "self", 1)]);

        const string privateSignatureSource = """
            module app::main;
            newtype PrivateId = i32;
            pub fn expose(value: self::app::main::PrivateId) -> i32 effects {} { return value.value; }
            """;
        var privateSignature = await ExpectDiagnosticsAsync(
            harness, "newtype-public-signature-visibility", privateSignatureSource, "E_TYPE_VISIBILITY");
        AssertNewtypeDiagnosticSequence(privateSignatureSource, privateSignature,
            [("E_TYPE_VISIBILITY", "value", 1)]);

        const string missingTraitSource = """
            module fixture::newtype_trait_missing;
            pub newtype UserId = i32;
            pub trait Score { fn score(value: Self) -> i32 effects {}; }
            fn score_i32(value: i32) -> i32 effects {} { return value; }
            impl self::fixture::newtype_trait_missing::Score for i32 { score = self::fixture::newtype_trait_missing::score_i32; }
            pub fn main() -> i32 effects {} {
                return self::fixture::newtype_trait_missing::Score.score(
                    self::fixture::newtype_trait_missing::UserId.wrap(1));
            }
            """;
        var missingTrait = await ExpectDiagnosticsAsync(
            harness, "newtype-does-not-forward-representation-trait", missingTraitSource, "E_TRAIT_IMPL_MISSING");
        AssertNewtypeDiagnosticSequence(missingTraitSource, missingTrait,
            [("E_TRAIT_IMPL_MISSING", "score", 3)]);

        const string nonComparableSource = """
            module fixture::newtype_non_comparable;
            pub newtype SecretText = Secret<Text>;
            pub fn compare(left: self::fixture::newtype_non_comparable::SecretText, right: self::fixture::newtype_non_comparable::SecretText) -> bool effects {} {
                return left == right;
            }
            """;
        var nonComparable = await ExpectDiagnosticsAsync(
            harness, "newtype-preserves-representation-equality-limit", nonComparableSource, "E_TYPE_MISMATCH");
        AssertNewtypeDiagnosticSequence(nonComparableSource, nonComparable,
            [("E_TYPE_MISMATCH", "==", 1)]);

        await AssertNewtypeOrphanRulesAsync(harness);
        await AssertNewtypeCodecRejectionsAsync(harness);
        await AssertNewtypeProjectionEvaluatesOnceAsync(harness);
        await AssertNewtypeReportsRuntimeAndAotAsync(harness);
    }

    private static void AssertNewtypeDiagnosticSequence(
        string source,
        IReadOnlyList<DiagnosticSnapshot> actual,
        params (string Code, string Token, int Occurrence)[] expected)
    {
        AssertEqual(expected.Length, actual.Count,
            $"Expected {expected.Length} exact newtype diagnostics, received {actual.Count}.");
        for (var index = 0; index < expected.Length; index++)
        {
            AssertEqual(expected[index].Code, actual[index].Code,
                $"Unexpected newtype diagnostic code at index {index}.");
            try
            {
                AssertRangeAtToken(source, actual[index], expected[index].Token, expected[index].Occurrence);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"Newtype diagnostic {index + 1} for {source.Split('\n', 2)[0]} expected token " +
                    $"'{expected[index].Token}' occurrence {expected[index].Occurrence}; actual range " +
                    $"{actual[index].StartLine}:{actual[index].StartColumn}-{actual[index].EndLine}:{actual[index].EndColumn} " +
                    $"in source line '{source.Split('\n')[actual[index].StartLine - 1]}': {exception.Message}",
                    exception);
            }
        }
    }

    private static void AssertNewtypeRangeAtLiteral(
        string source,
        DiagnosticSnapshot diagnostic,
        string literal,
        int occurrence)
    {
        var offset = -1;
        var searchFrom = 0;
        for (var index = 0; index < occurrence; index++)
        {
            offset = source.IndexOf(literal, searchFrom, StringComparison.Ordinal);
            if (offset < 0)
                throw new InvalidOperationException($"Could not find literal occurrence {occurrence} of {literal} in source.");
            searchFrom = offset + literal.Length;
        }

        var position = PositionOf(source, offset);
        AssertEqual(position.Line, diagnostic.StartLine, $"Unexpected diagnostic start line for literal {literal}.");
        AssertEqual(position.Column, diagnostic.StartColumn, $"Unexpected diagnostic start column for literal {literal}.");
        AssertEqual(position.Line, diagnostic.EndLine, $"Unexpected diagnostic end line for literal {literal}.");
        AssertEqual(position.Column + literal.Length, diagnostic.EndColumn, $"Unexpected diagnostic end column for literal {literal}.");
    }

    private static async Task AssertNewtypeOrphanRulesAsync(Harness harness)
    {
        const string localTraitManifest = """
            name = "newtype-local-trait-impl"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            remote = "../remote"
            """;
        const string localTraitSource = """
            module app::model;
            pub newtype LocalId = i32;
            fn local_measure(value: self::app::model::LocalId) -> i32 effects {} { return value.value; }
            pub impl remote::traits::Measure for self::app::model::LocalId {
                measure = self::app::model::local_measure;
            }
            pub fn measure(value: self::app::model::LocalId) -> i32 effects {} {
                return remote::traits::Measure.measure(value);
            }
            """;
        const string foreignTraitSource = """
            module traits;
            pub trait Measure { fn measure(value: Self) -> i32 effects {}; }
            """;
        var localRoot = await harness.WritePackageGraphAsync(
            "newtype-local-foreign-trait-orphan-success",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(localTraitManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/model.lang"] = localTraitSource }),
                ["remote"] = new PackageFixture(LibraryPackageManifest("newtype-foreign-trait"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/traits.lang"] = foreignTraitSource })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-local-foreign-trait-lock", localRoot, "lock"));
        var localCheck = await harness.InvokePackageDirectoryAsync(
            "newtype-local-foreign-trait-check", localRoot, "check", "--json");
        AssertEqual(0, localCheck.ExitCode, Describe(localCheck));
        AssertEqual(0, ParseDiagnosticSnapshots(localCheck.StandardOutput).Length, Describe(localCheck));

        const string foreignBothManifest = """
            name = "newtype-foreign-both-impl"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            remote = "../remote"
            model = "../model"
            """;
        const string foreignTypeSource = """
            module ids;
            pub newtype UserId = i32;
            """;
        const string foreignBothSource = """
            module app::impls;
            fn measure_user(value: model::ids::UserId) -> i32 effects {} { return value.value; }
            impl remote::traits::Measure for model::ids::UserId {
                measure = self::app::impls::measure_user;
            }
            """;
        var foreignRoot = await harness.WritePackageGraphAsync(
            "newtype-foreign-trait-foreign-type-orphan-rejected",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(foreignBothManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/impls.lang"] = foreignBothSource }),
                ["remote"] = new PackageFixture(LibraryPackageManifest("newtype-orphan-trait"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/traits.lang"] = foreignTraitSource }),
                ["model"] = new PackageFixture(LibraryPackageManifest("newtype-orphan-model"),
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["src/ids.lang"] = foreignTypeSource })
            });
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-foreign-both-lock", foreignRoot, "lock"));
        var orphanRejected = await harness.InvokePackageDirectoryAsync(
            "newtype-foreign-both-check", foreignRoot, "check", "--json");
        var orphanDiagnostics = ParseDiagnosticSnapshots(orphanRejected.StandardOutput);
        AssertEqual(1, orphanRejected.ExitCode, Describe(orphanRejected));
        AssertEqual(string.Empty, orphanRejected.StandardError, Describe(orphanRejected));
        AssertEqual(1, orphanDiagnostics.Length, Describe(orphanRejected));
        AssertEqual("E_TRAIT_IMPL_SIGNATURE", orphanDiagnostics[0].Code, Describe(orphanRejected));
        AssertEqual(Path.GetFullPath(Path.Combine(foreignRoot, "src", "app", "impls.lang")),
            Path.GetFullPath(orphanDiagnostics[0].File), Describe(orphanRejected));
        AssertRangeAtToken(foreignBothSource, orphanDiagnostics[0], "impl", 1);
    }

    private static async Task AssertNewtypeCodecRejectionsAsync(Harness harness)
    {
        async Task AssertRejectedWithoutArtifactsAsync(
            string caseName,
            string manifest,
            IReadOnlyDictionary<string, string> sourceFiles,
            string expectedCode,
            string sourceFile,
            string rangeToken,
            int occurrence = 1)
        {
            var packageRoot = await harness.WritePackageAsync(caseName, manifest, sourceFiles);
            var check = await harness.InvokePackageDirectoryAsync(
                $"{caseName}-check", packageRoot, "check", "--json");
            AssertTrue(check.ExitCode != 0, Describe(check));
            AssertEqual(string.Empty, check.StandardError, Describe(check));
            var diagnostics = ParseDiagnosticSnapshots(check.StandardOutput);
            AssertEqual(1, diagnostics.Length, Describe(check));
            AssertEqual(expectedCode, diagnostics[0].Code, Describe(check));
            var source = sourceFiles[sourceFile];
            try
            {
                AssertRangeAtToken(source, diagnostics[0], rangeToken, occurrence);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidOperationException(
                    $"Codec rejection {caseName} expected token '{rangeToken}' occurrence {occurrence}; " +
                    $"actual range {diagnostics[0].StartLine}:{diagnostics[0].StartColumn}-" +
                    $"{diagnostics[0].EndLine}:{diagnostics[0].EndColumn} in " +
                    $"'{source.Split('\n')[diagnostics[0].StartLine - 1]}': {exception.Message}",
                    exception);
            }

            var build = await harness.InvokePackageDirectoryWithTimeoutAsync(
                $"{caseName}-build", packageRoot, "build", TimeSpan.FromMinutes(2));
            AssertTrue(build.ExitCode != 0, Describe(build));
            var outputDirectory = Path.Combine(packageRoot, "out");
            var generatedFiles = Directory.Exists(outputDirectory)
                ? Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories).ToArray()
                : [];
            AssertEqual(0, generatedFiles.Length,
                $"The rejected {caseName} package must leave no output artifacts: {string.Join(", ", generatedFiles)}.");
        }

        const string cliManifest = """
            name = "newtype-cli-codec-rejected"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"
            """;
        const string cliArgumentSource = """
            module app::main;
            pub newtype UserId = i32;
            pub union Failure { Failed }
            command echo {
                help "Echo a user id.";
                argument user: self::app::main::UserId help "User id.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }
            pub fn run(args: self::app::main::EchoArgs) -> Result<Text, self::app::main::Failure> effects {} { return Ok("ok"); }
            pub fn describe(error: self::app::main::Failure) -> Text effects {} {
                return match error { self::app::main::Failure.Failed => "failed" };
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-cli-argument-codec",
            cliManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = cliArgumentSource },
            "E_COMMAND_DECL",
            "src/app/main.lang",
            "argument",
            1);

        const string webManifest = """
            name = "newtype-route-codec-rejected"
            version = "0.1.0"
            kind = "web"
            source_root = "src"
            entry_module = "app::main"

            [capabilities]
            net.listen = "allow"
            """;
        const string pathBindingSource = """
            module app::main;
            pub newtype UserId = i32;
            pub union Reply { Ready }
            fn ready(id: self::app::main::UserId) -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }
            route GET "/users/{id}" {
                path id: self::app::main::UserId;
                handler: self::app::main::ready;
                response Ready: 200;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-path-binding-codec",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = pathBindingSource },
            "E_ROUTE_BINDING",
            "src/app/main.lang",
            "self",
            4);

        const string queryBindingSource = """
            module app::main;
            pub newtype UserId = i32;
            pub union Reply { Ready }
            fn ready(id: self::app::main::UserId) -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }
            route GET "/users" {
                query id: self::app::main::UserId;
                handler: self::app::main::ready;
                response Ready: 200;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-query-binding-codec",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = queryBindingSource },
            "E_ROUTE_BINDING",
            "src/app/main.lang",
            "self",
            4);

        const string postBodySource = """
            module app::main;
            pub newtype RequestBody = i32;
            pub union Reply { Accepted }
            fn post(request: self::app::main::RequestBody) -> self::app::main::Reply effects {} { return self::app::main::Reply.Accepted; }
            route POST "/items" {
                body: self::app::main::RequestBody;
                handler: self::app::main::post;
                response Accepted: 200;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-direct-body-codec",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = postBodySource },
            "E_ROUTE_DECL",
            "src/app/main.lang",
            "self",
            4);

        const string newtypeResponseSource = """
            module app::main;
            pub newtype UserId = i32;
            pub union Reply { User(self::app::main::UserId), Empty }
            fn get() -> self::app::main::Reply effects {} {
                return self::app::main::Reply.User(self::app::main::UserId.wrap(1));
            }
            route GET "/users" {
                handler: self::app::main::get;
                response User: 200 json self::app::main::UserId;
                response Empty: 204;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-json-response-codec",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = newtypeResponseSource },
            "E_ROUTE_CODEC_UNSUPPORTED",
            "src/app/main.lang",
            "self",
            6);

        const string nestedRequestSource = """
            module app::main;
            pub newtype UserId = i32;
            pub struct Request { user: self::app::main::UserId }
            pub union Reply { Accepted }
            fn post(request: self::app::main::Request) -> self::app::main::Reply effects {} { return self::app::main::Reply.Accepted; }
            route POST "/users" {
                body: self::app::main::Request;
                handler: self::app::main::post;
                response Accepted: 200;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-nested-request-codec",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = nestedRequestSource },
            "E_ROUTE_CODEC_UNSUPPORTED",
            "src/app/main.lang",
            "self",
            5);

        const string invalidNewtypeBodySource = """
            module app::main;
            newtype Broken = Option<self::app::main::Missing>;
            struct Body { value: self::app::main::Broken }
            union Reply { Accepted }
            fn post(request: self::app::main::Body) -> self::app::main::Reply effects {} { return self::app::main::Reply.Accepted; }
            route POST "/items" {
                body: self::app::main::Body;
                handler: self::app::main::post;
                response Accepted: 200;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-route-invalid-representation-preserves-type-diagnostic",
            webManifest,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = invalidNewtypeBodySource },
            "E_NAME_UNRESOLVED",
            "src/app/main.lang",
            "self");

        const string sqliteManifest = """
            name = "newtype-sqlite-codec-rejected"
            version = "0.1.0"
            kind = "web"
            source_root = "src"
            entry_module = "app::main"
            sqlite_path = "data/newtype.sqlite3"
            sqlite_schema = "db/schema.sql"

            [capabilities]
            net.listen = "allow"
            db.read = "allow"
            db.write = "allow"
            """;
        const string sqliteHeader = """
            module app::main;
            pub union Reply { Ready }
            fn ready() -> self::app::main::Reply effects {} { return self::app::main::Reply.Ready; }
            route GET "/" { handler: self::app::main::ready; response Ready: 200; }
            """;
        string ComposeSqliteSource(string body) =>
            sqliteHeader + "\n" + body[(body.IndexOf('\n') + 1)..];
        const string sqliteSchema = "CREATE TABLE IF NOT EXISTS users (id INTEGER NOT NULL);\n";
        const string nestedSqliteParameterSource = """
            module app::main;
            pub newtype UserId = i32;
            pub struct Parameters { id: self::app::main::UserId }
            pub struct Row { id: i32 }
            fn load(db: DbRead, parameters: self::app::main::Parameters) -> bool effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM users", parameters);
                return true;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-sqlite-parameter-field-codec",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = ComposeSqliteSource(nestedSqliteParameterSource),
                ["db/schema.sql"] = sqliteSchema
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.lang",
            "id");

        const string nestedSqliteRowSource = """
            module app::main;
            pub newtype UserId = i32;
            pub struct Parameters {}
            pub struct Row { id: self::app::main::UserId }
            fn load(db: DbRead, parameters: self::app::main::Parameters) -> bool effects { db.read } {
                let loaded: Result<Option<self::app::main::Row>, DbError> = db.query_one("SELECT id FROM users", parameters);
                return true;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-sqlite-row-field-codec",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = ComposeSqliteSource(nestedSqliteRowSource),
                ["db/schema.sql"] = sqliteSchema
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.lang",
            "id");

        const string directSqliteRowSource = """
            module app::main;
            pub newtype UserId = i32;
            pub struct Parameters {}
            fn load(db: DbRead, parameters: self::app::main::Parameters) -> bool effects { db.read } {
                let loaded: Result<Option<self::app::main::UserId>, DbError> = db.query_one("SELECT id FROM users", parameters);
                return true;
            }
            """;
        await AssertRejectedWithoutArtifactsAsync(
            "newtype-sqlite-direct-row-codec",
            sqliteManifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = ComposeSqliteSource(directSqliteRowSource),
                ["db/schema.sql"] = sqliteSchema
            },
            "E_DB_CODEC_UNSUPPORTED",
            "src/app/main.lang",
            "db",
            3);
    }

    private static async Task AssertNewtypeProjectionEvaluatesOnceAsync(Harness harness)
    {
        const string manifest = """
            name = "newtype-projector-evaluation"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"

            [capabilities]
            log.write = "allow"
            """;
        const string source = """
            module app::main;
            pub union ProjectionError { Failed }
            pub newtype UserId = i32;

            command project {
                help "Project one value.";
                argument input: i32 help "The value to wrap and project.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }

            fn logged_user(logger: Logger, value: i32) -> self::app::main::UserId effects { log.write } {
                let logged: bool = logger.info("newtype-projection", "evaluated");
                return self::app::main::UserId.wrap(value);
            }

            pub fn run(args: self::app::main::ProjectArgs, logger: Logger) -> Result<Text, self::app::main::ProjectionError> effects { log.write } {
                let projected: i32 = self::app::main::logged_user(logger, args.input).value;
                if projected == args.input { return Ok("projected"); } else { return Err(self::app::main::ProjectionError.Failed); }
            }

            pub fn describe(error: self::app::main::ProjectionError) -> Text effects {} {
                return match error { self::app::main::ProjectionError.Failed => "projection failed" };
            }
            """;
        var packageRoot = await harness.WritePackageAsync(
            "newtype-projector-evaluation",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.lang"] = source
            });
        var lockResult = await harness.InvokePackageDirectoryAsync(
            "newtype-projector-evaluation-lock", packageRoot, "lock");
        AssertEqual(0, lockResult.ExitCode, Describe(lockResult));
        AssertEqual(string.Empty, lockResult.StandardError, Describe(lockResult));
        AssertEqual(
            $"No dependencies to lock for package 'newtype-projector-evaluation'.{Environment.NewLine}",
            lockResult.StandardOutput,
            Describe(lockResult));
        var run = await harness.InvokePackageDirectoryAsync(
            "newtype-projector-evaluation-run", packageRoot, "run", "--", "project", "7");
        AssertEqual(0, run.ExitCode, Describe(run));
        AssertEqual("projected" + Environment.NewLine, run.StandardOutput, Describe(run));
        var logLines = run.StandardError.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        AssertEqual(1, logLines.Length,
            $"A projection target with one Logger.info operation must evaluate once. {Describe(run)}");
        using var log = JsonDocument.Parse(logLines[0]);
        AssertJsonPropertyOrder(log.RootElement, "level,event,detail");
        AssertEqual("info", log.RootElement.GetProperty("level").GetString(), Describe(run));
        AssertEqual("newtype-projection", log.RootElement.GetProperty("event").GetString(), Describe(run));
        AssertEqual("evaluated", log.RootElement.GetProperty("detail").GetString(), Describe(run));
    }

    private static async Task AssertNewtypeReportsRuntimeAndAotAsync(Harness harness)
    {
        const string rootManifest = """
            name = "newtype-consumer"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"

            [dependencies]
            core = "../core"
            core_alias = "../core"
            """;
        const string coreManifest = """
            name = "newtype-core"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"

            [dependencies]
            base = "../foundation"
            """;
        const string foundationManifest = """
            name = "newtype-foundation"
            version = "0.1.0"
            kind = "lib"
            source_root = "src"
            """;
        const string foundationSource = """
            module types;
            pub newtype TenantId = i32;
            pub fn wrap_tenant(value: i32) -> self::types::TenantId effects {} {
                return self::types::TenantId.wrap(value);
            }
            """;
        const string coreSource = """
            module ids;
            pub newtype UserId = i32;
            pub newtype OrderId = i32;
            newtype PrivateId = i32;

            pub struct Box<T> { value: T }
            pub union Maybe<T> { Some(T), None }
            pub trait Score { fn score(value: Self) -> i32 effects {}; }

            fn score_i32(value: i32) -> i32 effects {} { return value; }
            fn score_user(value: self::ids::UserId) -> i32 effects {} { return value.value * 10; }
            fn score_order(value: self::ids::OrderId) -> i32 effects {} { return value.value * 100; }
            fn score_private(value: self::ids::PrivateId) -> i32 effects {} { return value.value; }

            pub impl self::ids::Score for i32 { score = self::ids::score_i32; }
            pub impl self::ids::Score for self::ids::UserId { score = self::ids::score_user; }
            pub impl self::ids::Score for self::ids::OrderId { score = self::ids::score_order; }
            impl self::ids::Score for self::ids::PrivateId { score = self::ids::score_private; }

            pub fn identity<T>(value: T) -> T effects {} { return value; }
            pub fn read(value: self::ids::UserId) -> i32 effects {} { return value.value; }
            pub fn tenant_identity(value: base::types::TenantId) -> base::types::TenantId effects {} { return value; }
            pub fn dispatch<T: self::ids::Score>(value: T) -> i32 effects {} {
                return self::ids::Score.score(value);
            }
            fn unused_private() -> i32 effects {} { return 1; }
            """;
        const string rootSource = """
            module app::main;
            pub newtype BoxedUser = core::ids::Box<core::ids::UserId>;
            pub newtype TaggedUser = core::ids::Maybe<core::ids::UserId>;
            pub newtype UserList = List<self::app::main::BoxedUser>;
            pub newtype UserMap = Map<Text, self::app::main::BoxedUser>;
            struct Holder { id: core::ids::UserId }

            pub fn bridge(value: core::ids::UserId) -> core_alias::ids::UserId effects {} {
                return core_alias::ids::identity(value);
            }

            fn box_user(value: core::ids::UserId) -> self::app::main::BoxedUser effects {} {
                return self::app::main::BoxedUser.wrap(
                    core::ids::Box<core::ids::UserId> { value: value });
            }

            pub fn same_box(left: self::app::main::BoxedUser, right: self::app::main::BoxedUser) -> bool effects {} {
                return left == right;
            }

            fn tagged_user(value: core::ids::UserId) -> self::app::main::TaggedUser effects {} {
                return self::app::main::TaggedUser.wrap(
                    core::ids::Maybe<core::ids::UserId>.Some(value));
            }

            pub fn same_maybe(left: self::app::main::TaggedUser, right: self::app::main::TaggedUser) -> bool effects {} {
                return left == right;
            }

            fn user_list(value: self::app::main::BoxedUser) -> self::app::main::UserList effects {} {
                let values: List<self::app::main::BoxedUser> = [value];
                return self::app::main::UserList.wrap(values);
            }

            fn same_user_list(left: self::app::main::UserList, right: self::app::main::UserList) -> bool effects {} {
                return left == right;
            }

            fn user_map(value: self::app::main::BoxedUser) -> self::app::main::UserMap effects {} {
                let empty: Map<Text, self::app::main::BoxedUser> = Map.empty();
                return self::app::main::UserMap.wrap(empty.set("user", value));
            }

            fn same_user_map(left: self::app::main::UserMap, right: self::app::main::UserMap) -> bool effects {} {
                return left == right;
            }

            fn verify_newtype_equality(user: core::ids::UserId, other_user: core::ids::UserId) -> bool effects {} {
                let box_left: self::app::main::BoxedUser = self::app::main::box_user(user);
                let box_right: self::app::main::BoxedUser = self::app::main::box_user(user);
                let box_different: self::app::main::BoxedUser = self::app::main::box_user(other_user);
                let maybe_left: self::app::main::TaggedUser = self::app::main::tagged_user(user);
                let maybe_right: self::app::main::TaggedUser = self::app::main::tagged_user(user);
                let maybe_different: self::app::main::TaggedUser = self::app::main::tagged_user(other_user);
                let list_left: self::app::main::UserList = self::app::main::user_list(box_left);
                let list_right: self::app::main::UserList = self::app::main::user_list(box_right);
                let list_different: self::app::main::UserList = self::app::main::user_list(box_different);
                let map_left: self::app::main::UserMap = self::app::main::user_map(box_left);
                let map_right: self::app::main::UserMap = self::app::main::user_map(box_right);
                let map_different: self::app::main::UserMap = self::app::main::user_map(box_different);
                if self::app::main::same_box(box_left, box_right) {
                    if self::app::main::same_maybe(maybe_left, maybe_right) {
                        if self::app::main::same_user_list(list_left, list_right) {
                            if self::app::main::same_user_map(map_left, map_right) {
                                if self::app::main::same_box(box_left, box_different) {
                                    return false;
                                } else {
                                    if self::app::main::same_maybe(maybe_left, maybe_different) {
                                        return false;
                                    } else {
                                        if self::app::main::same_user_list(list_left, list_different) {
                                            return false;
                                        } else {
                                            if self::app::main::same_user_map(map_left, map_different) {
                                                return false;
                                            } else {
                                                return true;
                                            }
                                        }
                                    }
                                }
                            } else { return false; }
                        } else { return false; }
                    } else { return false; }
                } else { return false; }
            }

            pub fn main() -> i32 effects {} {
                let user: core::ids::UserId = core::ids::UserId.wrap(3);
                let other_user: core::ids::UserId = core::ids::UserId.wrap(4);
                let holder: self::app::main::Holder = self::app::main::Holder { id: user };
                let copied: core_alias::ids::UserId = self::app::main::bridge(holder.id);
                let same: bool = copied == user;
                let raw: i32 = core::ids::read(self::app::main::box_user(user).value.value);
                let user_score: i32 = core::ids::Score.score(copied);
                let order: core_alias::ids::OrderId = core_alias::ids::OrderId.wrap(4);
                let order_score: i32 = core_alias::ids::dispatch(order);
                let int_score: i32 = core::ids::dispatch(5);
                let values_equal: bool = self::app::main::verify_newtype_equality(user, other_user);
                if same {
                    if values_equal {
                        if raw == 3 {
                            if user_score == 30 {
                                if order_score == 400 {
                                    if int_score == 5 { return 438; }
                                }
                            }
                        }
                    }
                }
                return 0;
            }
            """;

        async Task<string> CreateGraphAsync(string caseName)
        {
            return await harness.WritePackageGraphAsync(
                caseName,
                new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
                {
                    ["root"] = new PackageFixture(rootManifest,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["src/app/main.lang"] = rootSource }),
                    ["core"] = new PackageFixture(coreManifest,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["src/ids.lang"] = coreSource }),
                    ["foundation"] = new PackageFixture(foundationManifest,
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["src/types.lang"] = foundationSource })
                });
        }

        async Task<(string Api, string Audit)> ReadReportsAsync(string root)
        {
            var api = await harness.InvokeCompilerCommandAsync("inspect", "api", root, "--json");
            AssertEqual(0, api.ExitCode, Describe(api));
            AssertEqual(string.Empty, api.StandardError, Describe(api));
            var audit = await harness.InvokeCompilerCommandAsync("audit", root, "--json");
            AssertEqual(0, audit.ExitCode, Describe(audit));
            AssertEqual(string.Empty, audit.StandardError, Describe(audit));
            using (var apiDocument = JsonDocument.Parse(api.StandardOutput))
            {
                var rootElement = apiDocument.RootElement;
                AssertInspectApiPropertyOrder(rootElement);
                AssertEqual(11, rootElement.GetProperty("schema_version").GetInt32(),
                    "Newtype API output must use current inspect schema 11.");
                AssertNewtypeApiFacts(rootElement);
                AssertApiPortable(api.StandardOutput, rootElement, harness.TemporaryRoot);
            }
            using (var auditDocument = JsonDocument.Parse(audit.StandardOutput))
            {
                var rootElement = auditDocument.RootElement;
                AssertAuditPropertyOrder(rootElement);
                AssertEqual(9, rootElement.GetProperty("schema_version").GetInt32(),
                    "Newtype audit output must use current audit schema 9.");
                AssertNewtypeAuditFacts(rootElement);
                AssertAuditPortable(audit.StandardOutput, root, harness.TemporaryRoot);
            }
            return (api.StandardOutput, audit.StandardOutput);
        }

        var packageRoot = await CreateGraphAsync("newtype-public-dependency-graph");
        var coreRoot = Path.GetFullPath(Path.Combine(packageRoot, "..", "core"));
        var coreSourcePath = Path.Combine(coreRoot, "src", "ids.lang");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-public-dependency-lock", packageRoot, "lock"));

        var run = await harness.InvokePackageDirectoryAsync("newtype-public-dependency-run", packageRoot, "run");
        AssertRunOutput("438" + Environment.NewLine, run);

        var reports = await ReadReportsAsync(packageRoot);
        var repeated = await ReadReportsAsync(packageRoot);
        AssertEqual(reports.Api, repeated.Api, "Repeated newtype API output must be byte-identical.");
        AssertEqual(reports.Audit, repeated.Audit, "Repeated newtype audit output must be byte-identical.");

        var managedBuild = await harness.InvokePackageDirectoryAsync(
            "newtype-managed-build", packageRoot, "build");
        AssertEqual(0, managedBuild.ExitCode, Describe(managedBuild));
        var managedArtifact = ParseBuiltArtifact(managedBuild, "Built executable: ");
        var managedOutputDirectory = Path.GetDirectoryName(managedArtifact)!;
        var managedRelativeArtifact = Path.GetRelativePath(managedOutputDirectory, managedArtifact)
            .Replace(Path.DirectorySeparatorChar, '/');
        string baselineReceiptHash;
        using (var receipt = await AssertBuildReceiptAsync(
                   managedOutputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [managedRelativeArtifact],
                   packageRoot,
                   harness.TemporaryRoot))
        {
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "Newtype builds must preserve receipt schema 3.");
            baselineReceiptHash = receipt.RootElement.GetProperty("audit_snapshot_sha256").GetString() ?? string.Empty;
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X64
            || (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux()))
            throw new IntegrationTestSkippedException("The nominal-newtype NativeAOT test requires Windows x64 or Linux x64.");
        var aotBuild = await harness.InvokePackageDirectoryWithTimeoutAsync(
            "newtype-current-host-aot-build",
            packageRoot,
            "build",
            AotPublishTimeout,
            "--aot",
            "--rid",
            CurrentHostAotRid());
        AssertEqual(0, aotBuild.ExitCode, Describe(aotBuild));
        var nativeArtifact = ParseBuiltArtifact(aotBuild, "Built native executable: ");
        AssertRunOutput("438" + Environment.NewLine,
            await ExecuteNativeAsync(nativeArtifact, TimeSpan.FromSeconds(30)));

        var originalCoreSource = await File.ReadAllTextAsync(coreSourcePath);
        const string bodyAnchor = "fn unused_private() -> i32 effects {} { return 1; }";
        var bodyEdited = originalCoreSource.Replace(
            bodyAnchor,
            "fn unused_private() -> i32 effects {} { return 2; }",
            StringComparison.Ordinal);
        AssertTrue(bodyEdited != originalCoreSource, "The body-edit identity check must change the private helper body.");
        await File.WriteAllTextAsync(coreSourcePath, bodyEdited);

        var staleApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertTrue(staleApi.ExitCode != 0 && staleApi.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"A body edit must require a fresh lock before inspection. {Describe(staleApi)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-body-edit-relock", packageRoot, "lock"));
        var editedReports = await ReadReportsAsync(packageRoot);
        AssertEqual(reports.Api, editedReports.Api,
            "A private implementation body edit must not change the public newtype API.");
        AssertTrue(reports.Audit != editedReports.Audit,
            "Audit input hashes must reflect a changed private implementation body.");
        AssertEqual(NewtypeIdProjection(reports.Api), NewtypeIdProjection(editedReports.Api),
            "Public newtype identities must survive private implementation body edits.");
        AssertEqual(NewtypeIdProjection(reports.Audit), NewtypeIdProjection(editedReports.Audit),
            "Audit newtype identities must survive private implementation body edits.");
        AssertEqual(TraitImplIdProjection(reports.Audit), TraitImplIdProjection(editedReports.Audit),
            "Audit trait implementation identities must survive private implementation body edits.");

        var editedBuild = await harness.InvokePackageDirectoryAsync(
            "newtype-body-edited-managed-build", packageRoot, "build");
        AssertEqual(0, editedBuild.ExitCode, Describe(editedBuild));
        var editedArtifact = ParseBuiltArtifact(editedBuild, "Built executable: ");
        var editedOutputDirectory = Path.GetDirectoryName(editedArtifact)!;
        var editedRelativeArtifact = Path.GetRelativePath(editedOutputDirectory, editedArtifact)
            .Replace(Path.DirectorySeparatorChar, '/');
        using (var receipt = await AssertBuildReceiptAsync(
                   editedOutputDirectory,
                   "managed",
                   expectedRuntimeIdentifier: null,
                   [editedRelativeArtifact],
                   packageRoot,
                   harness.TemporaryRoot))
        {
            AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
                "A body-edited newtype build must preserve receipt schema 3.");
            AssertTrue(baselineReceiptHash != receipt.RootElement.GetProperty("audit_snapshot_sha256").GetString(),
                "The receipt audit snapshot hash must change after a source-body edit.");
        }

        var afterBodyEdit = await File.ReadAllTextAsync(coreSourcePath);
        var commentAndFormatEdited = afterBodyEdit
            .Replace("module ids;\n", "module ids;\n\n", StringComparison.Ordinal)
            .Replace(
                "fn unused_private() -> i32 effects {} { return 2; }",
                "// Formatting and comments do not define the newtype identity.\nfn unused_private() -> i32 effects {} { return 2; }",
                StringComparison.Ordinal);
        AssertTrue(commentAndFormatEdited != afterBodyEdit,
            "The comment/formatting stability check must change source text.");
        await File.WriteAllTextAsync(coreSourcePath, commentAndFormatEdited);
        var staleCommentApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertTrue(staleCommentApi.ExitCode != 0
            && staleCommentApi.StandardOutput.Contains("E_LOCK", StringComparison.Ordinal),
            $"A comment/formatting edit must require a fresh lock before inspection. {Describe(staleCommentApi)}");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-comment-format-relock", packageRoot, "lock"));
        var commentEditedReports = await ReadReportsAsync(packageRoot);
        AssertEqual(reports.Api, commentEditedReports.Api,
            "A comment/formatting edit must not change public newtype API metadata.");
        AssertTrue(editedReports.Audit != commentEditedReports.Audit,
            "Audit source hashes must reflect comment/formatting edits.");
        AssertEqual(NewtypeIdProjection(reports.Api), NewtypeIdProjection(commentEditedReports.Api),
            "Public newtype identities must survive comment/formatting edits.");
        AssertEqual(NewtypeIdProjection(reports.Audit), NewtypeIdProjection(commentEditedReports.Audit),
            "Audit newtype identities must survive comment/formatting edits.");
        AssertEqual(TraitImplIdProjection(reports.Audit), TraitImplIdProjection(commentEditedReports.Audit),
            "Audit trait implementation identities must survive comment/formatting edits.");

        var relocatedRoot = await CreateGraphAsync("newtype-public-dependency-relocated");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "newtype-relocated-lock", relocatedRoot, "lock"));
        var relocatedReports = await ReadReportsAsync(relocatedRoot);
        AssertEqual(reports.Api, relocatedReports.Api,
            "Newtype inspect IDs and references must survive physical package relocation.");
        AssertEqual(reports.Audit, relocatedReports.Audit,
            "Newtype audit IDs and package identities must survive physical package relocation.");
        AssertEqual(TraitImplIdProjection(reports.Audit), TraitImplIdProjection(relocatedReports.Audit),
            "Trait implementation identities must survive physical package relocation.");
    }

    private static void AssertNewtypeApiFacts(JsonElement api)
    {
        var newtypes = api.GetProperty("newtypes").EnumerateArray().ToArray();
        AssertEqual(
            "core::ids::OrderId\ncore::ids::UserId\nself::app::main::BoxedUser\nself::app::main::TaggedUser\nself::app::main::UserList\nself::app::main::UserMap",
            string.Join("\n", newtypes.Select(item => item.GetProperty("id").GetString())),
            "API should expose exactly the public root and direct-dependency newtypes in stable identity order.");
        foreach (var newtype in newtypes)
            AssertJsonPropertyOrder(newtype, "id,source_ids,package,representation");
        AssertEqual(
            "core::ids::OrderId=i32\ncore::ids::UserId=i32\nself::app::main::BoxedUser=struct:Box<newtype:UserId>\nself::app::main::TaggedUser=union:Maybe<newtype:UserId>\nself::app::main::UserList=List<newtype:BoxedUser>\nself::app::main::UserMap=Map<Text,newtype:BoxedUser>",
            string.Join("\n", newtypes.Select(item =>
                $"{item.GetProperty("id").GetString()}={ReportTypeShape(item.GetProperty("representation"))}")),
            "API newtype representations should exactly preserve primitive, nominal, list, and map structure.");

        var userId = newtypes.Single(item => item.GetProperty("source_ids").EnumerateArray()
            .Any(source => source.GetString() is "core::ids::UserId" or "core_alias::ids::UserId"));
        AssertEqual(2, userId.GetProperty("source_ids").GetArrayLength(),
            "One dependency type should preserve both direct root aliases in its source IDs.");
        AssertEqual("core::ids::UserId\ncore_alias::ids::UserId",
            string.Join("\n", userId.GetProperty("source_ids").EnumerateArray().Select(source => source.GetString())),
            "The UserId source ID set should include exactly its two root aliases in ordinal order.");
        var representation = userId.GetProperty("representation");
        AssertEqual("primitive", representation.GetProperty("kind").GetString(),
            "UserId should report its checked i32 representation.");
        AssertEqual("i32", representation.GetProperty("name").GetString(),
            "The UserId representation should preserve its primitive name.");
        AssertTrue(!JsonSerializer.Serialize(api).Contains("PrivateId", StringComparison.Ordinal),
            "Inspect API must filter private newtype declarations.");

        var apiImplementations = api.GetProperty("trait_impls").EnumerateArray().ToArray();
        AssertEqual(3, apiImplementations.Length,
            "API should expose exactly the three public direct-dependency Score implementations.");
        AssertEqual(
            "i32:score\nnewtype:OrderId:score\nnewtype:UserId:score",
            string.Join("\n", apiImplementations
                .Select(item => $"{ReportTypeShape(item.GetProperty("target"))}:{string.Join(",", item.GetProperty("methods").EnumerateArray().Select(method => method.GetString()))}")
                .Order(StringComparer.Ordinal)),
            "API trait implementation targets should preserve the exact public primitive and newtype set.");
        foreach (var implementation in apiImplementations)
        {
            AssertTrue((implementation.GetProperty("id").GetString() ?? string.Empty)
                    .StartsWith("lang.impl.v1.", StringComparison.Ordinal),
                "Public API implementation IDs should use stable semantic identities.");
            AssertEqual("lang.trait.v1.root/dep:core::ids::Score",
                implementation.GetProperty("trait").GetString(),
                "Every public implementation should refer to the stable dependency trait ID.");
        }

        var functions = api.GetProperty("functions").EnumerateArray().ToArray();
        var bridge = functions.Single(function => function.GetProperty("id").GetString() == "self::app::main::bridge");
        var parameterType = bridge.GetProperty("parameters")[0].GetProperty("type");
        AssertEqual("nominal", parameterType.GetProperty("kind").GetString(),
            "Public signatures should encode a newtype as a nominal type.");
        AssertEqual("newtype", parameterType.GetProperty("declaration_kind").GetString(),
            "Nominal references should distinguish newtypes from structs and unions.");
        AssertEqual(0, parameterType.GetProperty("type_arguments").GetArrayLength(),
            "Nongeneric newtype references should serialize an explicit empty argument list.");

        var transitiveFunction = functions.Single(function => function.GetProperty("id").GetString() == "core::ids::tenant_identity");
        var transitiveType = transitiveFunction.GetProperty("parameters")[0].GetProperty("type");
        AssertEqual("newtype", transitiveType.GetProperty("declaration_kind").GetString(),
            "Transitive newtype signature references should keep their declaration kind.");
        AssertEqual(JsonValueKind.Null, transitiveType.GetProperty("source_id").ValueKind,
            "A transitive newtype reference should not invent a root-direct source ID.");
        AssertEqual(0, transitiveType.GetProperty("source_ids").GetArrayLength(),
            "A transitive newtype reference should have no root-direct alias IDs.");
    }

    private static void AssertNewtypeAuditFacts(JsonElement audit)
    {
        var compiler = audit.GetProperty("compiler");
        var newtypes = compiler.GetProperty("newtypes").EnumerateArray().ToArray();
        AssertEqual(
            "lang.newtype.v1.root/dep:core/dep:base::types::TenantId\nlang.newtype.v1.root/dep:core::ids::OrderId\nlang.newtype.v1.root/dep:core::ids::PrivateId\nlang.newtype.v1.root/dep:core::ids::UserId\nlang.newtype.v1.root::app::main::BoxedUser\nlang.newtype.v1.root::app::main::TaggedUser\nlang.newtype.v1.root::app::main::UserList\nlang.newtype.v1.root::app::main::UserMap",
            string.Join("\n", newtypes.Select(item => item.GetProperty("id").GetString())),
            "Audit should retain exactly the public, private, and transitive newtypes in stable identity order.");
        foreach (var newtype in newtypes)
        {
            AssertJsonPropertyOrder(newtype, "id,package,module,name,visibility,representation");
            AssertTrue((newtype.GetProperty("id").GetString() ?? string.Empty)
                    .StartsWith("lang.newtype.v1.", StringComparison.Ordinal),
                "Audit newtype IDs should use the portable current identity prefix.");
        }
        AssertEqual(
            "lang.newtype.v1.root/dep:core/dep:base::types::TenantId=i32\nlang.newtype.v1.root/dep:core::ids::OrderId=i32\nlang.newtype.v1.root/dep:core::ids::PrivateId=i32\nlang.newtype.v1.root/dep:core::ids::UserId=i32\nlang.newtype.v1.root::app::main::BoxedUser=struct:Box<newtype:UserId>\nlang.newtype.v1.root::app::main::TaggedUser=union:Maybe<newtype:UserId>\nlang.newtype.v1.root::app::main::UserList=List<newtype:BoxedUser>\nlang.newtype.v1.root::app::main::UserMap=Map<Text,newtype:BoxedUser>",
            string.Join("\n", newtypes.Select(item =>
                $"{item.GetProperty("id").GetString()}={ReportTypeShape(item.GetProperty("representation"))}")),
            "Audit newtype representations should exactly preserve all public, private, and transitive shapes.");
        var privateId = newtypes.Single(item => item.GetProperty("name").GetString() == "PrivateId");
        AssertEqual("private", privateId.GetProperty("visibility").GetString(),
            "Audit should retain private newtype declarations.");
        var auditImplementations = compiler.GetProperty("trait_impls").EnumerateArray().ToArray();
        AssertEqual(4, auditImplementations.Length,
            "Audit should retain all three public and the private Score implementation.");
        AssertEqual(
            "i32|public|score_i32\nnewtype:OrderId|public|score_order\nnewtype:PrivateId|private|score_private\nnewtype:UserId|public|score_user",
            string.Join("\n", auditImplementations.Select(item =>
            {
                var binding = item.GetProperty("methods").EnumerateArray().Single().GetProperty("binding_function");
                return $"{ReportTypeShape(item.GetProperty("target"))}|{item.GetProperty("visibility").GetString()}|{binding.GetProperty("name").GetString()}";
            }).Order(StringComparer.Ordinal)),
            "Audit should preserve exact public/private implementation targets and binding functions.");
        var userImplementation = auditImplementations.Single(item =>
            item.GetProperty("target").GetProperty("kind").GetString() == "nominal"
            && item.GetProperty("target").GetProperty("declaration_kind").GetString() == "newtype"
            && item.GetProperty("target").GetProperty("name").GetString() == "UserId");
        AssertTrue((userImplementation.GetProperty("id").GetString() ?? string.Empty)
                .StartsWith("lang.impl.v1.", StringComparison.Ordinal),
            "A newtype impl should retain its stable closed-target identity.");

        var intImplementation = auditImplementations.Single(item =>
            item.GetProperty("target").GetProperty("kind").GetString() == "primitive"
            && item.GetProperty("target").GetProperty("name").GetString() == "i32");
        AssertTrue(userImplementation.GetProperty("id").GetString() != intImplementation.GetProperty("id").GetString(),
            "An i32 impl and a newtype impl must remain distinct witnesses.");
        var functions = compiler.GetProperty("functions").EnumerateArray().ToArray();
        var rootMain = functions.Single(item => item.GetProperty("name").GetString() == "main");
        var directCalls = rootMain.GetProperty("direct_calls").EnumerateArray().ToArray();
        AssertTrue(directCalls.Any(call => call.GetProperty("name").GetString() == "score_user"),
            "Explicit UserId dispatch should reach the UserId implementation binding.");
        AssertTrue(!directCalls.Any(call => call.GetProperty("name").GetString() == "score_i32"),
            "A newtype dispatch path must not acquire the representation's impl binding edge.");
    }

    private static string ReportTypeShape(JsonElement type)
    {
        var kind = type.GetProperty("kind").GetString();
        return kind switch
        {
            "primitive" => type.GetProperty("name").GetString() ?? string.Empty,
            "nominal" => NominalShape(type),
            "list" => $"List<{ReportTypeShape(type.GetProperty("item"))}>",
            "map" => $"Map<{ReportTypeShape(type.GetProperty("key"))},{ReportTypeShape(type.GetProperty("value"))}>",
            "option" => $"Option<{ReportTypeShape(type.GetProperty("item"))}>",
            "result" => $"Result<{ReportTypeShape(type.GetProperty("ok"))},{ReportTypeShape(type.GetProperty("error"))}>",
            "secret" => $"Secret<{ReportTypeShape(type.GetProperty("item"))}>",
            "type_parameter" => $"{type.GetProperty("name").GetString()}#{type.GetProperty("ordinal").GetInt32()}",
            "self" => "Self",
            _ => throw new InvalidOperationException($"Unexpected report type kind {kind}.")
        };

        static string NominalShape(JsonElement nominal)
        {
            var head = $"{nominal.GetProperty("declaration_kind").GetString()}:{nominal.GetProperty("name").GetString()}";
            var arguments = nominal.GetProperty("type_arguments").EnumerateArray()
                .Select(ReportTypeShape)
                .ToArray();
            return arguments.Length == 0 ? head : $"{head}<{string.Join(",", arguments)}>";
        }
    }

    private static string NewtypeIdProjection(string json)
    {
        using var document = JsonDocument.Parse(json);
        var api = document.RootElement;
        if (api.TryGetProperty("newtypes", out var apiNewtypes))
            return string.Join("\n", apiNewtypes.EnumerateArray()
                .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        var auditNewtypes = api.GetProperty("compiler").GetProperty("newtypes");
        return string.Join("\n", auditNewtypes.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
    }

    private static string TraitImplIdProjection(string json)
    {
        using var document = JsonDocument.Parse(json);
        var implementations = document.RootElement.GetProperty("compiler").GetProperty("trait_impls");
        return string.Join("\n", implementations.EnumerateArray()
            .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
    }
}
