using System.Text.Json;

internal static partial class IntegrationTests
{
    private static async Task TestGenericSourceIdentityLifecycle(Harness harness)
    {
        const string dependencyAlias = "validation";
        const string dependencyName = "text-validation";
        const string dependencyRelativePath = "src/text/validation.hob";
        const string rootRelativePath = "src/app/main.hob";
        var consumerManifest = CliPackageManifest()
            .Replace("name = \"harness-package\"", "name = \"maintained-generic-consumer\"", StringComparison.Ordinal)
            + "\n[dependencies]\nvalidation = \"../validation\"\n";
        const string consumerSource = """
            module app::main;

            pub fn main() -> i32 effects {} {
                let normalized: validation::text::validation::Validation<validation::text::validation::Normalized<Text>> =
                    validation::text::validation::normalize("  current  ");
                let value: Text = match normalized {
                    validation::text::validation::Validation.Valid(result) => result.value,
                    validation::text::validation::Validation.Invalid(error) => ""
                };
                if value == "current" {
                    return 0;
                } else {
                    return 1;
                }
            }
            """;

        var packageRoot = await harness.WritePackageGraphAsync(
            "maintained-generic-source-identity",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(consumerManifest,
                    new Dictionary<string, string>(StringComparer.Ordinal) { [rootRelativePath] = consumerSource }),
                ["validation"] = new PackageFixture(LibraryPackageManifest(dependencyName),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [dependencyRelativePath] = GenericValidationLibrarySource("text::validation")
                    })
            });
        var dependencyRoot = Path.GetFullPath(Path.Combine(packageRoot, "..", "validation"));
        var dependencySourcePath = Path.Combine(dependencyRoot, dependencyRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var rootSourcePath = Path.Combine(packageRoot, rootRelativePath.Replace('/', Path.DirectorySeparatorChar));

        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-source-identity-lock", packageRoot, "lock"));

        var baselineRun = await harness.InvokePackageDirectoryAsync(
            "maintained-generic-source-identity-run", packageRoot, "run");
        AssertRunOutput("0" + Environment.NewLine, baselineRun);

        async Task AssertStaleLockRejectedAsync(string phase)
        {
            var check = await harness.InvokePackageDirectoryAsync(
                $"maintained-generic-{phase}-stale-check", packageRoot, "check", "--json");
            AssertGenericLockDiagnostic(check, $"{phase} check");

            var api = await harness.InvokeCompilerCommandAsync(
                "inspect", "api", packageRoot, "--json");
            AssertGenericLockDiagnostic(api, $"{phase} inspect api");

            var audit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
            AssertGenericLockDiagnostic(audit, $"{phase} audit");

            var beforeBuild = await CaptureMaintainedOutputSnapshotAsync(Path.Combine(packageRoot, "out"));
            var build = await harness.InvokePackageDirectoryAsync(
                $"maintained-generic-{phase}-stale-build", packageRoot, "build");
            AssertGenericLockDiagnostic(build, $"{phase} build");
            var afterBuild = await CaptureMaintainedOutputSnapshotAsync(Path.Combine(packageRoot, "out"));
            AssertTrue(beforeBuild.SequenceEqual(afterBuild),
                $"A stale {phase} build must not modify the existing output files.");
        }

        async Task<(string ApiJson, string AuditJson, string SemanticProjection,
            string RootInputHash, string DependencyInputHash, string AuditHash, string ReceiptProjection)> BuildCurrentAsync(
            string phase,
            string root)
        {
            var check = await harness.InvokePackageDirectoryAsync(
                $"maintained-generic-{phase}-check", root, "check", "--json");
            AssertEqual(0, check.ExitCode, Describe(check));
            AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

            var api = await harness.InvokeCompilerCommandAsync("inspect", "api", root, "--json");
            AssertEqual(0, api.ExitCode, Describe(api));
            var audit = await harness.InvokeCompilerCommandAsync("audit", root, "--json");
            AssertEqual(0, audit.ExitCode, Describe(audit));
            AssertGenericTraitReportFacts(api.StandardOutput, audit.StandardOutput, dependencyAlias, expectVisibleImpl: true);
            using (var apiDocument = JsonDocument.Parse(api.StandardOutput))
                AssertApiPortable(api.StandardOutput, apiDocument.RootElement, harness.TemporaryRoot);
            AssertAuditPortable(audit.StandardOutput, root, harness.TemporaryRoot);

            var build = await harness.InvokePackageDirectoryAsync(
                $"maintained-generic-{phase}-build", root, "build");
            AssertEqual(0, build.ExitCode, Describe(build));
            var artifact = ParseBuiltArtifact(build, "Built executable: ");
            var outputDirectory = Path.GetDirectoryName(artifact)!;
            var relativeArtifact = Path.GetRelativePath(outputDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/');
            using var receipt = await AssertBuildReceiptAsync(
                outputDirectory,
                "managed",
                expectedRuntimeIdentifier: null,
                [relativeArtifact],
                root,
                harness.TemporaryRoot);

            var receiptRoot = receipt.RootElement;
            AssertEqual(3, receiptRoot.GetProperty("schema_version").GetInt32(),
                "Maintained generic package builds use current build receipt schema 3.");
            var rootInputHash = MaintainedReceiptInputHash(receiptRoot, packageName: null, rootRelativePath);
            var dependencyInputHash = MaintainedReceiptInputHash(receiptRoot, dependencyName, dependencyRelativePath);
            var expectedRootHash = HashSha256(await File.ReadAllBytesAsync(
                Path.Combine(root, rootRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            var expectedDependencyHash = HashSha256(await File.ReadAllBytesAsync(
                Path.Combine(Path.GetFullPath(Path.Combine(root, "..", "validation")),
                    dependencyRelativePath.Replace('/', Path.DirectorySeparatorChar))));
            AssertEqual(expectedRootHash, rootInputHash, "The build receipt must bind the current root source bytes.");
            AssertEqual(expectedDependencyHash, dependencyInputHash, "The build receipt must bind the current generic library bytes.");

            return (
                api.StandardOutput,
                audit.StandardOutput,
                MaintainedTraitIdentityProjection(api.StandardOutput, audit.StandardOutput),
                rootInputHash,
                dependencyInputHash,
                receiptRoot.GetProperty("audit_snapshot_sha256").GetString() ?? string.Empty,
                MaintainedReceiptProjection(receiptRoot));
        }

        var baseline = await BuildCurrentAsync("baseline", packageRoot);
        var baselineSource = await File.ReadAllTextAsync(dependencySourcePath);
        AssertTrue(baselineSource.Contains("return Ok(input.trim());", StringComparison.Ordinal),
            "The maintained generic source helper must retain the stable normalize_text body anchor.");
        var baselineOutputSnapshot = await CaptureMaintainedOutputSnapshotAsync(Path.Combine(packageRoot, "out"));

        var refactoredSource = baselineSource.Replace(
            "return Ok(input.trim());",
            "let trimmed: Text = input.trim();\n    return Ok(trimmed);",
            StringComparison.Ordinal);
        AssertTrue(refactoredSource != baselineSource, "The N2 body edit must change the private Text binding body.");
        await File.WriteAllTextAsync(dependencySourcePath, refactoredSource);
        await AssertStaleLockRejectedAsync("body-edit");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-body-edit-relock", packageRoot, "lock"));
        var afterBodyEdit = await BuildCurrentAsync("body-edit", packageRoot);
        AssertEqual(baseline.ApiJson, afterBodyEdit.ApiJson,
            "A private binding body edit must not change the public API snapshot.");
        AssertEqual(baseline.SemanticProjection, afterBodyEdit.SemanticProjection,
            "Trait, implementation, bound, and witness identities must remain stable across body edits.");
        AssertTrue(baseline.AuditJson != afterBodyEdit.AuditJson,
            "The audit snapshot must record the changed dependency source bytes.");
        AssertTrue(baseline.DependencyInputHash != afterBodyEdit.DependencyInputHash,
            "The receipt dependency input hash must change after the binding body edit.");
        AssertEqual(baseline.RootInputHash, afterBodyEdit.RootInputHash,
            "A dependency edit must not change the root source input hash.");
        AssertTrue(baseline.AuditHash != afterBodyEdit.AuditHash,
            "A dependency body edit must change the receipt audit snapshot hash.");

        var commentSource = refactoredSource + "\n// comment-only source identity edit\n";
        await File.WriteAllTextAsync(dependencySourcePath, commentSource);
        await AssertStaleLockRejectedAsync("comment-edit");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-comment-edit-relock", packageRoot, "lock"));
        var afterCommentEdit = await BuildCurrentAsync("comment-edit", packageRoot);
        AssertEqual(baseline.ApiJson, afterCommentEdit.ApiJson,
            "A comment-only dependency edit must not change the public API snapshot.");
        AssertEqual(baseline.SemanticProjection, afterCommentEdit.SemanticProjection,
            "Portable trait and implementation identities must ignore comments and formatting.");
        AssertTrue(afterBodyEdit.DependencyInputHash != afterCommentEdit.DependencyInputHash,
            "A comment-only edit must refresh the dependency input hash.");
        AssertTrue(afterBodyEdit.AuditHash != afterCommentEdit.AuditHash,
            "A comment-only edit must change the receipt audit snapshot hash.");

        var invalidRootSource = consumerSource.Replace(
            "return 0;",
            "let wrong: i32 = true;\n                    return wrong;",
            StringComparison.Ordinal);
        AssertTrue(invalidRootSource != consumerSource, "The test must introduce a root-package type error.");
        await File.WriteAllTextAsync(rootSourcePath, invalidRootSource);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-root-type-error-relock", packageRoot, "lock"));
        var typeError = await harness.InvokePackageDirectoryAsync(
            "maintained-generic-root-type-error-check", packageRoot, "check", "--json");
        AssertTrue(typeError.ExitCode != 0, $"The invalid root source unexpectedly checked. {Describe(typeError)}");
        using (var typeErrorDocument = JsonDocument.Parse(typeError.StandardOutput))
        {
            var diagnosticsEnvelope = typeErrorDocument.RootElement;
            AssertJsonPropertyOrder(diagnosticsEnvelope, "schemaVersion,diagnostics");
            AssertEqual(1, diagnosticsEnvelope.GetProperty("schemaVersion").GetInt32(),
                "The refreshed root source failure must use diagnostics schema 1.");
            var diagnostics = diagnosticsEnvelope.GetProperty("diagnostics");
            AssertEqual(1, diagnostics.GetArrayLength(),
                "The refreshed root source failure must contain exactly one diagnostic and no partial report.");
            AssertEqual("E_TYPE_MISMATCH", diagnostics[0].GetProperty("code").GetString(),
                "The refreshed root source failure must contain only the expected type mismatch.");
        }
        var mismatch = ParseDiagnosticSnapshots(typeError.StandardOutput)
            .Single(diagnostic => diagnostic.Code == "E_TYPE_MISMATCH");
        AssertEqual(Path.GetFullPath(rootSourcePath), Path.GetFullPath(mismatch.File),
            "The refreshed source diagnostic must point at the current root file.");
        AssertRangeAtToken(invalidRootSource, mismatch, "true", 1);
        var typeErrorOutput = Path.Combine(packageRoot, "out");
        var typeErrorBeforeBuild = await CaptureMaintainedOutputSnapshotAsync(typeErrorOutput);
        var typeErrorBuild = await harness.InvokePackageDirectoryAsync(
            "maintained-generic-root-type-error-build", packageRoot, "build");
        AssertTrue(typeErrorBuild.ExitCode != 0, Describe(typeErrorBuild));
        AssertTrue((typeErrorBuild.StandardOutput + typeErrorBuild.StandardError)
                .Contains("E_TYPE_MISMATCH", StringComparison.Ordinal),
            $"The invalid root source build must report E_TYPE_MISMATCH. {Describe(typeErrorBuild)}");
        AssertTrue(!typeErrorBuild.StandardOutput.Contains("Built executable: ", StringComparison.Ordinal),
            "A rejected root type error build must not print a success artifact prefix.");
        var typeErrorAfterBuild = await CaptureMaintainedOutputSnapshotAsync(typeErrorOutput);
        AssertTrue(typeErrorBeforeBuild.SequenceEqual(typeErrorAfterBuild),
            "A rejected root type error build must not add or modify artifacts, receipts, or schemas.");

        var repairedRootSource = invalidRootSource.Replace(
            "let wrong: i32 = true;\n                    return wrong;",
            "let repaired: i32 = 0;\n                    return repaired;",
            StringComparison.Ordinal);
        AssertTrue(repairedRootSource != invalidRootSource, "The root type error repair must edit the source.");
        await File.WriteAllTextAsync(rootSourcePath, repairedRootSource);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-root-type-repair-relock", packageRoot, "lock"));
        var afterRootRepair = await BuildCurrentAsync("root-repair", packageRoot);
        AssertEqual(baseline.SemanticProjection, afterRootRepair.SemanticProjection,
            "A root caller edit must preserve the dependency trait identities.");
        AssertTrue(baseline.RootInputHash != afterRootRepair.RootInputHash,
            "The repaired root source must have a new receipt input hash.");
        AssertTrue(baseline.AuditHash != afterRootRepair.AuditHash,
            "The repaired root source must have a new receipt audit snapshot hash.");
        AssertTrue(baselineOutputSnapshot.Count > 0,
            "The baseline build must leave outputs for stale-build immutability checks.");

        var relocatedWorkspace = Path.Combine(
            harness.TemporaryRoot, "maintained-generic-relocated-" + Guid.NewGuid().ToString("N"));
        var relocatedRoot = Path.Combine(relocatedWorkspace, "root");
        var relocatedDependency = Path.Combine(relocatedWorkspace, "validation");
        Directory.CreateDirectory(relocatedWorkspace);
        Directory.CreateDirectory(relocatedRoot);
        Directory.CreateDirectory(relocatedDependency);
        CopyMaintainedPackageInputs(packageRoot, relocatedRoot);
        CopyMaintainedPackageInputs(dependencyRoot, relocatedDependency);
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "maintained-generic-relocated-lock", relocatedRoot, "lock"));
        var relocated = await BuildCurrentAsync("relocated", relocatedRoot);
        AssertEqual(afterRootRepair.ApiJson, relocated.ApiJson,
            "The relocated generic package graph must emit byte-identical API JSON.");
        AssertEqual(afterRootRepair.AuditJson, relocated.AuditJson,
            "The relocated generic package graph must emit byte-identical audit JSON.");
        AssertEqual(afterRootRepair.SemanticProjection, relocated.SemanticProjection,
            "Stable declaration IDs and witnesses must not include checkout paths.");
        AssertEqual(afterRootRepair.RootInputHash, relocated.RootInputHash,
            "Relocation must preserve the root source receipt hash.");
        AssertEqual(afterRootRepair.DependencyInputHash, relocated.DependencyInputHash,
            "Relocation must preserve the dependency source receipt hash.");
        AssertEqual(afterRootRepair.AuditHash, relocated.AuditHash,
            "Relocation must preserve the checked audit snapshot hash.");
        AssertEqual(afterRootRepair.ReceiptProjection, relocated.ReceiptProjection,
            "Equivalent relocated sources must produce the same portable receipt facts.");
    }

    private static async Task TestQualifiedHttpDependencyAuthority(Harness harness)
    {
        const string dependencyRelativePath = "src/network/client.hob";
        const string rootMainRelativePath = "src/app/main.hob";
        const string rootHandlerRelativePath = "src/handlers.hob";
        const string rootSource = """
            module app::main;

            command fetch {
                help "Fetch one remote target.";
                argument target: Text help "Relative request target.";
                handler: self::handlers::run;
                error: self::handlers::describe;
            }
            """;
        const string handlerSource = """
            module handlers;

            pub async fn run(args: self::app::main::FetchArgs, client: HttpClient) -> Result<Text, HttpError> effects { net.client } {
                return match await remote::network::client::fetch(client, args.target) {
                    Ok(response) => Ok(response.body),
                    Err(error) => Err(error)
                };
            }

            pub fn describe(error: HttpError) -> Text effects {} {
                return match error {
                    HttpError.InvalidTarget => "invalid target",
                    HttpError.Transport => "transport",
                    HttpError.Timeout => "timeout",
                    HttpError.ResponseTooLarge => "response too large",
                    HttpError.InvalidText => "invalid text"
                };
            }
            """;
        const string dependencySource = """
            module network::client;

            pub async fn fetch(client: HttpClient, target: Text) -> Result<HttpResponse, HttpError> effects { net.client } {
                return await client.get_text_async(target);
            }
            """;
        var sourceFiles = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [rootMainRelativePath] = rootSource,
            [rootHandlerRelativePath] = handlerSource
        };
        var baseManifest = CliPackageManifest()
            .Replace("name = \"harness-package\"", "name = \"qualified-http-root\"", StringComparison.Ordinal)
            + "\n[dependencies]\nremote = \"../remote\"\n";
        var packageRoot = await harness.WritePackageGraphAsync(
            "qualified-http-dependency-authority",
            new Dictionary<string, PackageFixture>(StringComparer.Ordinal)
            {
                ["root"] = new PackageFixture(baseManifest, sourceFiles),
                ["remote"] = new PackageFixture(LibraryPackageManifest("remote-http-client"),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        [dependencyRelativePath] = dependencySource
                    })
            });
        var rootMainPath = Path.Combine(packageRoot, rootMainRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var dependencyRoot = Path.GetFullPath(Path.Combine(packageRoot, "..", "remote"));
        var dependencyManifest = await File.ReadAllTextAsync(Path.Combine(dependencyRoot, "hob.toml"));
        AssertTrue(!dependencyManifest.Contains("net.client", StringComparison.Ordinal),
            "The HTTP dependency must declare its effect without receiving a capability grant.");

        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "qualified-http-missing-grant-lock", packageRoot, "lock"));

        var missingGrant = await harness.InvokePackageDirectoryAsync(
            "qualified-http-missing-grant-check", packageRoot, "check", "--json");
        AssertRootHttpGrantDiagnostic(missingGrant, rootSource, rootMainPath);

        var missingApi = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertRootHttpGrantDiagnostic(missingApi, rootSource, rootMainPath);
        var missingAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertRootHttpGrantDiagnostic(missingAudit, rootSource, rootMainPath);
        var missingEffects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertRootHttpGrantDiagnostic(missingEffects, rootSource, rootMainPath);

        var missingBuild = await harness.InvokePackageDirectoryAsync(
            "qualified-http-missing-grant-build", packageRoot, "build");
        AssertRootHttpGrantBuildDiagnostic(missingBuild);
        var outputRoot = Path.Combine(packageRoot, "out");
        AssertTrue(!Directory.Exists(outputRoot)
            || !Directory.EnumerateFileSystemEntries(outputRoot, "*", SearchOption.AllDirectories).Any(),
            "A rejected root capability must not leave any build output.");

        const string origin = "https://api.example.test";
        var grantedManifest = CliPackageManifest()
            .Replace("name = \"harness-package\"", "name = \"qualified-http-root\"", StringComparison.Ordinal)
            + $"http_origin = \"{origin}\"\n"
            + "\n[capabilities]\nnet.client = \"allow\"\n"
            + "\n[dependencies]\nremote = \"../remote\"\n";
        await File.WriteAllTextAsync(Path.Combine(packageRoot, "hob.toml"), grantedManifest);
        var staleAfterGrant = await harness.InvokePackageDirectoryAsync(
            "qualified-http-grant-stale-lock", packageRoot, "check", "--json");
        AssertGenericLockDiagnostic(staleAfterGrant, "HTTP root grant and origin manifest edit");
        AssertLockCommandSucceeded(await harness.InvokePackageDirectoryAsync(
            "qualified-http-grant-relock", packageRoot, "lock"));

        var check = await harness.InvokePackageDirectoryAsync(
            "qualified-http-grant-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        var apiRun = await harness.InvokeCompilerCommandAsync("inspect", "api", packageRoot, "--json");
        AssertEqual(0, apiRun.ExitCode, Describe(apiRun));
        AssertTrue(apiRun.StandardOutput.Contains(origin, StringComparison.Ordinal), Describe(apiRun));
        using var apiDocument = JsonDocument.Parse(apiRun.StandardOutput);
        var api = apiDocument.RootElement;
        AssertInspectApiPropertyOrder(api);
        AssertEqual(13, api.GetProperty("schema_version").GetInt32(),
            "The current inspect API schema is 13.");
        AssertJsonStringArray(api.GetProperty("manifest_grants"), ["net.client"]);
        AssertEqual(origin, api.GetProperty("http_origin").GetString(),
            "The root manifest should preserve its HTTP origin.");
        var command = api.GetProperty("commands").EnumerateArray().Single();
        AssertJsonStringArray(command.GetProperty("required_capabilities"), ["net.client"]);
        var run = api.GetProperty("functions").EnumerateArray()
            .Single(function => function.GetProperty("id").GetString() == "self::handlers::run");
        AssertJsonStringArray(run.GetProperty("required_capabilities"), ["net.client"]);
        var directCall = run.GetProperty("calls").EnumerateArray().Single(call =>
            call.GetProperty("module").GetString() == "network::client"
            && call.GetProperty("name").GetString() == "fetch");
        AssertTrue(directCall.GetProperty("source_ids").EnumerateArray()
                .Any(sourceId => sourceId.GetString() == "remote::network::client::fetch"),
            "The API must preserve the qualified dependency alias on the source-level call.");

        var apiPath = run.GetProperty("effect_paths").EnumerateArray()
            .Single(path => path.GetProperty("effect").GetString() == "net.client");
        var apiSteps = apiPath.GetProperty("steps").EnumerateArray().ToArray();
        AssertTrue(apiSteps.Length >= 3, "The API effect path must include handler, dependency function, and operation.");
        AssertEqual("function", apiSteps[0].GetProperty("kind").GetString(), "The effect path must start at the root handler.");
        AssertEqual("handlers", apiSteps[0].GetProperty("module").GetString(), "The root effect path module must be handlers.");
        AssertEqual("run", apiSteps[0].GetProperty("name").GetString(), "The effect path must start at handlers::run.");
        var dependencyStep = apiSteps.Single(step => step.GetProperty("kind").GetString() == "function"
            && step.GetProperty("module").GetString() == "network::client"
            && step.GetProperty("name").GetString() == "fetch");
        AssertTrue(dependencyStep.GetProperty("source_ids").EnumerateArray()
                .Any(sourceId => sourceId.GetString() == "remote::network::client::fetch"),
            "The qualified dependency function must appear in the effect path.");
        AssertEqual("operation", apiSteps[^1].GetProperty("kind").GetString(),
            "The HTTP effect path must end at the trusted operation.");
        AssertEqual("HttpClient.get_text_async", apiSteps[^1].GetProperty("name").GetString(),
            "The HTTP effect path must reach HttpClient.get_text_async.");

        var effectsRun = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertEqual(0, effectsRun.ExitCode, Describe(effectsRun));
        using (var effectsDocument = JsonDocument.Parse(effectsRun.StandardOutput))
        {
            var effectPath = effectsDocument.RootElement.GetProperty("effect_paths").EnumerateArray()
                .Single(path => path.GetProperty("effect").GetString() == "net.client");
            var steps = effectPath.GetProperty("steps").EnumerateArray()
                .Select(step => step.GetString() ?? string.Empty).ToArray();
            AssertEqual(3, steps.Length,
                $"inspect effects must retain the complete root -> dependency -> HTTP chain: [{string.Join(" -> ", steps)}].");
            AssertEqual("handlers::run", steps[0], "inspect effects must begin at the root command handler.");
            AssertEqual("remote-http-client@0.1.0::network::client::fetch", steps[1],
                "inspect effects must cross the named HTTP dependency before reaching its adapter.");
            AssertEqual("HttpClient.get_text_async", steps[2],
                "inspect effects must terminate at the trusted HTTP adapter operation.");
        }

        var repeatedEffects = await harness.InvokeCompilerCommandAsync(
            "inspect", "effects", packageRoot, "self::handlers::run", "--json");
        AssertEqual(0, repeatedEffects.ExitCode, Describe(repeatedEffects));
        AssertEqual(effectsRun.StandardOutput, repeatedEffects.StandardOutput,
            "Repeated inspect-effects output must be byte-identical.");

        var auditRun = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, auditRun.ExitCode, Describe(auditRun));
        using (var auditDocument = JsonDocument.Parse(auditRun.StandardOutput))
        {
            var audit = auditDocument.RootElement;
            AssertAuditPropertyOrder(audit);
            AssertEqual(11, audit.GetProperty("schema_version").GetInt32(),
                "The current audit schema is 11.");
            AssertJsonStringArray(audit.GetProperty("manifest_grants"), ["net.client"]);
            var httpClaim = audit.GetProperty("trusted_claims").EnumerateArray()
                .Single(claim => claim.GetProperty("operation").GetString() == "HttpClient.get_text_async");
            AssertJsonStringArray(httpClaim.GetProperty("effects"), ["net.client"]);
            AssertTrue(httpClaim.GetProperty("reachable_from").EnumerateArray()
                    .Any(reachable => reachable.GetProperty("module").GetString() == "handlers"
                        && reachable.GetProperty("name").GetString() == "run"),
                "Audit must connect the HTTP adapter claim to the root handler.");
        }

        var repeatedAudit = await harness.InvokeCompilerCommandAsync("audit", packageRoot, "--json");
        AssertEqual(0, repeatedAudit.ExitCode, Describe(repeatedAudit));
        AssertEqual(auditRun.StandardOutput, repeatedAudit.StandardOutput,
            "Repeated audit output must be byte-identical.");

        var build = await harness.InvokePackageDirectoryAsync(
            "qualified-http-grant-build", packageRoot, "build");
        AssertEqual(0, build.ExitCode, Describe(build));
        var artifact = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(artifact)!;
        var relativeArtifact = Path.GetRelativePath(outputDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/');
        using var receipt = await AssertBuildReceiptAsync(
            outputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [relativeArtifact, "command-schema.json"],
            packageRoot,
            harness.TemporaryRoot);
        AssertJsonStringArray(receipt.RootElement.GetProperty("manifest_grants"), ["net.client"]);
        using var schema = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(outputDirectory, "command-schema.json")));
        var schemaCommand = schema.RootElement.GetProperty("commands").EnumerateArray().Single();
        AssertJsonStringArray(schemaCommand.GetProperty("capabilities"), ["net.client"]);
    }

    private static async Task TestSecretCanaryExitMatrix(Harness harness)
    {
        const string secretCanary = "maintained-generic-secret-canary-73c0f08e";
        const string manifest = """
            name = "maintained-generic-secret-cli"
            version = "0.1.0"
            kind = "cli"
            source_root = "src"
            entry_module = "app::main"

            [config]
            token = "Secret<Text>|required"

            [capabilities]
            env.read = "allow"
            """;
        const string source = """
            module app::main;

            pub union ProbeError { Invalid }

            command probe {
                help "Exercise safe exit paths.";
                argument mode: Text help "domain, fault, or ok.";
                argument seed: i32 help "Overflow seed.";
                handler: self::app::main::run;
                error: self::app::main::describe;
            }

            pub fn run(
                args: self::app::main::ProbeArgs,
                config: Config
            ) -> Result<Text, self::app::main::ProbeError> effects { env.read } {
                let token: Secret<Text> = config.get_secret_text("token");
                if args.mode == "domain" {
                    return Err(self::app::main::ProbeError.Invalid);
                }
                if args.mode == "fault" {
                    let overflow: i32 = args.seed + 1;
                    return Ok("unreachable");
                }
                return Ok("safe");
            }

            pub fn describe(error: self::app::main::ProbeError) -> Text effects {} {
                return match error {
                    self::app::main::ProbeError.Invalid => "invalid input"
                };
            }
            """;
        var secretReadIndex = source.IndexOf("config.get_secret_text(\"token\")", StringComparison.Ordinal);
        var domainBranchIndex = source.IndexOf("args.mode == \"domain\"", StringComparison.Ordinal);
        var faultBranchIndex = source.IndexOf("args.mode == \"fault\"", StringComparison.Ordinal);
        AssertTrue(secretReadIndex >= 0 && domainBranchIndex > secretReadIndex && faultBranchIndex > domainBranchIndex,
            "The handler must load Secret<Text> before the domain and runtime-fault branches.");
        AssertTrue(!source.Contains("Secrets", StringComparison.Ordinal)
            && !source.Contains("secret.reveal", StringComparison.Ordinal)
            && !source.Contains("reveal_text", StringComparison.Ordinal),
            "The canary fixture must not inject or reveal Secrets.");

        var packageRoot = await harness.WritePackageAsync(
            "maintained-generic-secret-canary",
            manifest,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["src/app/main.hob"] = source
            });
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HOB_CONFIG_TOKEN"] = secretCanary
        };
        var check = await harness.InvokePackageDirectoryAsync(
            "maintained-generic-secret-check", packageRoot, "check", "--json");
        AssertEqual(0, check.ExitCode, Describe(check));
        AssertEqual(0, ParseDiagnosticSnapshots(check.StandardOutput).Length, Describe(check));

        void AssertNoCanary(ProcessResult result, string context)
        {
            AssertTrue(!result.StandardOutput.Contains(secretCanary, StringComparison.Ordinal)
                && !result.StandardError.Contains(secretCanary, StringComparison.Ordinal),
                $"The config canary must not appear in {context}. {Describe(result)}");
        }

        void AssertSafeCliOutput(ProcessResult result, string context)
        {
            AssertNoCanary(result, context);
            foreach (var stream in new[] { result.StandardOutput, result.StandardError })
            {
                AssertTrue(!stream.Contains(harness.TemporaryRoot, StringComparison.OrdinalIgnoreCase)
                    && !stream.Contains(harness.RepositoryRoot, StringComparison.OrdinalIgnoreCase),
                    $"The {context} must not expose temporary or repository paths. {Describe(result)}");
                AssertTrue(!stream.Contains("Exception", StringComparison.Ordinal)
                    && !stream.Contains(" at ", StringComparison.Ordinal),
                    $"The {context} must not expose an exception or stack trace. {Describe(result)}");
            }
        }

        var topHelp = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-top-help", packageRoot, "run", environment, "--", "--help");
        AssertEqual(0, topHelp.ExitCode, Describe(topHelp));
        AssertEqual(string.Empty, topHelp.StandardError, Describe(topHelp));
        AssertTrue(!string.IsNullOrWhiteSpace(topHelp.StandardOutput)
            && topHelp.StandardOutput.Contains("probe", StringComparison.Ordinal)
            && topHelp.StandardOutput.Contains("Exercise safe exit paths.", StringComparison.Ordinal),
            $"Top-level help must contain the documented command usage. {Describe(topHelp)}");
        AssertSafeCliOutput(topHelp, "top-level help");
        var commandHelp = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-command-help", packageRoot, "run", environment, "--", "probe", "--help");
        AssertEqual(0, commandHelp.ExitCode, Describe(commandHelp));
        AssertEqual(string.Empty, commandHelp.StandardError, Describe(commandHelp));
        AssertTrue(!string.IsNullOrWhiteSpace(commandHelp.StandardOutput)
            && commandHelp.StandardOutput.Contains("mode", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("seed", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("domain, fault, or ok.", StringComparison.Ordinal)
            && commandHelp.StandardOutput.Contains("Overflow seed.", StringComparison.Ordinal),
            $"Command help must contain its documented arguments. {Describe(commandHelp)}");
        AssertSafeCliOutput(commandHelp, "command help");

        var invalidOption = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-invalid-option", packageRoot, "run", environment,
            "--", "probe", "domain", "0", "--unknown");
        AssertEqual(2, invalidOption.ExitCode, Describe(invalidOption));
        AssertEqual(string.Empty, invalidOption.StandardOutput, Describe(invalidOption));
        AssertEqual("CLI_UNKNOWN_OPTION: \"--unknown\"" + Environment.NewLine,
            invalidOption.StandardError, Describe(invalidOption));
        AssertSafeCliOutput(invalidOption, "invalid-option output");

        var domainError = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-domain-error", packageRoot, "run", environment,
            "--", "probe", "domain", "0");
        AssertEqual(3, domainError.ExitCode, Describe(domainError));
        AssertEqual(string.Empty, domainError.StandardOutput, Describe(domainError));
        AssertEqual("invalid input" + Environment.NewLine, domainError.StandardError, Describe(domainError));
        AssertSafeCliOutput(domainError, "typed domain-error output");

        var runtimeFault = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-runtime-fault", packageRoot, "run", environment,
            "--", "probe", "fault", "2147483647");
        AssertEqual(70, runtimeFault.ExitCode, Describe(runtimeFault));
        AssertEqual(string.Empty, runtimeFault.StandardOutput, Describe(runtimeFault));
        AssertEqual("Runtime fault" + Environment.NewLine, runtimeFault.StandardError, Describe(runtimeFault));
        AssertSafeCliOutput(runtimeFault, "runtime-fault output");

        var api = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            environment, "inspect", "api", packageRoot, "--json");
        AssertEqual(0, api.ExitCode, Describe(api));
        AssertNoCanary(api, "inspect api output");
        using (var apiDocument = JsonDocument.Parse(api.StandardOutput))
        {
            AssertInspectApiPropertyOrder(apiDocument.RootElement);
            AssertEqual(13, apiDocument.RootElement.GetProperty("schema_version").GetInt32(),
                "The canary package uses inspect API schema 13.");
            AssertTrue(apiDocument.RootElement.GetProperty("config").EnumerateArray()
                    .Any(field => field.GetProperty("name").GetString() == "token"
                        && field.GetProperty("source_type").GetString() == "Secret<Text>"),
                "The current API should describe the secret configuration field without its value.");
        }

        var audit = await harness.InvokeCompilerCommandWithEnvironmentAsync(
            environment, "audit", packageRoot, "--json");
        AssertEqual(0, audit.ExitCode, Describe(audit));
        AssertNoCanary(audit, "audit output");
        using (var auditDocument = JsonDocument.Parse(audit.StandardOutput))
        {
            AssertAuditPropertyOrder(auditDocument.RootElement);
            AssertEqual(11, auditDocument.RootElement.GetProperty("schema_version").GetInt32(),
                "The canary package uses audit schema 11.");
        }

        var build = await harness.InvokePackageDirectoryWithEnvironmentAsync(
            "maintained-generic-secret-build", packageRoot, "build", environment);
        AssertEqual(0, build.ExitCode, Describe(build));
        AssertNoCanary(build, "build output");
        var artifact = ParseBuiltArtifact(build, "Built executable: ");
        var outputDirectory = Path.GetDirectoryName(artifact)!;
        var relativeArtifact = Path.GetRelativePath(outputDirectory, artifact).Replace(Path.DirectorySeparatorChar, '/');
        using var receipt = await AssertBuildReceiptAsync(
            outputDirectory,
            "managed",
            expectedRuntimeIdentifier: null,
            [relativeArtifact, "command-schema.json"],
            packageRoot,
            harness.TemporaryRoot);
        AssertEqual(3, receipt.RootElement.GetProperty("schema_version").GetInt32(),
            "The canary package uses build receipt schema 3.");
        var schemaPath = Path.Combine(outputDirectory, "command-schema.json");
        var receiptPath = Path.Combine(outputDirectory, "build-receipt.json");
        AssertTrue(!(await File.ReadAllTextAsync(schemaPath)).Contains(secretCanary, StringComparison.Ordinal),
            "The command schema must not include environment configuration values.");
        AssertTrue(!(await File.ReadAllTextAsync(receiptPath)).Contains(secretCanary, StringComparison.Ordinal),
            "The build receipt must not include environment configuration values.");
        await AssertNoTextInFilesAsync(Path.Combine(packageRoot, "out"), secretCanary);
    }

    private static void AssertRootHttpGrantDiagnostic(ProcessResult result, string rootSource, string rootMainPath)
    {
        AssertTrue(result.ExitCode != 0, $"A missing root net.client grant unexpectedly succeeded. {Describe(result)}");
        var diagnostics = ParseDiagnosticSnapshots(result.StandardOutput);
        AssertEqual(1, diagnostics.Length, Describe(result));
        AssertEqual("E_CAPABILITY_MISSING", diagnostics[0].Code, Describe(result));
        AssertEqual("Command handler requires the root package's net.client capability grant",
            diagnostics[0].Message, Describe(result));
        AssertEqual(Path.GetFullPath(rootMainPath), Path.GetFullPath(diagnostics[0].File),
            "The capability diagnostic must point at the current root command source.");
        AssertRangeAtToken(rootSource, diagnostics[0], "self", 1);
    }

    private static void AssertRootHttpGrantBuildDiagnostic(ProcessResult result)
    {
        var output = result.StandardOutput + result.StandardError;
        AssertTrue(result.ExitCode != 0, $"A missing root net.client grant unexpectedly built. {Describe(result)}");
        AssertTrue(output.Contains("E_CAPABILITY_MISSING", StringComparison.Ordinal)
            && output.Contains("Command handler requires the root package's net.client capability grant", StringComparison.Ordinal),
            $"A missing root net.client grant must reject the build with the stable diagnostic. {Describe(result)}");
    }

    private static void AssertGenericLockDiagnostic(ProcessResult result, string context)
    {
        AssertTrue(result.ExitCode != 0, $"The {context} unexpectedly succeeded. {Describe(result)}");
        if (result.StandardOutput.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            AssertEqual(string.Empty, result.StandardError, Describe(result));
            using var document = JsonDocument.Parse(result.StandardOutput);
            var root = document.RootElement;
            AssertJsonPropertyOrder(root, "schemaVersion,diagnostics");
            AssertEqual(1, root.GetProperty("schemaVersion").GetInt32(),
                $"The {context} must use diagnostics schema 1.");
            var diagnostics = root.GetProperty("diagnostics");
            AssertEqual(1, diagnostics.GetArrayLength(),
                $"The {context} must return exactly one diagnostic and no partial report. {Describe(result)}");
            AssertEqual("E_LOCK", diagnostics[0].GetProperty("code").GetString(),
                $"The {context} must fail only with E_LOCK.");
            return;
        }

        var output = result.StandardOutput + result.StandardError;
        AssertTrue(output.Contains("E_LOCK", StringComparison.Ordinal),
            $"The {context} must fail with E_LOCK. {Describe(result)}");
        AssertTrue(!result.StandardOutput.Contains("Built executable: ", StringComparison.Ordinal),
            $"The {context} must not print a success artifact prefix. {Describe(result)}");
    }

    private static async Task<SortedDictionary<string, string>> CaptureMaintainedOutputSnapshotAsync(string outputRoot)
    {
        var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (!Directory.Exists(outputRoot))
            return snapshot;

        foreach (var file in Directory.EnumerateFiles(outputRoot, "*", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(outputRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            snapshot.Add(relativePath, HashSha256(await File.ReadAllBytesAsync(file)));
        }

        return snapshot;
    }

    private static string MaintainedReceiptInputHash(JsonElement receipt, string? packageName, string relativePath)
    {
        var input = receipt.GetProperty("inputs").EnumerateArray().Single(candidate =>
        {
            if (!string.Equals(candidate.GetProperty("path").GetString(), relativePath, StringComparison.Ordinal))
                return false;

            var package = candidate.GetProperty("package");
            if (packageName is null)
                return package.ValueKind == JsonValueKind.Object
                    && package.GetProperty("path").GetString() == "."
                    && package.GetProperty("source").GetProperty("kind").GetString() == "root";

            return package.ValueKind == JsonValueKind.Object
                && string.Equals(package.GetProperty("name").GetString(), packageName, StringComparison.Ordinal);
        });
        return input.GetProperty("sha256").GetString() ?? string.Empty;
    }

    private static string MaintainedTraitIdentityProjection(string apiJson, string auditJson)
    {
        using var apiDocument = JsonDocument.Parse(apiJson);
        using var auditDocument = JsonDocument.Parse(auditJson);
        var facts = new List<string>();
        var api = apiDocument.RootElement;
        foreach (var section in new[] { "structs", "unions", "traits", "trait_impls" })
            facts.Add($"api.{section}:{api.GetProperty(section).GetRawText()}");
        foreach (var function in api.GetProperty("functions").EnumerateArray())
        {
            facts.Add(string.Join("|",
                "api.function",
                function.GetProperty("id").GetString(),
                function.GetProperty("type_parameters").GetRawText(),
                function.GetProperty("calls").GetRawText(),
                function.GetProperty("trait_calls").GetRawText()));
        }

        var compiler = auditDocument.RootElement.GetProperty("compiler");
        facts.Add($"audit.traits:{compiler.GetProperty("traits").GetRawText()}");
        facts.Add($"audit.trait_impls:{compiler.GetProperty("trait_impls").GetRawText()}");
        foreach (var function in compiler.GetProperty("functions").EnumerateArray())
        {
            facts.Add(string.Join("|",
                "audit.function",
                function.GetProperty("package").GetProperty("name").GetString(),
                function.GetProperty("module").GetString(),
                function.GetProperty("name").GetString(),
                function.GetProperty("type_parameters").GetRawText(),
                function.GetProperty("direct_calls").GetRawText(),
                function.GetProperty("trait_calls").GetRawText()));
        }

        return string.Join("\n", facts);
    }

    private static string MaintainedReceiptProjection(JsonElement receipt)
    {
        var artifactPaths = receipt.GetProperty("artifacts").EnumerateArray()
            .Select(artifact => artifact.GetProperty("path").GetString() ?? string.Empty)
            .ToArray();
        return JsonSerializer.Serialize(new
        {
            package_graph = receipt.GetProperty("package_graph").GetRawText(),
            inputs = receipt.GetProperty("inputs").GetRawText(),
            manifest_grants = receipt.GetProperty("manifest_grants").GetRawText(),
            audit_snapshot_sha256 = receipt.GetProperty("audit_snapshot_sha256").GetString(),
            artifacts = artifactPaths
        });
    }
}
