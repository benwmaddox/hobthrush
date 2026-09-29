using System.Text.Json;

internal static partial class IntegrationTests
{
    private static bool IsLowerSha256Value(string? value) =>
        value is { Length: 64 } &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static void AssertManagedAdapterProvenanceOrder(JsonElement adapters)
    {
        AssertEqual(JsonValueKind.Array, adapters.ValueKind,
            "Managed adapter provenance must always be serialized as an array.");
        foreach (var adapter in adapters.EnumerateArray())
        {
            AssertJsonPropertyOrder(
                adapter,
                "package,bridge_id,contract_version,target_framework,portability_target,operations,assemblies,closure_sha256,assurance");
            AssertPackageIdentitySource(adapter.GetProperty("package"));
            foreach (var operation in adapter.GetProperty("operations").EnumerateArray())
            {
                AssertJsonPropertyOrder(
                    operation,
                    "operation_id,name,signature,is_async,effects,required_capabilities");
                var signature = operation.GetProperty("signature");
                AssertJsonPropertyOrder(signature, "parameters,result");
                foreach (var parameter in signature.GetProperty("parameters").EnumerateArray())
                    AssertJsonPropertyOrder(parameter, "name,type");
            }
            foreach (var assembly in adapter.GetProperty("assemblies").EnumerateArray())
            {
                AssertJsonPropertyOrder(assembly, "identity,path,sha256");
                var path = assembly.GetProperty("path").GetString() ?? string.Empty;
                AssertTrue(!Path.IsPathFullyQualified(path) && !path.Contains('\\')
                    && !path.Split('/').Any(segment => segment is "" or "." or ".."),
                    $"Managed adapter assembly paths must be normalized package-relative paths: {path}.");
                AssertTrue(IsLowerSha256Value(assembly.GetProperty("sha256").GetString()),
                    "Managed adapter assembly provenance must use lowercase SHA-256 values.");
            }
            AssertTrue(IsLowerSha256Value(adapter.GetProperty("closure_sha256").GetString()),
                "Managed adapter provenance must use a lowercase closure SHA-256.");
            AssertEqual("claim_only", adapter.GetProperty("assurance").GetString(),
                "Managed adapter assurance must remain claim_only.");
        }
    }

    private static void AssertSha256AdapterProvenance(
        JsonElement adapters,
        string expectedPackagePath,
        string expectedAssemblyHash)
    {
        AssertManagedAdapterProvenanceOrder(adapters);
        var records = adapters.EnumerateArray().ToArray();
        AssertEqual(1, records.Length, "The package graph should report one SHA-256 text adapter.");
        var adapter = records[0];
        var package = adapter.GetProperty("package");
        AssertEqual("sha256-adapter", package.GetProperty("name").GetString(),
            "Unexpected managed adapter package identity.");
        AssertEqual("0.1.0", package.GetProperty("version").GetString(),
            "Unexpected managed adapter package version.");
        AssertEqual(expectedPackagePath, package.GetProperty("path").GetString(),
            "Managed adapter provenance should use its stable relative package path.");
        AssertEqual("path", package.GetProperty("source").GetProperty("kind").GetString(),
            "Managed adapter provenance should retain its path source identity.");
        AssertEqual("../adapter", package.GetProperty("source").GetProperty("path").GetString(),
            "Managed adapter source identity should remain relative.");
        AssertEqual("lang.sha256-text.v1", adapter.GetProperty("bridge_id").GetString(),
            "Unexpected managed adapter bridge.");
        AssertEqual(1, adapter.GetProperty("contract_version").GetInt32(),
            "Unexpected managed adapter contract version.");
        AssertEqual("net10.0", adapter.GetProperty("target_framework").GetString(),
            "Managed adapter provenance should retain the target framework.");
        AssertEqual("portable-anycpu-il", adapter.GetProperty("portability_target").GetString(),
            "Managed adapter provenance should retain the selected portability target.");

        var operations = adapter.GetProperty("operations").EnumerateArray().ToArray();
        AssertEqual(1, operations.Length, "The SHA-256 bridge should report one closed operation.");
        var operation = operations[0];
        AssertEqual("sha256.text.hash_utf8", operation.GetProperty("operation_id").GetString(),
            "Unexpected managed adapter operation ID.");
        AssertEqual("sha::hash_utf8", operation.GetProperty("name").GetString(),
            "Provenance should retain the declared adapter function name.");
        AssertEqual(false, operation.GetProperty("is_async").GetBoolean(),
            "The SHA-256 adapter operation is synchronous.");
        AssertJsonStringArray(operation.GetProperty("effects"), []);
        AssertJsonStringArray(operation.GetProperty("required_capabilities"), []);
        var signature = operation.GetProperty("signature");
        AssertEqual("Text", signature.GetProperty("result").GetString(),
            "The SHA-256 operation must return Text.");
        var parameters = signature.GetProperty("parameters").EnumerateArray().ToArray();
        AssertEqual(1, parameters.Length, "The SHA-256 operation must have one parameter.");
        AssertEqual("value", parameters[0].GetProperty("name").GetString(),
            "The SHA-256 signature must retain its declared parameter name.");
        AssertEqual("Text", parameters[0].GetProperty("type").GetString(),
            "The SHA-256 operation must accept Text.");

        var assemblies = adapter.GetProperty("assemblies").EnumerateArray().ToArray();
        AssertEqual(1, assemblies.Length, "The closed SHA-256 package should record one assembly.");
        AssertEqual(
            "Lang.ManagedAdapters, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
            assemblies[0].GetProperty("identity").GetString(),
            "The assembly provenance should record its exact reviewed CLR identity.");
        AssertEqual("lib/Lang.ManagedAdapters.dll", assemblies[0].GetProperty("path").GetString(),
            "The assembly provenance path should be package-relative.");
        AssertEqual(expectedAssemblyHash, assemblies[0].GetProperty("sha256").GetString(),
            "The assembly provenance should bind the fixture's exact bytes.");
    }
}