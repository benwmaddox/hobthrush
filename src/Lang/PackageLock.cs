using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class PackageLock
{
    private const string FileName = "lang.lock";
    private const int SchemaVersion = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly byte[] ContentHashDomain = Encoding.UTF8.GetBytes("LANG-PACKAGE-CONTENT\0v1");
    private static readonly HashSet<string> ReservedDependencyAliases = new(StringComparer.Ordinal)
    {
        "await", "false", "if", "match", "null", "true", "with"
    };

    public static byte[] Create(PackageDependencyGraph graph)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
               {
                   Indented = true,
                   IndentSize = 2
               }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", SchemaVersion);

            writer.WritePropertyName("root");
            writer.WriteStartObject();
            writer.WriteString("name", graph.Root.Package.Manifest.Name);
            writer.WriteString("version", graph.Root.Package.Manifest.Version);
            writer.WriteString("manifest_sha256", HashNormalizedText(graph.Root.Package.ManifestText));
            WriteDependencies(writer, graph, graph.Root);
            writer.WriteEndObject();

            writer.WritePropertyName("packages");
            writer.WriteStartArray();
            foreach (var package in graph.Nodes
                         .Where(node => !ReferenceEquals(node, graph.Root))
                         .OrderBy(node => node.RelativePath, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", package.RelativePath);
                writer.WriteString("name", package.Package.Manifest.Name);
                writer.WriteString("version", package.Package.Manifest.Version);
                writer.WriteString("content_sha256", HashPackageContent(package.Package));
                WriteDependencies(writer, graph, package);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        var serializedText = StrictUtf8.GetString(stream.ToArray())
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        return StrictUtf8.GetBytes(serializedText.TrimEnd('\n') + "\n");
    }

    public static List<Diagnostic> Write(PackageDependencyGraph graph)
    {
        if (!HasDependencies(graph))
            return [];

        var lockFile = Path.Combine(graph.Root.Package.Root, FileName);
        var diagnostics = new List<Diagnostic>();
        string? temporaryFile = null;
        try
        {
            if (ExistsAsReparsePoint(lockFile) || Directory.Exists(lockFile))
            {
                diagnostics.Add(LockDiagnostic(
                    "Cannot replace a symbolic link, reparse point, or directory at the package lock path",
                    lockFile));
                return diagnostics;
            }

            var bytes = Create(graph);
            temporaryFile = Path.Combine(
                graph.Root.Package.Root,
                $".{FileName}.{Guid.NewGuid():N}.tmp");
            using (var file = new FileStream(
                       temporaryFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }

            File.Move(temporaryFile, lockFile, overwrite: true);
            temporaryFile = null;
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(LockDiagnostic($"Could not write package lock: {error.Message}", lockFile));
        }
        finally
        {
            if (temporaryFile is not null)
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch (Exception error) when (IsFileError(error))
                {
                    // A failed cleanup must not hide the original lock write diagnostic.
                }
            }
        }

        return diagnostics;
    }

    public static List<Diagnostic> Validate(PackageDependencyGraph graph)
    {
        if (!HasDependencies(graph))
            return [];

        var lockFile = Path.Combine(graph.Root.Package.Root, FileName);
        if (!File.Exists(lockFile))
        {
            return
            [
                LockDiagnostic("Package lock is missing; run 'lang lock' to create it", lockFile)
            ];
        }

        byte[] bytes;
        try
        {
            if (ExistsAsReparsePoint(lockFile) || Directory.Exists(lockFile))
                return [LockDiagnostic("Package lock is malformed or unreadable; run 'lang lock' to regenerate it", lockFile)];

            bytes = File.ReadAllBytes(lockFile);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return [LockDiagnostic("Package lock is malformed or unreadable; run 'lang lock' to regenerate it", lockFile)];
        }

        if (!HasStrictSchema(bytes))
            return [LockDiagnostic("Package lock is malformed or unsupported; run 'lang lock' to regenerate it", lockFile)];

        byte[] expected;
        try
        {
            expected = Create(graph);
        }
        catch (Exception error) when (IsFileError(error) || error is EncoderFallbackException)
        {
            return [LockDiagnostic("Could not hash package inputs; run 'lang lock' after correcting the package files", lockFile)];
        }

        if (!bytes.AsSpan().SequenceEqual(expected))
            return [LockDiagnostic("Package lock is stale or noncanonical; run 'lang lock' to update it", lockFile)];

        return [];
    }

    private static void WriteDependencies(
        Utf8JsonWriter writer,
        PackageDependencyGraph graph,
        ResolvedPackage package)
    {
        writer.WritePropertyName("dependencies");
        writer.WriteStartObject();
        foreach (var dependency in package.DependencyIds.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var targetPath = graph.ById[dependency.Value].RelativePath;
            writer.WriteString(dependency.Key, targetPath);
        }

        writer.WriteEndObject();
    }

    private static string HashPackageContent(LoadedPackage package)
    {
        var entries = new List<(string Path, string Text)>
        {
            ("lang.toml", package.ManifestText)
        };
        entries.AddRange(package.Sources.Select(source =>
            (NormalizeRelative(Path.GetRelativePath(package.Root, source.File)), source.Text)));
        entries.Sort((left, right) => StringComparer.Ordinal.Compare(left.Path, right.Path));

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(ContentHashDomain);
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        foreach (var entry in entries)
        {
            var pathBytes = StrictUtf8.GetBytes(entry.Path);
            var textBytes = StrictUtf8.GetBytes(NormalizeLineEndings(entry.Text));
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)pathBytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(pathBytes);
            BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)textBytes.LongLength);
            hash.AppendData(length);
            hash.AppendData(textBytes);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static string HashNormalizedText(string text) =>
        Convert.ToHexString(SHA256.HashData(StrictUtf8.GetBytes(NormalizeLineEndings(text)))).ToLowerInvariant();

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool HasDependencies(PackageDependencyGraph graph) =>
        graph.Nodes.Any(node => node.Package.Manifest.Dependencies.Count != 0);

    private static bool HasStrictSchema(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return false;

        try
        {
            _ = StrictUtf8.GetString(bytes);
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
            var documentRoot = document.RootElement;
            if (!HasExactProperties(documentRoot, ["schema_version", "root", "packages"]) ||
                !documentRoot.GetProperty("schema_version").TryGetInt32(out var schemaVersion) ||
                schemaVersion != SchemaVersion)
                return false;

            var root = documentRoot.GetProperty("root");
            if (!HasExactProperties(root, ["name", "version", "manifest_sha256", "dependencies"]) ||
                !IsString(root.GetProperty("name")) ||
                !IsString(root.GetProperty("version")) ||
                !IsHash(root.GetProperty("manifest_sha256")) ||
                !HasSortedDependencies(root.GetProperty("dependencies")))
                return false;

            var packages = documentRoot.GetProperty("packages");
            if (packages.ValueKind != JsonValueKind.Array)
                return false;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            string? previousPath = null;
            foreach (var package in packages.EnumerateArray())
            {
                if (!HasExactProperties(package, ["path", "name", "version", "content_sha256", "dependencies"]))
                    return false;

                var pathElement = package.GetProperty("path");
                if (!IsString(pathElement))
                    return false;
                var path = pathElement.GetString()!;
                if (!IsPortableRelativePath(path) || !seenPaths.Add(path) ||
                    (previousPath is not null && StringComparer.Ordinal.Compare(previousPath, path) >= 0))
                    return false;
                previousPath = path;

                if (!IsString(package.GetProperty("name")) ||
                    !IsString(package.GetProperty("version")) ||
                    !IsHash(package.GetProperty("content_sha256")) ||
                    !HasSortedDependencies(package.GetProperty("dependencies")))
                    return false;
            }

            return true;
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool HasExactProperties(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name) || index >= expected.Length || property.Name != expected[index])
                return false;
            index++;
        }

        return index == expected.Length;
    }

    private static bool HasSortedDependencies(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? previousAlias = null;
        foreach (var property in element.EnumerateObject())
        {
            if (!IsDependencyAlias(property.Name) || !seen.Add(property.Name) || !IsString(property.Value) ||
                (previousAlias is not null && StringComparer.Ordinal.Compare(previousAlias, property.Name) >= 0) ||
                !IsPortableRelativePath(property.Value.GetString()!))
                return false;
            previousAlias = property.Name;
        }

        return true;
    }

    private static bool IsPortableRelativePath(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\') || path.Contains(':') || Path.IsPathRooted(path))
            return false;

        return path.Split('/').All(PortablePackagePath.IsValidRelativeSegment);
    }

    private static bool IsIdentifier(string identifier) =>
        identifier.Length != 0 &&
        (char.IsLetter(identifier[0]) || identifier[0] == '_') &&
        identifier.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');

    private static bool IsDependencyAlias(string identifier) =>
        IsIdentifier(identifier) && !ReservedDependencyAliases.Contains(identifier);

    private static bool IsString(JsonElement element) => element.ValueKind == JsonValueKind.String;

    private static bool IsHash(JsonElement element)
    {
        if (!IsString(element))
            return false;

        var value = element.GetString();
        return value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    private static bool ExistsAsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static Diagnostic LockDiagnostic(string message, string file) =>
        new("E_LOCK", message, file, new Range(1, 1, 1, 1));

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
