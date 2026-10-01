using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed record ManagedAdapterOperationDefinition(
    string OperationId,
    IReadOnlyList<string> ParameterTypes,
    string ReturnType,
    bool IsAsync,
    IReadOnlyList<string> Effects,
    IReadOnlyList<string> RequiredCapabilities);

internal sealed record ManagedAdapterDefinition(
    string BridgeId,
    string CatalogRevision,
    string TargetFramework,
    string PortabilityTarget,
    string AssemblyName,
    string AssemblyVersion,
    string AssemblyCulture,
    string AssemblyPublicKeyToken,
    string TypeName,
    string MethodName,
    bool AotSupported,
    IReadOnlyList<ManagedAdapterOperationDefinition> Operations);

internal sealed record ManagedAdapterDescriptor(
    ManagedAdapterDefinition Definition,
    string AssemblyPath,
    string AssemblySha256,
    string ClosureSha256,
    string SourceAssemblyPath)
{
    public string BridgeId => Definition.BridgeId;
    public string TargetFramework => Definition.TargetFramework;
    public string PortabilityTarget => Definition.PortabilityTarget;
    public IReadOnlyList<string> OperationIds => Definition.Operations
        .Select(operation => operation.OperationId)
        .OrderBy(operationId => operationId, StringComparer.Ordinal)
        .ToArray();
}

/// <summary>
/// Closed set of managed adapter contracts accepted by the compiler. Validation reads PE
/// metadata only; package code is never loaded or executed by the compiler.
/// </summary>
internal static class ManagedAdapterCatalog
{
    private const string RequiredTargetFramework = ".NETCoreApp,Version=v10.0";
    private const string ClosureDomain = "HOB-MANAGED-ADAPTER-CLOSURE\0v1";
    private const int MaximumAdapterAssemblyBytes = 64 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private static readonly ManagedAdapterDefinition Sha256Text = new(
        BridgeId: "hob.sha256-text.v1",
        CatalogRevision: "1",
        TargetFramework: "net10.0",
        PortabilityTarget: "portable-anycpu-il",
        AssemblyName: "Hob.ManagedAdapters",
        AssemblyVersion: "1.0.0.0",
        AssemblyCulture: "neutral",
        AssemblyPublicKeyToken: "null",
        TypeName: "Hob.ManagedAdapters.Sha256Text",
        MethodName: "HashUtf8",
        AotSupported: true,
        Operations: Array.AsReadOnly<ManagedAdapterOperationDefinition>(
        [
            new(
                "sha256.text.hash_utf8",
                Array.AsReadOnly(["Text"]),
                "Text",
                IsAsync: false,
                Array.AsReadOnly(Array.Empty<string>()),
                Array.AsReadOnly(Array.Empty<string>()))
        ]));

    private static readonly IReadOnlyDictionary<string, ManagedAdapterDefinition> ByBridgeId =
        new Dictionary<string, ManagedAdapterDefinition>(StringComparer.Ordinal)
        {
            [Sha256Text.BridgeId] = Sha256Text
        };

    public static IReadOnlyList<ManagedAdapterDefinition> Definitions { get; } =
        Array.AsReadOnly([Sha256Text]);

    public static bool TryGetDefinition(string bridgeId, out ManagedAdapterDefinition? definition) =>
        ByBridgeId.TryGetValue(bridgeId, out definition);

    public static ManagedAdapterDescriptor? ValidatePackageDeclaration(
        string packageRoot,
        IReadOnlyDictionary<string, string> values,
        string manifestFile,
        List<Diagnostic> diagnostics)
    {
        if (values.Count == 0)
            return null;

        if (!values.TryGetValue("bridge_id", out var bridgeId) ||
            !values.TryGetValue("target_framework", out var targetFramework) ||
            !values.TryGetValue("assembly_path", out var assemblyPath) ||
            !values.TryGetValue("assembly_sha256", out var expectedSha256))
            return null;

        if (!TryGetDefinition(bridgeId, out var definition) || definition is null)
        {
            diagnostics.Add(ManifestDiagnostic($"Unknown managed adapter bridge_id '{bridgeId}'", manifestFile));
            return null;
        }

        if (!string.Equals(targetFramework, definition.TargetFramework, StringComparison.Ordinal))
        {
            diagnostics.Add(ManifestDiagnostic(
                $"Managed adapter '{bridgeId}' requires target_framework = \"{definition.TargetFramework}\"",
                manifestFile));
            return null;
        }

        if (!IsPortablePackageFilePath(assemblyPath))
        {
            diagnostics.Add(ManifestDiagnostic(
                "managed_adapter.assembly_path must be a portable package-relative file path",
                manifestFile));
            return null;
        }

        if (!IsLowerSha256(expectedSha256))
        {
            diagnostics.Add(ManifestDiagnostic(
                "managed_adapter.assembly_sha256 must be exactly 64 lowercase hexadecimal characters",
                manifestFile));
            return null;
        }

        string sourceAssemblyPath;
        try
        {
            if (!TryResolveRegularPackageFile(packageRoot, assemblyPath, out sourceAssemblyPath))
            {
                diagnostics.Add(ManifestDiagnostic(
                    "managed_adapter.assembly_path must identify a package-local regular file without symbolic links or reparse points",
                    manifestFile));
                return null;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(ManifestDiagnostic(
                "Could not safely resolve managed_adapter.assembly_path",
                manifestFile));
            return null;
        }

        byte[] assemblyBytes;
        try
        {
            assemblyBytes = ReadBoundedAssemblySnapshot(sourceAssemblyPath);
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(ManifestDiagnostic(
                $"Could not read managed adapter assembly: {error.Message}",
                manifestFile));
            return null;
        }

        var actualSha256 = Convert.ToHexString(SHA256.HashData(assemblyBytes)).ToLowerInvariant();
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            diagnostics.Add(ManifestDiagnostic(
                "Managed adapter assembly SHA-256 does not match managed_adapter.assembly_sha256",
                manifestFile));
            return null;
        }

        if (!TryGetFrameworkReferenceIdentities(out var frameworkReferences, out var frameworkError))
        {
            diagnostics.Add(ManifestDiagnostic(frameworkError, manifestFile));
            return null;
        }

        string? metadataError;
        try
        {
            using var snapshot = new MemoryStream(assemblyBytes, writable: false);
            using var peReader = new PEReader(snapshot, PEStreamOptions.LeaveOpen);
            metadataError = ValidateAssemblyMetadata(peReader, definition!, frameworkReferences!);
        }
        catch (Exception error) when (error is BadImageFormatException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            metadataError = $"Managed adapter assembly metadata is invalid: {error.Message}";
        }

        if (metadataError is not null)
        {
            diagnostics.Add(ManifestDiagnostic(metadataError, manifestFile));
            return null;
        }

        var closureSha256 = ComputeClosureSha256(definition!, assemblyPath, actualSha256);
        return new ManagedAdapterDescriptor(definition!, assemblyPath, actualSha256, closureSha256, sourceAssemblyPath);
    }

    public static string ComputeClosureSha256(
        ManagedAdapterDefinition definition,
        string assemblyPath,
        string assemblySha256)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendField(hash, ClosureDomain);
        AppendField(hash, definition.BridgeId);
        AppendField(hash, definition.CatalogRevision);
        AppendField(hash, definition.TargetFramework);
        AppendField(hash, definition.PortabilityTarget);
        AppendField(hash, definition.AotSupported ? "aot" : "no-aot");
        AppendField(hash, definition.AssemblyName);
        AppendField(hash, definition.AssemblyVersion);
        AppendField(hash, definition.AssemblyCulture);
        AppendField(hash, definition.AssemblyPublicKeyToken);
        AppendField(hash, definition.TypeName);
        AppendField(hash, definition.MethodName);

        var operations = definition.Operations.OrderBy(item => item.OperationId, StringComparer.Ordinal).ToArray();
        AppendField(hash, operations.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var operation in operations)
        {
            AppendField(hash, operation.OperationId);
            AppendField(hash, operation.ParameterTypes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var parameterType in operation.ParameterTypes)
                AppendField(hash, parameterType);
            AppendField(hash, operation.ReturnType);
            AppendField(hash, operation.IsAsync ? "async" : "sync");
            var effects = operation.Effects.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            AppendField(hash, effects.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var effect in effects)
                AppendField(hash, $"effect:{effect}");
            var capabilities = operation.RequiredCapabilities.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            AppendField(hash, capabilities.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var capability in capabilities)
                AppendField(hash, $"capability:{capability}");
        }

        AppendField(hash, assemblyPath);
        AppendField(hash, assemblySha256);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static bool IsPortableLockRecord(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.Object ||
            !HasExactProperties(element,
            [
                "bridge_id", "catalog_revision", "operation_ids", "target_framework", "portability_target",
                "assembly_path", "assembly_sha256", "closure_sha256"
            ]))
            return false;

        var bridgeId = element.GetProperty("bridge_id");
        if (!IsString(bridgeId) || !TryGetDefinition(bridgeId.GetString()!, out var definition) || definition is null)
            return false;

        var revision = element.GetProperty("catalog_revision");
        var targetFramework = element.GetProperty("target_framework");
        var portabilityTarget = element.GetProperty("portability_target");
        var assemblyPath = element.GetProperty("assembly_path");
        var assemblyHash = element.GetProperty("assembly_sha256");
        var closureHash = element.GetProperty("closure_sha256");
        var operationIds = element.GetProperty("operation_ids");
        if (!IsString(revision) || revision.GetString() != definition.CatalogRevision ||
            !IsString(targetFramework) || targetFramework.GetString() != definition.TargetFramework ||
            !IsString(portabilityTarget) || portabilityTarget.GetString() != definition.PortabilityTarget ||
            !IsString(assemblyPath) || !IsPortablePackageFilePath(assemblyPath.GetString()!) ||
            !IsLowerHash(assemblyHash) || !IsLowerHash(closureHash) ||
            operationIds.ValueKind != JsonValueKind.Array)
            return false;

        var expectedOperationIds = definition.Operations
            .Select(operation => operation.OperationId)
            .OrderBy(operationId => operationId, StringComparer.Ordinal)
            .ToArray();
        var actualOperationIds = operationIds.EnumerateArray().ToArray();
        if (actualOperationIds.Length != expectedOperationIds.Length)
            return false;
        for (var index = 0; index < actualOperationIds.Length; index++)
        {
            if (!IsString(actualOperationIds[index]) ||
                !string.Equals(actualOperationIds[index].GetString(), expectedOperationIds[index], StringComparison.Ordinal))
                return false;
        }

        var calculatedClosure = ComputeClosureSha256(
            definition,
            assemblyPath.GetString()!,
            assemblyHash.GetString()!);
        return string.Equals(calculatedClosure, closureHash.GetString(), StringComparison.Ordinal);
    }

    private static string? ValidateAssemblyMetadata(
        PEReader peReader,
        ManagedAdapterDefinition definition,
        IReadOnlySet<AssemblyIdentityKey> frameworkReferences)
    {
        if (!peReader.HasMetadata || peReader.PEHeaders.CorHeader is not { } corHeader ||
            peReader.PEHeaders.PEHeader is not { } peHeader)
            return "Managed adapter must be a managed PE assembly";

        var headers = peReader.PEHeaders;
        if (headers.CoffHeader.Machine != Machine.I386 || peHeader.Magic != PEMagic.PE32 ||
            (corHeader.Flags & CorFlags.ILOnly) == 0 ||
            (corHeader.Flags & (CorFlags.Requires32Bit | CorFlags.Prefers32Bit | CorFlags.NativeEntryPoint)) != 0 ||
            corHeader.EntryPointTokenOrRelativeVirtualAddress != 0 ||
            corHeader.ManagedNativeHeaderDirectory.RelativeVirtualAddress != 0)
            return "Managed adapter must be an IL-only AnyCPU library without native code or an entry point";

        if (peHeader.ExportTableDirectory.RelativeVirtualAddress != 0 ||
            peHeader.ExportTableDirectory.Size != 0)
            return "Managed adapter cannot export native entry points";

        var reader = peReader.GetMetadataReader();
        if (!reader.IsAssembly)
            return "Managed adapter must be a single-file assembly, not a netmodule";

        var assembly = reader.GetAssemblyDefinition();
        if (reader.GetString(assembly.Name) != definition.AssemblyName ||
            assembly.Version.ToString() != definition.AssemblyVersion ||
            NormalizeCulture(reader, assembly.Culture) != definition.AssemblyCulture ||
            !assembly.PublicKey.IsNil && reader.GetBlobBytes(assembly.PublicKey).Length != 0)
            return "Managed adapter assembly identity must be Hob.ManagedAdapters, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";

        if (reader.AssemblyFiles.Count != 0 || reader.GetTableRowCount(TableIndex.ModuleRef) != 0)
            return "Managed adapter cannot contain linked netmodules, external files, or native module references";

        foreach (var methodHandle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if ((method.Attributes & MethodAttributes.PinvokeImpl) != 0)
                return "Managed adapter cannot contain P/Invoke methods";
            if ((method.ImplAttributes & MethodImplAttributes.CodeTypeMask) != MethodImplAttributes.IL ||
                (method.ImplAttributes & MethodImplAttributes.ManagedMask) != MethodImplAttributes.Managed)
                return "Managed adapter methods must use managed IL implementations";
        }

        foreach (var referenceHandle in reader.AssemblyReferences)
        {
            if (!TryCreateAssemblyReferenceIdentity(reader, referenceHandle, out var identity) ||
                !frameworkReferences.Contains(identity))
                return "Managed adapter references an assembly outside the selected .NET 10 reference pack";
        }

        if (!HasExpectedTargetFramework(reader))
            return "Managed adapter must declare TargetFramework .NETCoreApp,Version=v10.0";

        if (reader.ExportedTypes.Count != 0)
            return "Managed adapter cannot define exported type forwarders";

        var targetTypeHandle = default(TypeDefinitionHandle);
        var targetTypeCount = 0;
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            var typeName = GetTypeName(reader, type.Namespace, type.Name);
            if (typeName == definition.TypeName)
            {
                targetTypeHandle = typeHandle;
                targetTypeCount++;
            }
            else if (IsExternallyVisibleType(type.Attributes))
            {
                return "Managed adapter cannot expose public types beyond its catalog type";
            }
        }

        if (targetTypeCount != 1)
            return $"Managed adapter must define exactly one public type named {definition.TypeName}";

        var targetType = reader.GetTypeDefinition(targetTypeHandle);
        if ((targetType.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public ||
            (targetType.Attributes & TypeAttributes.Abstract) == 0 ||
            (targetType.Attributes & TypeAttributes.Sealed) == 0 ||
            (targetType.Attributes & TypeAttributes.Interface) != 0 ||
            targetType.GetGenericParameters().Count != 0 ||
            targetType.GetInterfaceImplementations().Count != 0 ||
            !HasSystemObjectBaseType(reader, targetType))
            return $"Managed adapter type {definition.TypeName} must be a public, abstract, sealed, non-generic class";

        var targetMethodCount = 0;
        foreach (var methodHandle in targetType.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            var methodName = reader.GetString(method.Name);
            var isPublic = (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
            if (methodName == definition.MethodName)
            {
                targetMethodCount++;
                if (!isPublic ||
                    (method.Attributes & MethodAttributes.Static) == 0 ||
                    method.GetGenericParameters().Count != 0 ||
                    !HasStringToStringSignature(reader, method) ||
                    method.RelativeVirtualAddress == 0)
                    return "Managed adapter catalog method must be public static string HashUtf8(string) with no generic arity";
            }

            if (isPublic)
            {
                if (methodName != definition.MethodName)
                    return "Managed adapter cannot expose public methods beyond its catalog method";
            }
        }

        if (targetMethodCount != 1)
            return "Managed adapter must expose exactly one catalog method and no extra public methods";

        foreach (var fieldHandle in targetType.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if ((field.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public)
                return "Managed adapter cannot expose public fields";
        }

        return null;
    }

    private static bool HasStringToStringSignature(MetadataReader reader, MethodDefinition method)
    {
        var signature = reader.GetBlobReader(method.Signature);
        if (signature.ReadByte() != 0x00 || signature.ReadCompressedInteger() != 1 ||
            signature.ReadByte() != 0x0e || signature.ReadByte() != 0x0e)
            return false;
        return signature.Offset == signature.Length;
    }

    private static bool HasExpectedTargetFramework(MetadataReader reader)
    {
        var count = 0;
        foreach (var attributeHandle in reader.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(attributeHandle);
            if (GetAttributeTypeName(reader, attribute.Constructor) != "System.Runtime.Versioning.TargetFrameworkAttribute")
                continue;

            count++;
            try
            {
                var blob = reader.GetBlobReader(attribute.Value);
                if (blob.ReadUInt16() != 1 || blob.ReadSerializedString() != RequiredTargetFramework)
                    return false;
            }
            catch (BadImageFormatException)
            {
                return false;
            }
        }

        return count == 1;
    }

    private static string? GetAttributeTypeName(MetadataReader reader, EntityHandle constructor)
    {
        EntityHandle typeHandle = constructor.Kind switch
        {
            HandleKind.MethodDefinition => reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            HandleKind.MemberReference => reader.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            _ => default
        };

        return typeHandle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeName(
                reader,
                reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Namespace,
                reader.GetTypeDefinition((TypeDefinitionHandle)typeHandle).Name),
            HandleKind.TypeReference => GetTypeName(
                reader,
                reader.GetTypeReference((TypeReferenceHandle)typeHandle).Namespace,
                reader.GetTypeReference((TypeReferenceHandle)typeHandle).Name),
            _ => null
        };
    }

    private static bool IsExternallyVisibleType(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask) is TypeAttributes.Public or TypeAttributes.NestedPublic;

    private static string GetTypeName(MetadataReader reader, StringHandle namespaceHandle, StringHandle nameHandle)
    {
        var typeName = reader.GetString(nameHandle);
        var typeNamespace = namespaceHandle.IsNil ? string.Empty : reader.GetString(namespaceHandle);
        return typeNamespace.Length == 0 ? typeName : $"{typeNamespace}.{typeName}";
    }

    private static bool HasSystemObjectBaseType(MetadataReader reader, TypeDefinition type)
    {
        if (type.BaseType.Kind != HandleKind.TypeReference)
            return false;

        var baseType = reader.GetTypeReference((TypeReferenceHandle)type.BaseType);
        if (GetTypeName(reader, baseType.Namespace, baseType.Name) != "System.Object" ||
            baseType.ResolutionScope.Kind != HandleKind.AssemblyReference)
            return false;

        var assembly = reader.GetAssemblyReference((AssemblyReferenceHandle)baseType.ResolutionScope);
        return reader.GetString(assembly.Name) == "System.Runtime";
    }

    private static string NormalizeCulture(MetadataReader reader, StringHandle cultureHandle)
    {
        var culture = cultureHandle.IsNil ? string.Empty : reader.GetString(cultureHandle);
        return string.IsNullOrEmpty(culture) ? "neutral" : culture;
    }

    private static bool TryGetFrameworkReferenceIdentities(
        out IReadOnlySet<AssemblyIdentityKey>? identities,
        out string error)
    {
        identities = null;
        error = string.Empty;
        var referenceDirectory = FindNet10ReferenceDirectory();
        if (referenceDirectory is null)
        {
            error = "The selected .NET 10 reference pack could not be found; install/restore the net10.0 target pack before loading managed adapters";
            return false;
        }

        var collected = new HashSet<AssemblyIdentityKey>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(referenceDirectory, "*.dll", SearchOption.TopDirectoryOnly))
            {
                using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var peReader = new PEReader(file, PEStreamOptions.LeaveOpen);
                if (!peReader.HasMetadata)
                    continue;
                var reader = peReader.GetMetadataReader();
                if (!reader.IsAssembly)
                    continue;
                var assembly = reader.GetAssemblyDefinition();
                collected.Add(CreateAssemblyDefinitionIdentity(reader, assembly));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            error = "Could not read the selected .NET 10 reference pack assembly identities";
            return false;
        }

        if (collected.Count == 0)
        {
            error = "The selected .NET 10 reference pack contains no readable assembly identities";
            return false;
        }

        identities = collected;
        return true;
    }

    private static string? FindNet10ReferenceDirectory()
    {
        var roots = new List<string>();
        void AddRoot(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path))
                roots.Add(Path.GetFullPath(path));
        }

        AddRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"));
        AddRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT_X64"));
        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
        AddRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"));

        void AddAncestorCandidates(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var current = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
            while (!string.IsNullOrEmpty(current))
            {
                AddRoot(current);
                AddRoot(Path.Combine(current, ".dotnet"));
                current = Directory.GetParent(current)?.FullName;
            }
        }

        AddAncestorCandidates(AppContext.BaseDirectory);
        AddAncestorCandidates(Environment.CurrentDirectory);

        var requestedVersion = Environment.Version;
        foreach (var root in roots.Distinct(PathComparer))
        {
            var packRoot = Path.Combine(root, "packs", "Microsoft.NETCore.App.Ref");
            if (!Directory.Exists(packRoot))
                continue;

            var versions = Directory.EnumerateDirectories(packRoot)
                .Select(path => (Path: path, Name: Path.GetFileName(path)))
                .Select(item => (item.Path, item.Name, Parsed: Version.TryParse(item.Name, out var version) ? version : null))
                .Where(item => item.Parsed is { Major: 10 })
                .OrderByDescending(item => item.Parsed)
                .ToArray();
            if (versions.Length == 0)
                continue;

            var selected = versions.FirstOrDefault(item =>
                item.Parsed!.Major == requestedVersion.Major &&
                item.Parsed.Minor == requestedVersion.Minor &&
                item.Parsed.Build == requestedVersion.Build);
            var pack = selected.Path is null ? versions[0].Path : selected.Path;
            var referenceDirectory = Path.Combine(pack, "ref", "net10.0");
            if (Directory.Exists(referenceDirectory))
                return referenceDirectory;
        }

        return null;
    }

    private static bool TryCreateAssemblyReferenceIdentity(
        MetadataReader reader,
        AssemblyReferenceHandle handle,
        out AssemblyIdentityKey identity)
    {
        var reference = reader.GetAssemblyReference(handle);
        var keyOrToken = reference.PublicKeyOrToken.IsNil
            ? []
            : reader.GetBlobBytes(reference.PublicKeyOrToken);
        var token = (reference.Flags & AssemblyFlags.PublicKey) != 0
            ? ComputePublicKeyToken(keyOrToken)
            : Convert.ToHexString(keyOrToken).ToLowerInvariant();
        identity = new AssemblyIdentityKey(
            reader.GetString(reference.Name),
            reference.Version,
            NormalizeCulture(reader, reference.Culture),
            token);
        return true;
    }

    private static AssemblyIdentityKey CreateAssemblyDefinitionIdentity(
        MetadataReader reader,
        AssemblyDefinition assembly)
    {
        var publicKey = assembly.PublicKey.IsNil ? [] : reader.GetBlobBytes(assembly.PublicKey);
        return new AssemblyIdentityKey(
            reader.GetString(assembly.Name),
            assembly.Version,
            NormalizeCulture(reader, assembly.Culture),
            ComputePublicKeyToken(publicKey));
    }

    private static string ComputePublicKeyToken(byte[] publicKey)
    {
        if (publicKey.Length == 0)
            return string.Empty;
        var hash = SHA1.HashData(publicKey);
        Span<byte> token = stackalloc byte[8];
        for (var index = 0; index < token.Length; index++)
            token[index] = hash[hash.Length - 1 - index];
        return Convert.ToHexString(token).ToLowerInvariant();
    }

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static bool TryResolveRegularPackageFile(string packageRoot, string relativePath, out string fullPath)
    {
        var root = Path.GetFullPath(packageRoot);
        fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return false;

        var current = root;
        var segments = relativePath.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            var attributes = File.GetAttributes(current);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                return false;
            var isFinal = index == segments.Length - 1;
            if (isFinal)
            {
                if ((attributes & FileAttributes.Directory) != 0 || !File.Exists(current))
                    return false;
            }
            else if ((attributes & FileAttributes.Directory) == 0)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ReadBoundedAssemblySnapshot(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length <= 0 || file.Length > MaximumAdapterAssemblyBytes)
            throw new IOException($"Managed adapter assembly must be between 1 byte and {MaximumAdapterAssemblyBytes} bytes");

        using var snapshot = new MemoryStream(capacity: (int)file.Length);
        var buffer = new byte[81920];
        while (true)
        {
            var bytesRead = file.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
                break;
            if (snapshot.Length + bytesRead > MaximumAdapterAssemblyBytes)
                throw new IOException($"Managed adapter assembly exceeds the {MaximumAdapterAssemblyBytes}-byte size limit");
            snapshot.Write(buffer, 0, bytesRead);
        }

        if (snapshot.Length == 0)
            throw new IOException("Managed adapter assembly is empty");
        return snapshot.ToArray();
    }

    private static bool IsPortablePackageFilePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || path[0] == '/' || Path.IsPathRooted(path))
            return false;
        return path.Split('/').All(segment =>
            segment != ".." && PortablePackagePath.IsValidRelativeSegment(segment));
    }

    private static bool HasExactProperties(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;
        var index = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || index >= expected.Length || property.Name != expected[index])
                return false;
            index++;
        }
        return index == expected.Length;
    }

    private static bool IsLowerHash(JsonElement element)
    {
        if (!IsString(element))
            return false;
        return IsLowerSha256(element.GetString()!);
    }

    private static bool IsString(JsonElement element) => element.ValueKind == JsonValueKind.String;

    private static bool IsLowerSha256(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static Diagnostic ManifestDiagnostic(string message, string file) =>
        new("E_MANAGED_ADAPTER", message, file, new Range(1, 1, 1, 1));

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly record struct AssemblyIdentityKey(
        string Name,
        Version Version,
        string Culture,
        string PublicKeyToken);
}
