using System.Text;

internal sealed record PackageManifest(
    string Name,
    string Version,
    string Kind,
    string SourceRoot,
    string? EntryModule)
{
    public bool IsLibrary => Kind == "lib";
}

internal sealed record PackageSource(string Module, string File);

internal sealed record LoadedPackage(
    string Root,
    string ManifestFile,
    string SourceDirectory,
    PackageManifest Manifest,
    IReadOnlyList<PackageSource> Sources);

internal sealed record PackageLoadResult(LoadedPackage? Package, List<Diagnostic> Diagnostics);

internal static class PackageLoader
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "name",
        "version",
        "kind",
        "source_root",
        "entry_module"
    };

    public static PackageLoadResult Load(string packageDirectory)
    {
        var diagnostics = new List<Diagnostic>();
        var root = Path.GetFullPath(packageDirectory);
        var manifestFile = Path.Combine(root, "lang.toml");

        if (!File.Exists(manifestFile))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "Package directory must contain a root lang.toml manifest",
                manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        if (HasReparsePoint(manifestFile))
        {
            diagnostics.Add(AtStart("E_MANIFEST", "The root lang.toml manifest cannot be a symbolic link", manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        string manifestText;
        try
        {
            manifestText = File.ReadAllText(manifestFile, new UTF8Encoding(false, true));
        }
        catch (DecoderFallbackException error)
        {
            diagnostics.Add(AtStart("E_MANIFEST", $"Manifest is not valid UTF-8: {error.Message}", manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_IO", $"Could not read package manifest: {error.Message}", manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        var values = ParseManifest(manifestText, manifestFile, diagnostics);
        ValidateManifest(values, manifestFile, diagnostics);
        if (diagnostics.Count != 0)
            return new PackageLoadResult(null, diagnostics);

        var manifest = new PackageManifest(
            values["name"],
            values["version"],
            values["kind"],
            values["source_root"],
            values.GetValueOrDefault("entry_module"));

        string sourceDirectory;
        try
        {
            sourceDirectory = Path.GetFullPath(Path.Combine(
                root,
                manifest.SourceRoot.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_MANIFEST", $"Invalid source_root path: {error.Message}", manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        if (!IsWithin(root, sourceDirectory) || string.Equals(root, sourceDirectory, PathComparison))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "source_root must name a normalized directory inside the package root",
                manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        if (!Directory.Exists(sourceDirectory))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                $"source_root directory '{manifest.SourceRoot}' does not exist",
                manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        if (HasReparsePointWithin(root, sourceDirectory))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "source_root cannot contain a symbolic link or reparse point",
                manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

        var sources = DiscoverSources(root, sourceDirectory, manifestFile, diagnostics);
        if (diagnostics.Count != 0)
            return new PackageLoadResult(null, diagnostics);

        return new PackageLoadResult(
            new LoadedPackage(root, manifestFile, sourceDirectory, manifest, sources),
            diagnostics);
    }

    public static Diagnostic ModulePathError(string message, string file) =>
        AtStart("E_MODULE_PATH", message, file);

    private static Dictionary<string, string> ParseManifest(
        string text,
        string file,
        List<Diagnostic> diagnostics)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = StripComment(line).Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            var equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                diagnostics.Add(AtLine(
                    "E_MANIFEST",
                    "Expected a simple key = \"value\" assignment",
                    file,
                    lineNumber));
                continue;
            }

            var key = trimmed[..equals].Trim();
            var rawValue = trimmed[(equals + 1)..].Trim();
            if (!IsKey(key))
            {
                diagnostics.Add(AtLine("E_MANIFEST", "Manifest keys must be simple lowercase identifiers", file, lineNumber));
                continue;
            }

            if (!KnownKeys.Contains(key))
            {
                diagnostics.Add(AtLine("E_MANIFEST", $"Unknown manifest key '{key}'", file, lineNumber));
                continue;
            }

            if (values.ContainsKey(key))
            {
                diagnostics.Add(AtLine("E_MANIFEST", $"Duplicate manifest key '{key}'", file, lineNumber));
                continue;
            }

            if (rawValue.Length < 2 || rawValue[0] != '"' || rawValue[^1] != '"')
            {
                diagnostics.Add(AtLine(
                    "E_MANIFEST",
                    $"Value for '{key}' must be a simple double-quoted string",
                    file,
                    lineNumber));
                continue;
            }

            var value = rawValue[1..^1];
            if (value.Contains('"') || value.Contains('\\') || value.Any(char.IsControl))
            {
                diagnostics.Add(AtLine(
                    "E_MANIFEST",
                    $"Value for '{key}' contains an unsupported escape or control character",
                    file,
                    lineNumber));
                continue;
            }

            values.Add(key, value);
        }

        return values;
    }

    private static void ValidateManifest(
        IReadOnlyDictionary<string, string> values,
        string file,
        List<Diagnostic> diagnostics)
    {
        foreach (var key in new[] { "name", "version", "kind", "source_root" })
        {
            if (!values.ContainsKey(key))
                diagnostics.Add(AtStart("E_MANIFEST", $"Missing required manifest key '{key}'", file));
        }

        if (values.TryGetValue("name", out var name) && !IsSafePackageName(name))
            diagnostics.Add(AtStart("E_MANIFEST", "name must be a filesystem-safe package name", file));

        if (values.TryGetValue("version", out var version) && string.IsNullOrWhiteSpace(version))
            diagnostics.Add(AtStart("E_MANIFEST", "version must not be empty", file));

        if (values.TryGetValue("kind", out var kind) && kind is not ("lib" or "cli"))
            diagnostics.Add(AtStart("E_MANIFEST", "kind must be either \"lib\" or \"cli\"", file));

        if (values.TryGetValue("source_root", out var sourceRoot) && !IsNormalizedRelativePath(sourceRoot))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "source_root must be a normalized relative path using forward slashes and cannot escape the package root",
                file));
        }

        var hasEntryModule = values.TryGetValue("entry_module", out var entryModule);
        if (values.TryGetValue("kind", out kind))
        {
            if (kind == "cli" && !hasEntryModule)
                diagnostics.Add(AtStart("E_MANIFEST", "CLI packages require entry_module", file));
            else if (kind == "lib" && hasEntryModule)
                diagnostics.Add(AtStart("E_MANIFEST", "Library packages must not declare entry_module", file));
        }

        if (hasEntryModule && !IsValidModuleName(entryModule!))
            diagnostics.Add(AtStart("E_MANIFEST", "entry_module must be a valid dotted module name", file));
    }

    private static IReadOnlyList<PackageSource> DiscoverSources(
        string root,
        string sourceDirectory,
        string manifestFile,
        List<Diagnostic> diagnostics)
    {
        var files = new List<string>();
        var directories = new Stack<string>();
        directories.Push(sourceDirectory);

        try
        {
            while (directories.Count != 0)
            {
                var directory = directories.Pop();
                foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        diagnostics.Add(AtStart(
                            "E_MANIFEST",
                            "Symbolic links and reparse points are not allowed under source_root",
                            entry));
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                        directories.Push(entry);
                    else if (string.Equals(Path.GetExtension(entry), ".lang", StringComparison.OrdinalIgnoreCase))
                        files.Add(Path.GetFullPath(entry));
                }
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_IO", $"Could not discover package sources: {error.Message}", manifestFile));
            return [];
        }

        var ordered = files
            .Select(file => (File: file, Relative: NormalizeRelative(Path.GetRelativePath(sourceDirectory, file))))
            .OrderBy(item => item.Relative, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
        {
            diagnostics.Add(ModulePathError("Package source_root contains no .lang files", manifestFile));
            return [];
        }

        var sources = new List<PackageSource>(ordered.Length);
        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ordered)
        {
            if (!IsWithin(root, item.File))
            {
                diagnostics.Add(ModulePathError("Source file resolves outside the package root", item.File));
                continue;
            }

            var segments = item.Relative.Split('/');
            segments[^1] = segments[^1][..^".lang".Length];
            if (!segments.All(IsIdentifier))
            {
                diagnostics.Add(ModulePathError(
                    $"Source path '{item.Relative}' does not map to a valid module name",
                    item.File));
                continue;
            }

            var module = string.Join('.', segments);

            if (!modules.Add(module))
            {
                diagnostics.Add(ModulePathError(
                    $"Multiple source files map to module '{module}'",
                    item.File));
                continue;
            }

            sources.Add(new PackageSource(module, item.File));
        }

        return sources;
    }

    private static bool IsSafePackageName(string name)
    {
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

        return !((deviceName.Length == 4 &&
                  (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                   deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                  deviceName[3] is >= '1' and <= '9'));
    }

    private static bool IsNormalizedRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path))
            return false;

        var segments = path.Split('/');
        return segments.All(segment =>
            segment.Length != 0 && segment is not ("." or "..") &&
            !segment.EndsWith('.') && !segment.EndsWith(' ') &&
            !segment.Any(char.IsControl) && !segment.Contains(':'));
    }

    private static bool IsValidModuleName(string module) =>
        module.Length != 0 && module.Split('.').All(IsIdentifier);

    private static bool IsIdentifier(string identifier)
    {
        if (identifier.Length == 0 || !(char.IsLetter(identifier[0]) || identifier[0] == '_'))
            return false;
        return identifier.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static bool IsKey(string key) =>
        key.Length != 0 && key[0] is >= 'a' and <= 'z' &&
        key.Skip(1).All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');

    private static string StripComment(string line)
    {
        var insideString = false;
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] == '"')
                insideString = !insideString;
            else if (line[index] == '#' && !insideString)
                return line[..index];
        }

        return line;
    }

    private static bool IsAsciiAlphaNumeric(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool HasReparsePointWithin(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (HasReparsePoint(current))
                return true;
        }

        return false;
    }

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static Diagnostic AtStart(string code, string message, string file) =>
        new(code, message, file, new Range(1, 1, 1, 1));

    private static Diagnostic AtLine(string code, string message, string file, int line) =>
        new(code, message, file, new Range(line, 1, line, 2));

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;
}
