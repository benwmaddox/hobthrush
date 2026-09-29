using System.Text;
using System.Security.Cryptography;

internal sealed record ProjectWorkflowResult(
    int ExitCode,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? Message = null);

internal static class ProjectWorkflow
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly HashSet<string> ReservedAliases = new(StringComparer.Ordinal)
    {
        "await", "false", "if", "match", "null", "self", "true", "with"
    };

    public static ProjectWorkflowResult Create(string kind, string name, string workingDirectory)
    {
        if (kind is not ("lib" or "cli" or "web"))
            return Failure("E_MANIFEST", "Package kind must be \"lib\", \"cli\", or \"web\"", workingDirectory);

        if (!IsSafePackageName(name))
        {
            return Failure(
                "E_MANIFEST",
                "Package name must be filesystem-safe: start with an ASCII letter or digit, then use only letters, digits, '.', '_' or '-'; reserved device names are not allowed",
                workingDirectory);
        }

        string parent;
        string target;
        try
        {
            parent = Path.GetFullPath(workingDirectory);
            target = Path.GetFullPath(Path.Combine(parent, name));
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Failure("E_IO", $"Invalid package target: {error.Message}", workingDirectory);
        }

        try
        {
            if (PathEntryExists(target))
                return Failure("E_IO", $"Package target already exists: '{target}'", target);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Failure("E_IO", $"Could not inspect package target: {error.Message}", target);
        }

        var staging = Path.Combine(parent, $".{name}.lang-new-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(staging);
            WritePackageTemplate(staging, kind, name);

            var loaded = PackageLoader.Load(staging);
            if (loaded.Diagnostics.Count != 0 || loaded.Package is null)
            {
                List<Diagnostic> diagnostics = loaded.Diagnostics.Count != 0
                    ? loaded.Diagnostics
                    : [AtStart("E_MANIFEST", "Generated package manifest could not be loaded", Path.Combine(staging, "lang.toml"))];
                TryDeleteStaging(staging);
                return new ProjectWorkflowResult(1, diagnostics);
            }

            if (PathEntryExists(target))
            {
                TryDeleteStaging(staging);
                return Failure("E_IO", $"Package target already exists: '{target}'", target);
            }

            Directory.Move(staging, target);
            return new ProjectWorkflowResult(0, [], $"Created {kind} package at '{target}'.");
        }
        catch (Exception error) when (IsFileError(error))
        {
            TryDeleteStaging(staging);
            return Failure("E_IO", $"Could not create package: {error.Message}", target);
        }
    }

    public static ProjectWorkflowResult Add(string packageDirectoryArgument, string dependencyInput)
    {
        string packageDirectory;
        try
        {
            packageDirectory = Path.GetFullPath(packageDirectoryArgument);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Failure("E_IO", $"Invalid package directory: {error.Message}", packageDirectoryArgument);
        }

        if (!Directory.Exists(packageDirectory))
            return Failure("E_MANIFEST", "Package directory does not exist", packageDirectory);

        PackageLoadResult rootLoad;
        try
        {
            rootLoad = PackageLoader.Load(packageDirectory);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Failure("E_IO", $"Could not load package directory: {error.Message}", packageDirectory);
        }

        if (rootLoad.Diagnostics.Count != 0 || rootLoad.Package is null)
        {
            return new ProjectWorkflowResult(
                1,
                rootLoad.Diagnostics.Count != 0
                    ? rootLoad.Diagnostics
                    : [AtStart("E_MANIFEST", "Could not load package manifest", Path.Combine(packageDirectory, "lang.toml"))]);
        }

        var rootPackage = rootLoad.Package;
        if (!TryCreateDependencySpec(packageDirectory, dependencyInput, out var spec, out var specError))
            return Failure("E_DEPENDENCY", specError, rootPackage.ManifestFile);

        var duplicate = FindDuplicateSource(rootPackage, spec!);
        if (duplicate is not null)
            return Failure("E_DEPENDENCY", $"Dependency source is already declared under alias '{duplicate}'", rootPackage.ManifestFile);

        byte[] originalManifestBytes;
        string originalManifestText;
        bool manifestHadBom;
        try
        {
            originalManifestBytes = File.ReadAllBytes(rootPackage.ManifestFile);
            originalManifestText = DecodeManifest(originalManifestBytes, out manifestHadBom);
        }
        catch (Exception error) when (IsFileError(error) || error is DecoderFallbackException)
        {
            return Failure("E_IO", $"Could not read package manifest bytes: {error.Message}", rootPackage.ManifestFile);
        }

        if (!string.Equals(originalManifestText, rootPackage.ManifestText, StringComparison.Ordinal))
            return Failure("E_MANIFEST", "Package manifest changed while resolving the dependency; run 'lang add' again", rootPackage.ManifestFile);

        if (!TryCapturePreviousLock(packageDirectory, out var previousLock, out var lockCaptureDiagnostic))
            return new ProjectWorkflowResult(1, [lockCaptureDiagnostic!]);

        PackageDependencyLoadResult dependencyLoad;
        try
        {
            dependencyLoad = PackageLoader.ResolveDependencyPackage(
                packageDirectory,
                spec!,
                PackageResolutionMode.AllowFetch);
        }
        catch (Exception error) when (IsFileError(error) || error is InvalidOperationException)
        {
            return Failure("E_DEPENDENCY", $"Could not resolve dependency: {error.Message}", rootPackage.ManifestFile);
        }

        if (dependencyLoad.Diagnostics.Count != 0 || dependencyLoad.Package is null)
        {
            return new ProjectWorkflowResult(
                1,
                dependencyLoad.Diagnostics.Count != 0
                    ? dependencyLoad.Diagnostics
                    : [AtStart("E_DEPENDENCY", "Could not load dependency package", rootPackage.ManifestFile)]);
        }

        var dependencyPackage = dependencyLoad.Package;
        if (!dependencyPackage.Manifest.IsLibrary)
            return Failure("E_DEPENDENCY", "Dependencies must point to packages with kind = \"lib\"", rootPackage.ManifestFile);

        if (string.Equals(dependencyPackage.Manifest.Name, rootPackage.Manifest.Name, StringComparison.Ordinal) &&
            string.Equals(dependencyPackage.Manifest.Version, rootPackage.Manifest.Version, StringComparison.Ordinal))
            return Failure("E_DEPENDENCY", "A package cannot depend on itself", rootPackage.ManifestFile);

        var alias = DeriveAlias(dependencyPackage.Manifest.Name);
        if (rootPackage.Manifest.Dependencies.Any(dependency =>
                string.Equals(dependency.Alias, alias, StringComparison.OrdinalIgnoreCase)))
        {
            return Failure(
                "E_DEPENDENCY",
                $"Package '{dependencyPackage.Manifest.Name}' maps to alias '{alias}', which is already in use",
                rootPackage.ManifestFile);
        }

        string updatedManifestText;
        byte[] updatedManifestBytes;
        try
        {
            updatedManifestText = AppendDependency(
                originalManifestText,
                alias,
                spec!.FormatManifest());
            updatedManifestBytes = EncodeManifest(updatedManifestText, manifestHadBom);
        }
        catch (Exception error) when (error is EncoderFallbackException or ArgumentException)
        {
            return Failure("E_MANIFEST", $"Could not encode the updated package manifest: {error.Message}", rootPackage.ManifestFile);
        }

        try
        {
            var currentManifestBytes = File.ReadAllBytes(rootPackage.ManifestFile);
            if (!currentManifestBytes.AsSpan().SequenceEqual(originalManifestBytes))
                return Failure("E_MANIFEST", "Package manifest changed while resolving the dependency; run 'lang add' again", rootPackage.ManifestFile);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return Failure("E_IO", $"Could not recheck package manifest: {error.Message}", rootPackage.ManifestFile);
        }

        var manifestWrite = WriteAtomically(rootPackage.ManifestFile, updatedManifestBytes, "package manifest");
        if (manifestWrite is not null)
        {
            return new ProjectWorkflowResult(
                1,
                RollBackAdd(rootPackage.ManifestFile, originalManifestBytes, packageDirectory, previousLock, [manifestWrite]));
        }

        try
        {
            var graphResult = PackageLoader.ResolveGraph(packageDirectory, PackageResolutionMode.AllowFetch);
            if (graphResult.Diagnostics.Count != 0 || graphResult.Graph is null)
            {
                List<Diagnostic> diagnostics = graphResult.Diagnostics.Count != 0
                    ? graphResult.Diagnostics
                    : [AtStart("E_DEPENDENCY", "Could not resolve package dependency graph", rootPackage.ManifestFile)];
                return new ProjectWorkflowResult(
                    1,
                    RollBackAdd(rootPackage.ManifestFile, originalManifestBytes, packageDirectory, previousLock, diagnostics));
            }

            var lockWriteDiagnostics = PackageLock.Write(graphResult.Graph);
            if (lockWriteDiagnostics.Count != 0)
            {
                return new ProjectWorkflowResult(
                    1,
                    RollBackAdd(
                        rootPackage.ManifestFile,
                        originalManifestBytes,
                        packageDirectory,
                        previousLock,
                        lockWriteDiagnostics));
            }

            var lockValidationDiagnostics = PackageLock.Validate(graphResult.Graph);
            if (lockValidationDiagnostics.Count != 0)
            {
                return new ProjectWorkflowResult(
                    1,
                    RollBackAdd(
                        rootPackage.ManifestFile,
                        originalManifestBytes,
                        packageDirectory,
                        previousLock,
                        lockValidationDiagnostics));
            }

            return new ProjectWorkflowResult(
                0,
                [],
                $"Added dependency '{alias}' to '{rootPackage.Manifest.Name}' and updated lang.lock.");
        }
        catch (Exception error) when (IsFileError(error) || error is InvalidOperationException or EncoderFallbackException or CryptographicException)
        {
            return new ProjectWorkflowResult(
                1,
                RollBackAdd(
                    rootPackage.ManifestFile,
                    originalManifestBytes,
                    packageDirectory,
                    previousLock,
                    [AtStart("E_LOCK", $"Could not update or validate package lock: {error.Message}", Path.Combine(packageDirectory, "lang.lock"))]));
        }
    }

    private static void WritePackageTemplate(string root, string kind, string name)
    {
        var manifest = $"name = \"{name}\"\nversion = \"0.1.0\"\nkind = \"{kind}\"\nsource_root = \"src\"\n";

        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        switch (kind)
        {
            case "lib":
                files.Add("src/starter/lib.lang", LibrarySource);
                break;
            case "cli":
                manifest += "entry_module = \"app::main\"\n";
                files.Add("src/app/main.lang", CliSource);
                break;
            case "web":
                manifest += "entry_module = \"app::main\"\n\n[capabilities]\nnet.listen = \"allow\"\n";
                files.Add("src/app/main.lang", WebSource);
                break;
        }

        File.WriteAllText(Path.Combine(root, "lang.toml"), manifest, Utf8WithoutBom);
        foreach (var (relativePath, source) in files)
        {
            var file = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, source, Utf8WithoutBom);
        }
    }

    private static bool TryCreateDependencySpec(
        string packageDirectory,
        string input,
        out PackageDependencySpec? spec,
        out string error)
    {
        spec = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "Dependency path or Git URL must not be empty";
            return false;
        }

        if (input.StartsWith("git+", StringComparison.Ordinal))
            return PackageDependencySpec.TryParseManifest(input, out spec, out error);

        if (input.Contains("://", StringComparison.Ordinal))
        {
            var separator = input.LastIndexOf('#');
            if (separator <= 0 || separator == input.Length - 1)
            {
                error = "Git dependency must be an exact URL followed by '#' and a 40-character lowercase commit";
                return false;
            }

            return PackageDependencySpec.TryParseManifest("git+" + input, out spec, out error);
        }

        try
        {
            var dependencyRoot = Path.GetFullPath(input, packageDirectory);
            var relativePath = Path.GetRelativePath(packageDirectory, dependencyRoot)
                .Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');
            if (relativePath == ".")
            {
                error = "A package cannot be added as its own dependency";
                return false;
            }

            return PackageDependencySpec.TryParseManifest(relativePath, out spec, out error);
        }
        catch (Exception pathError) when (IsFileError(pathError))
        {
            error = $"Invalid dependency path: {pathError.Message}";
            return false;
        }
    }

    private static string? FindDuplicateSource(LoadedPackage root, PackageDependencySpec requested)
    {
        foreach (var dependency in root.Manifest.Dependencies)
        {
            if (requested.Kind == PackageDependencyKind.Git &&
                dependency.Spec.Kind == PackageDependencyKind.Git &&
                string.Equals(requested.Url, dependency.Spec.Url, StringComparison.Ordinal) &&
                string.Equals(requested.Commit, dependency.Spec.Commit, StringComparison.Ordinal))
                return dependency.Alias;

            if (requested.Kind != PackageDependencyKind.Path || dependency.Spec.Kind != PackageDependencyKind.Path)
                continue;

            try
            {
                var requestedRoot = Path.GetFullPath(Path.Combine(
                    root.Root,
                    requested.Path!.Replace('/', Path.DirectorySeparatorChar)));
                var existingRoot = Path.GetFullPath(Path.Combine(
                    root.Root,
                    dependency.Spec.Path!.Replace('/', Path.DirectorySeparatorChar)));
                if (string.Equals(requestedRoot, existingRoot, PathComparison))
                    return dependency.Alias;
            }
            catch (Exception error) when (IsFileError(error))
            {
                if (string.Equals(requested.Path, dependency.Spec.Path, StringComparison.Ordinal))
                    return dependency.Alias;
            }
        }

        return null;
    }

    private static string DeriveAlias(string packageName)
    {
        var builder = new StringBuilder(packageName.Length);
        var previousWasSeparator = false;
        foreach (var character in packageName)
        {
            if (character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSeparator = false;
            }
            else if (builder.Length != 0 && !previousWasSeparator)
            {
                builder.Append('_');
                previousWasSeparator = true;
            }
        }

        var alias = builder.ToString().TrimEnd('_');
        if (alias.Length == 0)
            alias = "package";
        if (alias[0] is >= '0' and <= '9')
            alias = "pkg_" + alias;
        if (ReservedAliases.Contains(alias))
            alias = "dep_" + alias;
        return alias;
    }

    private static string AppendDependency(string text, string alias, string manifestValue)
    {
        var newline = FindNewline(text);
        var hasSection = TryFindDependencySectionEnd(text, out var insertionOffset);
        if (!hasSection)
            insertionOffset = text.Length;

        var prefix = text[..insertionOffset];
        var suffix = text[insertionOffset..];
        var separator = prefix.Length != 0 && prefix[^1] is not ('\r' or '\n') ? newline : string.Empty;
        var tableHeader = hasSection ? string.Empty : $"[dependencies]{newline}";
        var assignment = $"{alias} = \"{manifestValue}\"{newline}";
        return prefix + separator + tableHeader + assignment + suffix;
    }

    private static bool TryFindDependencySectionEnd(string text, out int insertionOffset)
    {
        insertionOffset = text.Length;
        var insideDependencies = false;
        var lineStart = 0;
        while (lineStart < text.Length)
        {
            var lineEnd = lineStart;
            while (lineEnd < text.Length && text[lineEnd] is not ('\r' or '\n'))
                lineEnd++;

            var trimmed = StripManifestComment(text[lineStart..lineEnd]).Trim();
            if (trimmed.StartsWith('['))
            {
                if (insideDependencies)
                {
                    insertionOffset = lineStart;
                    return true;
                }

                if (trimmed == "[dependencies]")
                    insideDependencies = true;
            }

            if (lineEnd == text.Length)
                break;

            lineStart = lineEnd + 1;
            if (text[lineEnd] == '\r' && lineStart < text.Length && text[lineStart] == '\n')
                lineStart++;
        }

        return insideDependencies;
    }

    private static string StripManifestComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
                inString = !inString;
            else if (line[index] == '#' && !inString)
                return line[..index];
        }

        return line;
    }

    private static string FindNewline(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
                return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
            if (text[index] == '\n')
                return "\n";
        }

        return "\n";
    }

    private static string DecodeManifest(byte[] bytes, out bool hadBom)
    {
        hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return StrictUtf8.GetString(hadBom ? bytes.AsSpan(3) : bytes);
    }

    private static byte[] EncodeManifest(string text, bool hadBom)
    {
        var body = StrictUtf8.GetBytes(text);
        if (!hadBom)
            return body;

        var bytes = new byte[3 + body.Length];
        bytes[0] = 0xEF;
        bytes[1] = 0xBB;
        bytes[2] = 0xBF;
        body.CopyTo(bytes, 3);
        return bytes;
    }

    private static bool TryCapturePreviousLock(
        string packageDirectory,
        out PreviousLock previousLock,
        out Diagnostic? diagnostic)
    {
        var lockFile = Path.Combine(packageDirectory, "lang.lock");
        previousLock = new PreviousLock(false, null);
        diagnostic = null;
        try
        {
            var attributes = File.GetAttributes(lockFile);
            if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            {
                diagnostic = AtStart(
                    "E_LOCK",
                    "Cannot replace a symbolic link, reparse point, or directory at the package lock path",
                    lockFile);
                return false;
            }

            previousLock = new PreviousLock(true, File.ReadAllBytes(lockFile));
            return true;
        }
        catch (FileNotFoundException)
        {
            previousLock = new PreviousLock(false, null);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            previousLock = new PreviousLock(false, null);
            return true;
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostic = AtStart("E_IO", $"Could not preserve the existing package lock: {error.Message}", lockFile);
            return false;
        }
    }

    private static List<Diagnostic> RollBackAdd(
        string manifestFile,
        byte[] originalManifest,
        string packageDirectory,
        PreviousLock previousLock,
        IReadOnlyList<Diagnostic> originalDiagnostics)
    {
        var diagnostics = originalDiagnostics.ToList();
        var lockFile = Path.Combine(packageDirectory, "lang.lock");
        if (previousLock.Existed)
        {
            var lockRestore = WriteAtomically(lockFile, previousLock.Bytes!, "previous package lock");
            if (lockRestore is not null)
                diagnostics.Add(lockRestore);
        }
        else
        {
            try
            {
                if (PathEntryExists(lockFile))
                {
                    var attributes = File.GetAttributes(lockFile);
                    if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(AtStart(
                            "E_IO",
                            "Could not remove the new package lock during rollback because the lock path is a directory or reparse point",
                            lockFile));
                    }
                    else
                    {
                        File.Delete(lockFile);
                    }
                }
            }
            catch (Exception error) when (IsFileError(error))
            {
                diagnostics.Add(AtStart("E_IO", $"Could not remove the new package lock during rollback: {error.Message}", lockFile));
            }
        }

        var manifestRestore = WriteAtomically(manifestFile, originalManifest, "original package manifest");
        if (manifestRestore is not null)
            diagnostics.Add(manifestRestore);
        return diagnostics;
    }

    private static Diagnostic? WriteAtomically(string target, byte[] bytes, string description)
    {
        string? temporaryFile = null;
        try
        {
            try
            {
                var attributes = File.GetAttributes(target);
                if ((attributes & FileAttributes.Directory) != 0 || (attributes & FileAttributes.ReparsePoint) != 0)
                    return AtStart("E_IO", $"Cannot replace a directory, symbolic link, or reparse point at the {description} path", target);
            }
            catch (FileNotFoundException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }

            var parent = Path.GetDirectoryName(target)!;
            temporaryFile = Path.Combine(parent, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
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

            File.Move(temporaryFile, target, overwrite: true);
            temporaryFile = null;
            return null;
        }
        catch (Exception error) when (IsFileError(error))
        {
            return AtStart("E_IO", $"Could not write {description}: {error.Message}", target);
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
                    // Preserve the primary write diagnostic if temporary cleanup fails.
                }
            }
        }
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private sealed record PreviousLock(bool Existed, byte[]? Bytes);

    private const string LibrarySource = """
        module starter::lib;

        pub fn identity(value: Text) -> Text effects {} {
            return value;
        }

        test "identity preserves its input" {
            assert self::starter::lib::identity("hello") == "hello";
        }
        """;

    private const string CliSource = """
        module app::main;

        pub fn main() -> Text effects {} {
            return "hello from your package";
        }

        test "main returns its greeting" {
            assert self::app::main::main() == "hello from your package";
        }
        """;

    private const string WebSource = """
        module app::main;

        pub union HealthReply { Healthy }

        fn health() -> self::app::main::HealthReply effects {} {
            return self::app::main::HealthReply.Healthy;
        }

        route GET "/health" {
            handler: self::app::main::health;
            response Healthy: 200;
        }

        test "health handler returns a healthy reply" {
            assert match self::app::main::health() {
                self::app::main::HealthReply.Healthy => true,
            };
        }
        """;

    private static bool IsSafePackageName(string name)
    {
        static bool IsAsciiAlphaNumeric(char character) =>
            character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

        if (name.Length == 0 || !IsAsciiAlphaNumeric(name[0]) || name[^1] == '.')
            return false;
        if (name.Any(character => !IsAsciiAlphaNumeric(character) && character is not ('.' or '_' or '-')))
            return false;

        var deviceName = name.Split('.')[0];
        if (deviceName.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            return false;

        return !(deviceName.Length == 4 &&
                 (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                  deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                 deviceName[3] is >= '1' and <= '9');
    }

    private static bool PathEntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
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

    private static void TryDeleteStaging(string staging)
    {
        try
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        catch (Exception error) when (IsFileError(error))
        {
            // Preserve the primary generation diagnostic if cleanup is blocked.
        }
    }

    private static ProjectWorkflowResult Failure(string code, string message, string file) =>
        new(1, [AtStart(code, message, file)]);

    private static Diagnostic AtStart(string code, string message, string file) =>
        new(code, message, file, new Range(1, 1, 1, 1));

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
