using System.Collections.Frozen;
using System.Text;

internal sealed record PackageManifest(
    string Name,
    string Version,
    string Kind,
    string SourceRoot,
    string? EntryModule,
    string? SqlitePath,
    string? SqliteSchema,
    IReadOnlySet<string> Capabilities,
    IReadOnlyList<PackageDependency> Dependencies)
{
    public bool IsLibrary => Kind == "lib";
}

internal sealed record PackageDependency(string Alias, string Path);

internal sealed record PackageSource(string Module, string File, string Text);

internal sealed record LoadedPackage(
    string Root,
    string ManifestFile,
    string SourceDirectory,
    PackageManifest Manifest,
    string ManifestText,
    IReadOnlyList<PackageSource> Sources,
    WebDatabaseOptions? WebDatabaseOptions);

internal sealed record WebDatabaseOptions(
    string RelativePath,
    string SchemaPath,
    string SchemaText);

internal sealed record PackageLoadResult(LoadedPackage? Package, List<Diagnostic> Diagnostics);

internal sealed record ResolvedPackage(
    string Id,
    string RelativePath,
    LoadedPackage Package,
    IReadOnlyDictionary<string, string> DependencyIds);

internal sealed record PackageDependencyGraph(
    ResolvedPackage Root,
    IReadOnlyList<ResolvedPackage> Nodes,
    IReadOnlyDictionary<string, ResolvedPackage> ById);

internal sealed record PackageGraphResult(PackageDependencyGraph? Graph, List<Diagnostic> Diagnostics);

internal static class PortablePackagePath
{
    private const string InvalidSegmentCharacters = "<>:\"/\\|?*";
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL"
    };

    public static bool IsValidRelativeSegment(string segment)
    {
        if (segment.Length == 0 || segment == ".")
            return false;
        if (segment == "..")
            return true;
        if (segment.EndsWith('.') || segment.EndsWith(' ') ||
            segment.Any(character => char.IsControl(character) || InvalidSegmentCharacters.Contains(character)))
            return false;

        return !IsReservedDeviceName(segment);
    }

    private static bool IsReservedDeviceName(string segment)
    {
        var deviceName = segment.Split('.')[0].TrimEnd(' ');
        if (ReservedDeviceNames.Contains(deviceName))
            return true;

        return deviceName.Length == 4 &&
               (deviceName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                deviceName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
               deviceName[3] is >= '1' and <= '9';
    }
}

internal static class PackageLoader
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.Ordinal)
    {
        "name",
        "version",
        "kind",
        "source_root",
        "entry_module",
        "sqlite_path",
        "sqlite_schema"
    };

    private static readonly HashSet<string> ReservedDependencyAliases = new(StringComparer.Ordinal)
    {
        "await", "false", "if", "match", "null", "self", "true", "with"
    };
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static PackageLoadResult Load(string packageDirectory)
    {
        var diagnostics = new List<Diagnostic>();
        var root = Path.GetFullPath(packageDirectory);
        var manifestFile = Path.Combine(root, "lang.toml");

        if (Directory.Exists(root) && HasReparsePointOnPath(root))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "Package directories cannot be reached through a symbolic link or reparse point",
                manifestFile));
            return new PackageLoadResult(null, diagnostics);
        }

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

        var parsedManifest = ParseManifest(manifestText, manifestFile, diagnostics);
        ValidateManifest(parsedManifest.Values, parsedManifest.Capabilities, manifestFile, diagnostics);
        if (diagnostics.Count != 0)
            return new PackageLoadResult(null, diagnostics);

        var manifest = new PackageManifest(
            parsedManifest.Values["name"],
            parsedManifest.Values["version"],
            parsedManifest.Values["kind"],
            parsedManifest.Values["source_root"],
            parsedManifest.Values.GetValueOrDefault("entry_module"),
            parsedManifest.Values.GetValueOrDefault("sqlite_path"),
            parsedManifest.Values.GetValueOrDefault("sqlite_schema"),
            parsedManifest.Capabilities,
            parsedManifest.Dependencies);

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

        var webDatabaseOptions = LoadWebDatabaseOptions(root, manifest, manifestFile, diagnostics);
        if (diagnostics.Count != 0)
            return new PackageLoadResult(null, diagnostics);

        var sources = DiscoverSources(root, sourceDirectory, manifestFile, diagnostics);
        if (diagnostics.Count != 0)
            return new PackageLoadResult(null, diagnostics);

        if (webDatabaseOptions is not null)
        {
            var databaseFile = ResolvePackageFilePath(root, webDatabaseOptions.RelativePath);
            var schemaFile = ResolvePackageFilePath(root, webDatabaseOptions.SchemaPath);
            var packageInputs = sources.Select(source => source.File)
                .Append(manifestFile)
                .Append(Path.Combine(root, "lang.lock"));
            if (string.Equals(databaseFile, schemaFile, PathComparison) ||
                packageInputs.Any(path => string.Equals(databaseFile, path, PathComparison)))
            {
                diagnostics.Add(AtStart(
                    "E_MANIFEST",
                    "sqlite_path cannot overwrite the manifest, lock, schema, or a language source file",
                    manifestFile));
                return new PackageLoadResult(null, diagnostics);
            }

            if (packageInputs.Any(path => string.Equals(schemaFile, path, PathComparison)))
            {
                diagnostics.Add(AtStart(
                    "E_MANIFEST",
                    "sqlite_schema must be separate from the manifest, lock, and language source files",
                    manifestFile));
                return new PackageLoadResult(null, diagnostics);
            }
        }

        return new PackageLoadResult(
            new LoadedPackage(root, manifestFile, sourceDirectory, manifest, manifestText, sources, webDatabaseOptions),
            diagnostics);
    }

    public static PackageGraphResult ResolveGraph(string packageDirectory)
    {
        PackageLoadResult rootLoad;
        try
        {
            rootLoad = Load(packageDirectory);
        }
        catch (Exception error) when (IsFileError(error))
        {
            return new PackageGraphResult(null,
            [
                AtStart("E_IO", $"Could not load package directory: {error.Message}", packageDirectory)
            ]);
        }

        if (rootLoad.Diagnostics.Count != 0 || rootLoad.Package is null)
            return new PackageGraphResult(null, rootLoad.Diagnostics);

        var root = rootLoad.Package;
        var diagnostics = new List<Diagnostic>();
        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var completed = new Dictionary<string, ResolvedPackage>(pathComparer);
        var loadedByPath = new Dictionary<string, LoadedPackage>(pathComparer)
        {
            [root.Root] = root
        };
        var active = new List<(LoadedPackage Package, string? ViaAlias)>();
        var identities = new Dictionary<(string Name, string Version), string>();
        identities[(root.Manifest.Name, root.Manifest.Version)] = root.Root;

        ResolvedPackage? Visit(LoadedPackage package, string? viaAlias, string edgeFile)
        {
            var activeIndex = active.FindIndex(frame => pathComparer.Equals(frame.Package.Root, package.Root));
            if (activeIndex >= 0)
            {
                var chain = new List<string> { active[activeIndex].Package.Manifest.Name };
                for (var index = activeIndex + 1; index < active.Count; index++)
                {
                    chain.Add($"alias '{active[index].ViaAlias}'");
                    chain.Add(active[index].Package.Manifest.Name);
                }

                chain.Add($"alias '{viaAlias}'");
                chain.Add(package.Manifest.Name);
                diagnostics.Add(AtStart(
                    "E_DEPENDENCY",
                    $"Dependency cycle: {string.Join(" -> ", chain)}",
                    edgeFile));
                return null;
            }

            if (completed.TryGetValue(package.Root, out var existing))
                return existing;

            active.Add((package, viaAlias));
            var dependencyIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var dependency in package.Manifest.Dependencies.OrderBy(item => item.Alias, StringComparer.Ordinal))
            {
                string targetRoot;
                try
                {
                    targetRoot = Path.GetFullPath(Path.Combine(
                        package.Root,
                        dependency.Path.Replace('/', Path.DirectorySeparatorChar)));
                    if (HasReparsePointOnPath(targetRoot))
                    {
                        diagnostics.Add(AtStart(
                            "E_DEPENDENCY",
                            $"Dependency '{dependency.Alias}' resolves through a symbolic link or reparse point",
                            package.ManifestFile));
                        continue;
                    }
                }
                catch (Exception error) when (IsFileError(error))
                {
                    diagnostics.Add(AtStart(
                        "E_DEPENDENCY",
                        $"Could not resolve dependency '{dependency.Alias}': {error.Message}",
                        package.ManifestFile));
                    continue;
                }

                if (!Directory.Exists(targetRoot))
                {
                    diagnostics.Add(AtStart(
                        "E_DEPENDENCY",
                        $"Dependency '{dependency.Alias}' directory '{dependency.Path}' does not exist",
                        package.ManifestFile));
                    continue;
                }

                if (!loadedByPath.TryGetValue(targetRoot, out var childPackage))
                {
                    var childManifest = Path.Combine(targetRoot, "lang.toml");
                    if (!File.Exists(childManifest))
                    {
                        diagnostics.Add(AtStart(
                            "E_DEPENDENCY",
                            $"Dependency '{dependency.Alias}' directory '{dependency.Path}' has no lang.toml manifest",
                            package.ManifestFile));
                        continue;
                    }

                    PackageLoadResult childLoad;
                    try
                    {
                        childLoad = Load(targetRoot);
                    }
                    catch (Exception error) when (IsFileError(error))
                    {
                        diagnostics.Add(AtStart(
                            "E_DEPENDENCY",
                            $"Could not load dependency '{dependency.Alias}': {error.Message}",
                            package.ManifestFile));
                        continue;
                    }

                    if (childLoad.Diagnostics.Count != 0 || childLoad.Package is null)
                    {
                        diagnostics.AddRange(childLoad.Diagnostics);
                        continue;
                    }

                    childPackage = childLoad.Package;
                    loadedByPath.Add(childPackage.Root, childPackage);
                }

                if (!childPackage.Manifest.IsLibrary)
                {
                    diagnostics.Add(AtStart(
                        "E_DEPENDENCY",
                        $"Dependency '{dependency.Alias}' must point to a package with kind = \"lib\"",
                        package.ManifestFile));
                    continue;
                }

                var identity = (childPackage.Manifest.Name, childPackage.Manifest.Version);
                if (identities.TryGetValue(identity, out var otherRoot) && !pathComparer.Equals(otherRoot, childPackage.Root))
                {
                    diagnostics.Add(AtStart(
                        "E_DEPENDENCY",
                        $"Package '{identity.Name}' version '{identity.Version}' resolves from multiple package roots",
                        package.ManifestFile));
                    continue;
                }

                identities[identity] = childPackage.Root;
                var child = Visit(childPackage, dependency.Alias, package.ManifestFile);
                if (child is not null)
                    dependencyIds.Add(dependency.Alias, child.Id);
            }

            active.RemoveAt(active.Count - 1);
            var resolved = new ResolvedPackage(
                package.Root,
                NormalizeRelative(Path.GetRelativePath(root.Root, package.Root)),
                package,
                dependencyIds);
            completed.Add(package.Root, resolved);
            return resolved;
        }

        var resolvedRoot = Visit(root, null, root.ManifestFile);
        if (diagnostics.Count != 0 || resolvedRoot is null)
            return new PackageGraphResult(null, diagnostics);

        var nodes = completed.Values
            .OrderBy(node => node.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var byId = new Dictionary<string, ResolvedPackage>(StringComparer.Ordinal);
        foreach (var node in nodes)
            byId.Add(node.Id, node);

        return new PackageGraphResult(
            new PackageDependencyGraph(resolvedRoot, nodes, byId),
            diagnostics);
    }

    public static Diagnostic ModulePathError(string message, string file) =>
        AtStart("E_MODULE_PATH", message, file);

    private sealed record ParsedManifest(
        Dictionary<string, string> Values,
        IReadOnlyList<PackageDependency> Dependencies,
        IReadOnlySet<string> Capabilities);

    private static ParsedManifest ParseManifest(
        string text,
        string file,
        List<Diagnostic> diagnostics)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var dependencies = new List<PackageDependency>();
        var capabilities = new HashSet<string>(StringComparer.Ordinal);
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inCapabilities = false;
        var inDependencies = false;
        var capabilitiesSeen = false;
        var dependenciesSeen = false;
        using var reader = new StringReader(text);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = StripComment(line).Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            if (trimmed.StartsWith('['))
            {
                if (trimmed == "[capabilities]" && !capabilitiesSeen && !dependenciesSeen)
                {
                    inCapabilities = true;
                    inDependencies = false;
                    capabilitiesSeen = true;
                    continue;
                }

                if (trimmed == "[dependencies]" && !dependenciesSeen)
                {
                    inCapabilities = false;
                    inDependencies = true;
                    dependenciesSeen = true;
                    continue;
                }

                var sectionMessage = trimmed == "[dependencies]"
                    ? "The [dependencies] section may appear only once"
                    : trimmed == "[capabilities]"
                        ? "The [capabilities] section may appear once before [dependencies]"
                    : $"Unknown manifest section '{trimmed}'";
                diagnostics.Add(AtLine("E_MANIFEST", sectionMessage, file, lineNumber));
                continue;
            }

            if (dependenciesSeen && !inDependencies)
            {
                diagnostics.Add(AtLine("E_MANIFEST", "No manifest assignments may follow the [dependencies] section", file, lineNumber));
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals <= 0)
            {
                diagnostics.Add(AtLine(
                    "E_MANIFEST",
                    inDependencies
                        ? "Expected a dependency alias = \"relative/path\" assignment"
                        : "Expected a simple key = \"value\" assignment",
                    file,
                    lineNumber));
                continue;
            }

            var key = trimmed[..equals].Trim();
            var rawValue = trimmed[(equals + 1)..].Trim();
            if (inCapabilities)
            {
                if (key is not ("fs.read" or "fs.write" or "net.listen" or "db.read" or "db.write"))
                {
                    diagnostics.Add(AtLine("E_MANIFEST", $"Unknown capability '{key}'", file, lineNumber));
                    continue;
                }

                if (!capabilities.Add(key))
                {
                    diagnostics.Add(AtLine("E_MANIFEST", $"Duplicate capability '{key}'", file, lineNumber));
                    continue;
                }

                if (!TryReadStringValue(rawValue, out var grant) || grant != "allow")
                {
                    diagnostics.Add(AtLine("E_MANIFEST", $"Capability '{key}' must be assigned the value \"allow\"", file, lineNumber));
                    continue;
                }

                continue;
            }

            if (inDependencies)
            {
                if (!IsDependencyAlias(key))
                {
                    diagnostics.Add(AtLine("E_MANIFEST", "Dependency aliases must be importable language identifiers", file, lineNumber));
                    continue;
                }

                if (!aliases.Add(key))
                {
                    diagnostics.Add(AtLine(
                        "E_MANIFEST",
                        $"Duplicate dependency alias '{key}' (aliases are case-insensitive)",
                        file,
                        lineNumber));
                    continue;
                }

                if (!TryReadStringValue(rawValue, out var dependencyPath))
                {
                    diagnostics.Add(AtLine(
                        "E_MANIFEST",
                        $"Value for dependency '{key}' must be a simple double-quoted string without escapes or control characters",
                        file,
                        lineNumber));
                    continue;
                }

                if (!IsDependencyRelativePath(dependencyPath))
                {
                    diagnostics.Add(AtLine(
                        "E_MANIFEST",
                        $"Dependency path for '{key}' must be relative, use forward slashes, and contain no empty or dot segments",
                        file,
                        lineNumber));
                    continue;
                }

                dependencies.Add(new PackageDependency(key, dependencyPath));
                continue;
            }

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

            if (!TryReadStringValue(rawValue, out var value))
            {
                diagnostics.Add(AtLine(
                    "E_MANIFEST",
                    $"Value for '{key}' must be a simple double-quoted string",
                    file,
                    lineNumber));
                continue;
            }

            values.Add(key, value);
        }

        return new ParsedManifest(values, dependencies, capabilities.ToFrozenSet(StringComparer.Ordinal));
    }

    private static void ValidateManifest(
        IReadOnlyDictionary<string, string> values,
        IReadOnlySet<string> capabilities,
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

        if (values.TryGetValue("kind", out var kind) && kind is not ("lib" or "cli" or "web"))
            diagnostics.Add(AtStart("E_MANIFEST", "kind must be \"lib\", \"cli\", or \"web\"", file));

        if (values.TryGetValue("source_root", out var sourceRoot) && !IsNormalizedRelativePath(sourceRoot))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "source_root must be a normalized relative path using forward slashes and cannot escape the package root",
                file));
        }

        var hasSqlitePath = values.TryGetValue("sqlite_path", out var sqlitePath);
        var hasSqliteSchema = values.TryGetValue("sqlite_schema", out var sqliteSchema);
        if (hasSqlitePath != hasSqliteSchema)
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "sqlite_path and sqlite_schema must be declared together",
                file));
        }

        if (hasSqlitePath && !IsNormalizedPackageRelativeFilePath(sqlitePath!))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "sqlite_path must be a normalized package-relative file path using forward slashes and cannot escape the package root",
                file));
        }

        if (hasSqliteSchema && !IsNormalizedPackageRelativeFilePath(sqliteSchema!))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                "sqlite_schema must be a normalized package-relative file path using forward slashes and cannot escape the package root",
                file));
        }

        var hasDatabaseConfig = hasSqlitePath && hasSqliteSchema;
        var hasDatabaseGrant = capabilities.Contains("db.read") || capabilities.Contains("db.write");
        if (hasDatabaseGrant && !hasDatabaseConfig)
        {
            diagnostics.Add(AtStart(
                "E_CAPABILITY_MISSING",
                "The db.read and db.write capabilities require sqlite_path and sqlite_schema configuration",
                file));
        }

        var hasEntryModule = values.TryGetValue("entry_module", out var entryModule);
        if (values.TryGetValue("kind", out kind))
        {
            if ((kind is "cli" or "web") && !hasEntryModule)
                diagnostics.Add(AtStart("E_MANIFEST", $"{(kind == "web" ? "Web" : "CLI")} packages require entry_module", file));
            else if (kind == "lib" && hasEntryModule)
                diagnostics.Add(AtStart("E_MANIFEST", "Library packages must not declare entry_module", file));

            if (kind == "lib" && capabilities.Count != 0)
                diagnostics.Add(AtStart("E_MANIFEST", "Library packages cannot declare capabilities", file));

            if (kind == "cli" && capabilities.Contains("net.listen"))
                diagnostics.Add(AtStart("E_MANIFEST", "The net.listen capability is only valid for web packages", file));

            if (kind != "web" && (hasDatabaseConfig || hasDatabaseGrant))
            {
                diagnostics.Add(AtStart(
                    "E_MANIFEST",
                    "SQLite configuration and db.read/db.write capabilities are only valid for web packages",
                    file));
            }

            if (kind == "web" && !capabilities.Contains("net.listen"))
                diagnostics.Add(AtStart(
                    "E_CAPABILITY_MISSING",
                    "Web packages require the root package's net.listen capability grant",
                    file));

            if (kind == "web" && hasSqliteSchema && !capabilities.Contains("db.write"))
            {
                diagnostics.Add(AtStart(
                    "E_CAPABILITY_MISSING",
                    "Web packages that declare sqlite_schema require the root package's db.write capability grant",
                    file));
            }
        }

        if (hasEntryModule && !IsValidModuleName(entryModule!))
            diagnostics.Add(AtStart("E_MANIFEST", "entry_module must be a valid module path using '::' separators", file));
    }

    private static WebDatabaseOptions? LoadWebDatabaseOptions(
        string root,
        PackageManifest manifest,
        string manifestFile,
        List<Diagnostic> diagnostics)
    {
        if (manifest.SqlitePath is null || manifest.SqliteSchema is null)
            return null;

        string databaseFile;
        string schemaFile;
        try
        {
            databaseFile = ResolvePackageFilePath(root, manifest.SqlitePath);
            schemaFile = ResolvePackageFilePath(root, manifest.SqliteSchema);
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_MANIFEST", $"Could not resolve SQLite package paths: {error.Message}", manifestFile));
            return null;
        }

        if (!IsWithin(root, databaseFile) || string.Equals(root, databaseFile, PathComparison))
        {
            diagnostics.Add(AtStart("E_MANIFEST", "sqlite_path must resolve to a file inside the package root", manifestFile));
            return null;
        }

        if (!IsWithin(root, schemaFile) || string.Equals(root, schemaFile, PathComparison))
        {
            diagnostics.Add(AtStart("E_MANIFEST", "sqlite_schema must resolve to a file inside the package root", manifestFile));
            return null;
        }

        try
        {
            if (HasReparsePointOnPath(databaseFile))
            {
                diagnostics.Add(AtStart("E_MANIFEST", "sqlite_path cannot resolve through a symbolic link or reparse point", manifestFile));
                return null;
            }

            if (Directory.Exists(databaseFile))
            {
                diagnostics.Add(AtStart("E_MANIFEST", "sqlite_path must name a file, not a directory", manifestFile));
                return null;
            }

            if (HasReparsePointOnPath(schemaFile))
            {
                diagnostics.Add(AtStart("E_MANIFEST", "sqlite_schema cannot resolve through a symbolic link or reparse point", manifestFile));
                return null;
            }
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_IO", $"Could not inspect SQLite package paths: {error.Message}", manifestFile));
            return null;
        }

        if (!File.Exists(schemaFile) || Directory.Exists(schemaFile))
        {
            diagnostics.Add(AtStart(
                "E_MANIFEST",
                $"sqlite_schema file '{manifest.SqliteSchema}' does not exist",
                manifestFile));
            return null;
        }

        string schemaText;
        try
        {
            schemaText = File.ReadAllText(schemaFile, StrictUtf8);
        }
        catch (DecoderFallbackException error)
        {
            diagnostics.Add(AtStart("E_MANIFEST", $"SQLite schema is not valid UTF-8: {error.Message}", schemaFile));
            return null;
        }
        catch (Exception error) when (IsFileError(error))
        {
            diagnostics.Add(AtStart("E_IO", $"Could not read SQLite schema: {error.Message}", schemaFile));
            return null;
        }

        schemaText = schemaText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return new WebDatabaseOptions(manifest.SqlitePath, manifest.SqliteSchema, schemaText);
    }

    private static string ResolvePackageFilePath(string root, string relativePath) =>
        Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));

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

            var module = string.Join("::", segments);

            if (!modules.Add(module))
            {
                diagnostics.Add(ModulePathError(
                    $"Multiple source files map to module '{module}'",
                    item.File));
                continue;
            }

            string sourceText;
            try
            {
                sourceText = File.ReadAllText(item.File, StrictUtf8);
            }
            catch (DecoderFallbackException error)
            {
                diagnostics.Add(AtStart("E_IO", $"Source file is not valid UTF-8: {error.Message}", item.File));
                continue;
            }
            catch (Exception error) when (IsFileError(error))
            {
                diagnostics.Add(AtStart($"E_IO", $"Could not read source file: {error.Message}", item.File));
                continue;
            }

            sources.Add(new PackageSource(module, item.File, sourceText));
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

    private static bool IsNormalizedPackageRelativeFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || Path.IsPathRooted(path))
            return false;

        return path.Split('/').All(segment =>
            segment != ".." && PortablePackagePath.IsValidRelativeSegment(segment));
    }

    private static bool IsDependencyRelativePath(string path)
    {
        if (path.Length == 0 || path[0] == '/' || path.Contains('\\') || Path.IsPathRooted(path))
            return false;

        return path.Split('/').All(PortablePackagePath.IsValidRelativeSegment);
    }

    private static bool TryReadStringValue(string rawValue, out string value)
    {
        value = string.Empty;
        if (rawValue.Length < 2 || rawValue[0] != '"' || rawValue[^1] != '"')
            return false;

        value = rawValue[1..^1];
        return !value.Contains('"') && !value.Contains('\\') && !value.Any(char.IsControl);
    }

    private static bool IsValidModuleName(string module) =>
        module.Length != 0 && module.Split("::", StringSplitOptions.None).All(IsIdentifier);

    private static bool IsIdentifier(string identifier)
    {
        if (identifier.Length == 0 || !(char.IsLetter(identifier[0]) || identifier[0] == '_'))
            return false;
        return identifier.Skip(1).All(character => char.IsLetterOrDigit(character) || character == '_');
    }

    private static bool IsDependencyAlias(string identifier) =>
        IsIdentifier(identifier) && !ReservedDependencyAliases.Contains(identifier);

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

    private static bool HasReparsePointOnPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath) ?? throw new ArgumentException("Path has no filesystem root", nameof(path));
        var current = pathRoot;
        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            return true;

        foreach (var segment in fullPath[pathRoot.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                return false;
            if (HasReparsePoint(current))
                return true;
        }

        return false;
    }

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
