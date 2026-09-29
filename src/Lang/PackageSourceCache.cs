using System.Diagnostics;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal enum PackageResolutionMode
{
    Offline,
    AllowFetch
}

internal enum PackageDependencyKind
{
    Path,
    Git
}

internal enum PackageSourceKind
{
    Root,
    Path,
    Git
}

internal sealed record PackageDependencySpec(
    PackageDependencyKind Kind,
    string? Path,
    string? Url,
    string? Commit)
{
    private const string GitPrefix = "git+";

    public static PackageDependencySpec ForPath(string path) =>
        new(PackageDependencyKind.Path, path, null, null);

    public static PackageDependencySpec ForGit(string url, string commit) =>
        new(PackageDependencyKind.Git, null, url, commit);

    public string FormatManifest() => Kind switch
    {
        PackageDependencyKind.Path => Path!,
        PackageDependencyKind.Git => $"{GitPrefix}{Url}#{Commit}",
        _ => throw new InvalidOperationException("Unknown package dependency kind")
    };

    public static bool TryParseManifest(
        string value,
        out PackageDependencySpec? spec,
        out string error)
    {
        spec = null;
        error = string.Empty;
        if (!value.StartsWith(GitPrefix, StringComparison.Ordinal))
        {
            if (!IsValidRelativePath(value))
            {
                error = "Dependency path must be relative, use forward slashes, and contain no empty or dot segments other than '..'";
                return false;
            }

            spec = ForPath(value);
            return true;
        }

        var separator = value.LastIndexOf('#');
        if (separator <= GitPrefix.Length || separator == value.Length - 1 ||
            value.IndexOf('#', GitPrefix.Length, separator - GitPrefix.Length) >= 0)
        {
            error = "Git dependency must use git+<absolute-https-or-file-url>#<40-lowercase-hex-commit>";
            return false;
        }

        var urlText = value[GitPrefix.Length..separator];
        var commit = value[(separator + 1)..];
        if (!IsCommit(commit))
        {
            error = "Git dependency commit must be exactly 40 lowercase hexadecimal characters; branches, tags, and short commits are not supported";
            return false;
        }

        if (urlText.Contains('#') || urlText.Contains('?') || urlText.Any(char.IsControl) || urlText.Contains('\\') || HasUserInformation(urlText) ||
            !Uri.TryCreate(urlText, UriKind.Absolute, out var uri) ||
            uri is null || uri.UserInfo.Length != 0 ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeFile) ||
            (uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.Host)) ||
            uri.Fragment.Length != 0)
        {
            error = "Git dependency URL must be an absolute https or file URL without user information, a query, or a fragment";
            return false;
        }

        var canonicalUrl = uri.AbsoluteUri;
        spec = ForGit(canonicalUrl, commit);
        return true;
    }

    internal static bool IsCommit(string value) =>
        value.Length == 40 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal static bool IsValidRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] == '/' || path.Contains('\\') || System.IO.Path.IsPathRooted(path))
            return false;

        return path.Split('/').All(PortablePackagePath.IsValidRelativeSegment);
    }

    private static bool HasUserInformation(string url)
    {
        var authorityMarker = url.IndexOf("://", StringComparison.OrdinalIgnoreCase);
        if (authorityMarker < 0)
            return false;

        var authorityStart = authorityMarker + 3;
        var authorityEnd = url.IndexOfAny(['/', '?'], authorityStart);
        if (authorityEnd < 0)
            authorityEnd = url.Length;
        return url.AsSpan(authorityStart, authorityEnd - authorityStart).Contains('@');
    }
}

internal sealed record PackageSourceIdentity(
    PackageSourceKind Kind,
    string? Path = null,
    string? Url = null,
    string? Commit = null)
{
    public static PackageSourceIdentity Root { get; } = new(PackageSourceKind.Root);
    public static PackageSourceIdentity ForPath(string path) => new(PackageSourceKind.Path, Path: path);
    public static PackageSourceIdentity ForGit(string url, string commit) =>
        new(PackageSourceKind.Git, Url: url, Commit: commit);
}

internal sealed record PackageDependencyLoadResult(
    LoadedPackage? Package,
    PackageSourceIdentity? SourceIdentity,
    List<Diagnostic> Diagnostics);

internal sealed record PackageCacheResolution(string? PackageRoot, string StablePath, string? Error)
{
    public bool Success => PackageRoot is not null && Error is null;
}

/// <summary>
/// Provider boundary for pinned Git package materialization. Dependency graph resolution
/// reaches this class only for Git specs; Offline verifies local entries and never starts Git,
/// while AllowFetch permits the bounded Git subprocess used to populate a cache entry.
/// </summary>
internal sealed class PackageSourceCache
{
    private static readonly byte[] CacheKeyDomain = Encoding.UTF8.GetBytes("LANG-GIT-SOURCE\0v1");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private const int MaximumGitOutputCharacters = 64 * 1024 * 1024;
    private const int MaximumGitErrorCharacters = 2 * 1024 * 1024;
    private const int MaximumCheckoutFiles = 100_000;
    private const long MaximumCheckoutFileBytes = 256L * 1024 * 1024;
    private const long MaximumCheckoutTotalBytes = 2L * 1024 * 1024 * 1024;
    private const string AttestationFileName = "attestation.json";
    private readonly string _root;

    public PackageSourceCache()
    {
        var configured = Environment.GetEnvironmentVariable("LANG_PACKAGE_CACHE");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            _root = Path.GetFullPath(configured);
        }
        else
        {
            var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
                throw new InvalidOperationException("Local application data is unavailable; set LANG_PACKAGE_CACHE to choose a package cache directory");
            _root = Path.GetFullPath(Path.Combine(localApplicationData, "lang", "packages"));
        }
    }

    public string Root => _root;

    public string? ValidateCacheRootForPackage(string packageDirectory)
    {
        var packageRoot = Path.GetFullPath(packageDirectory);
        if (IsWithinOrEqual(packageRoot, _root))
            return "LANG_PACKAGE_CACHE must not be equal to or inside the package directory or its source_root";
        return null;
    }

    public static string StableGitPath(string url, string commit)
    {
        return $"git/{StableGitDigest(url, commit)}";
    }

    public static string StableGitPackagePath(string url, string commit) =>
        $"git:{StableGitDigest(url, commit)}";

    private static string StableGitDigest(string url, string commit)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(CacheKeyDomain);
        AppendField(hash, url);
        AppendField(hash, commit);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public bool TryGetStablePath(string packageRoot, out string stablePath)
    {
        var fullPath = Path.GetFullPath(packageRoot);
        if (!IsWithinOrEqual(_root, fullPath))
        {
            stablePath = string.Empty;
            return false;
        }

        var relative = Path.GetRelativePath(_root, fullPath);
        stablePath = NormalizeRelative(relative);
        return relative != ".";
    }

    public PackageCacheResolution ResolveGit(PackageDependencySpec spec, PackageResolutionMode mode)
    {
        if (spec.Kind != PackageDependencyKind.Git || spec.Url is null || spec.Commit is null)
            return new PackageCacheResolution(null, string.Empty, "Dependency source is not an exact-commit Git dependency");

        var stablePath = StableGitPackagePath(spec.Url, spec.Commit);
        var cachePath = StableGitPath(spec.Url, spec.Commit);
        string target;
        try
        {
            target = Path.GetFullPath(Path.Combine(_root, cachePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(_root, target))
                return new PackageCacheResolution(null, stablePath, "Git cache key escaped the configured package cache");
            EnsureNoReparsePointOnPath(_root);
            if (mode == PackageResolutionMode.Offline)
            {
                if (Directory.Exists(target) || File.Exists(target))
                    return ValidateExisting(target, stablePath, spec.Url, spec.Commit);

                return new PackageCacheResolution(
                    null,
                    stablePath,
                    "Pinned Git dependency is not in the package cache; run 'lang lock' to fetch it");
            }

            return Populate(target, stablePath, spec.Url, spec.Commit);
        }
        catch (Exception error) when (IsFileError(error) || error is InvalidOperationException or TimeoutException or DecoderFallbackException)
        {
            return new PackageCacheResolution(null, stablePath, $"Could not resolve pinned Git dependency: {SafeOperationalError(error)}");
        }
    }

    private PackageCacheResolution ValidateExisting(string target, string stablePath, string url, string commit)
    {
        if (!Directory.Exists(target) || IsReparsePoint(target))
            return new PackageCacheResolution(null, stablePath, "Git cache entry is not a real directory");

        try
        {
            if (!TryInspectExistingTree(target, out var sourceRoot, out var attestationFile, out var contentHash, out var error))
                return new PackageCacheResolution(null, stablePath, error);

            var attestation = ReadAttestation(attestationFile);
            if (attestation is null || attestation.Url != url || attestation.Commit != commit)
                return new PackageCacheResolution(null, stablePath, "Git cache entry attestation does not match the pinned URL and commit");

            if (!string.Equals(attestation.ContentHash, contentHash, StringComparison.Ordinal))
                return new PackageCacheResolution(null, stablePath, "Git cache entry content does not match its attestation");

            return new PackageCacheResolution(sourceRoot, stablePath, null);
        }
        catch (Exception error) when (IsFileError(error) || error is InvalidOperationException or DecoderFallbackException or JsonException)
        {
            return new PackageCacheResolution(null, stablePath, "Git cache entry is unsafe or invalid");
        }
    }

    private static bool TryInspectExistingTree(
        string target,
        out string sourceRoot,
        out string attestationFile,
        out string contentHash,
        out string error)
    {
        sourceRoot = Path.Combine(target, "source");
        attestationFile = Path.Combine(target, AttestationFileName);
        contentHash = string.Empty;
        error = string.Empty;
        if (!Directory.Exists(target) || IsReparsePoint(target))
        {
            error = "Git cache entry is not a real directory";
            return false;
        }

        EnsureNoReparsePointOnPath(target);
        var entries = Directory.EnumerateFileSystemEntries(target).ToArray();
        if (entries.Length != 2 || !Directory.Exists(sourceRoot) || IsReparsePoint(sourceRoot) ||
            !File.Exists(attestationFile) || IsReparsePoint(attestationFile) ||
            entries.Any(entry => Path.GetFileName(entry) is not ("source" or AttestationFileName)))
        {
            error = "Git cache entry has an invalid attestation layout";
            return false;
        }

        var files = ValidateCheckoutTree(sourceRoot);
        if (!File.Exists(Path.Combine(sourceRoot, "lang.toml")))
        {
            error = "Git cache entry has no root lang.toml manifest";
            return false;
        }

        contentHash = HashCheckoutTree(files);
        return true;
    }

    private PackageCacheResolution Populate(string target, string stablePath, string url, string commit)
    {
        var gitDirectory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(gitDirectory);
        EnsureNoReparsePointOnPath(gitDirectory);

        var temporaryRoot = Path.Combine(gitDirectory, $"t-{Guid.NewGuid():N}");
        var checkoutRoot = Path.Combine(temporaryRoot, "source");
        var templateRoot = Path.Combine(temporaryRoot, "templates");
        var hooksRoot = Path.Combine(temporaryRoot, "hooks");
        var isolatedHome = Path.Combine(temporaryRoot, "home");
        var isolatedConfig = Path.Combine(temporaryRoot, "global.gitconfig");
        try
        {
            Directory.CreateDirectory(temporaryRoot);
            Directory.CreateDirectory(templateRoot);
            Directory.CreateDirectory(hooksRoot);
            Directory.CreateDirectory(isolatedHome);
            Directory.CreateDirectory(Path.Combine(isolatedHome, ".config"));
            File.WriteAllBytes(isolatedConfig, []);
            RunGit(null, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["init", "--quiet", $"--template={templateRoot}", checkoutRoot]);
            RunGit(checkoutRoot, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["remote", "add", "origin", url]);
            RunGit(checkoutRoot, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["fetch", "--depth=1", "--filter=blob:limit=268435456", "--quiet", "--no-tags", "--no-recurse-submodules", "origin", commit]);
            ValidateGitMetadataSize(Path.Combine(checkoutRoot, ".git"));

            var tree = RunGit(checkoutRoot, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["ls-tree", "-r", "-l", "-z", "FETCH_HEAD"]);
            ValidateGitTree(tree);
            RunGit(checkoutRoot, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["checkout", "--quiet", "--detach", "FETCH_HEAD"]);
            var head = RunGit(checkoutRoot, templateRoot, hooksRoot, isolatedHome, isolatedConfig,
                ["rev-parse", "--verify", "HEAD"]).Trim();
            if (!string.Equals(head, commit, StringComparison.Ordinal))
                throw new InvalidOperationException("Git checkout HEAD does not match the requested commit");

            var gitMetadata = Path.Combine(checkoutRoot, ".git");
            if (Directory.Exists(gitMetadata))
                DeleteGitMetadata(checkoutRoot, gitMetadata);
            else if (File.Exists(gitMetadata))
                DeleteGitMetadata(checkoutRoot, gitMetadata);
            var files = ValidateCheckoutTree(checkoutRoot);
            var contentHash = HashCheckoutTree(files);
            WriteAttestation(Path.Combine(temporaryRoot, AttestationFileName), url, commit, contentHash);
            DeleteOwnedTree(temporaryRoot, templateRoot);
            DeleteOwnedTree(temporaryRoot, hooksRoot);
            DeleteOwnedTree(temporaryRoot, isolatedHome);
            DeleteOwnedTree(temporaryRoot, isolatedConfig);
            return PromoteOrVerifyExisting(target, temporaryRoot, stablePath, url, commit, contentHash);
        }
        catch (Exception error) when (IsFileError(error) || error is InvalidOperationException or TimeoutException or DecoderFallbackException or System.ComponentModel.Win32Exception)
        {
            return new PackageCacheResolution(null, stablePath, $"Could not populate pinned Git dependency: {SafeOperationalError(error)}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot) && !IsReparsePoint(temporaryRoot))
                    DeleteOwnedTree(gitDirectory, temporaryRoot);
            }
            catch (Exception error) when (IsFileError(error) || error is InvalidOperationException)
            {
                // Preserve the fetch or validation diagnostic if cleanup fails.
            }
        }
    }

    private PackageCacheResolution PromoteOrVerifyExisting(
        string target,
        string temporaryRoot,
        string stablePath,
        string url,
        string commit,
        string verifiedContentHash)
    {
        if (Directory.Exists(target) || File.Exists(target))
            return VerifyExistingAgainstFetchedSource(target, temporaryRoot, stablePath, url, commit, verifiedContentHash);

        try
        {
            Directory.Move(temporaryRoot, target);
        }
        catch (IOException) when (Directory.Exists(target) || File.Exists(target))
        {
            return VerifyExistingAgainstFetchedSource(target, temporaryRoot, stablePath, url, commit, verifiedContentHash);
        }

        return ValidateExisting(target, stablePath, url, commit);
    }

    private PackageCacheResolution VerifyExistingAgainstFetchedSource(
        string target,
        string temporaryRoot,
        string stablePath,
        string url,
        string commit,
        string verifiedContentHash)
    {
        if (!TryInspectExistingTree(target, out _, out var attestationFile, out var existingContentHash, out var error))
            return new PackageCacheResolution(null, stablePath, error);

        if (!string.Equals(existingContentHash, verifiedContentHash, StringComparison.Ordinal))
            return new PackageCacheResolution(
                null,
                stablePath,
                "Cached Git source differs from the freshly fetched pinned commit; the existing cache entry and lock were left unchanged");

        EnsureNoReparsePointOnPath(target);
        var stagedAttestation = Path.Combine(temporaryRoot, AttestationFileName);
        File.Move(stagedAttestation, attestationFile, overwrite: true);
        return ValidateExisting(target, stablePath, url, commit);
    }

    private static string RunGit(
        string? workingDirectory,
        string templateDirectory,
        string hooksDirectory,
        string isolatedHome,
        string isolatedGlobalConfig,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = StrictUtf8,
            StandardErrorEncoding = StrictUtf8
        };
        if (workingDirectory is not null)
            startInfo.WorkingDirectory = workingDirectory;
        foreach (var inheritedVariable in startInfo.Environment.Keys
                     .Where(key => key.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase) ||
                                   key.StartsWith("GCM_", StringComparison.OrdinalIgnoreCase) ||
                                   key.Equals("SSH_ASKPASS", StringComparison.OrdinalIgnoreCase) ||
                                   key.Equals("SSH_ASKPASS_REQUIRE", StringComparison.OrdinalIgnoreCase))
                     .ToArray())
            startInfo.Environment.Remove(inheritedVariable);
        startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = isolatedGlobalConfig;
        startInfo.Environment["GIT_TEMPLATE_DIR"] = templateDirectory;
        startInfo.Environment["HOME"] = isolatedHome;
        startInfo.Environment["XDG_CONFIG_HOME"] = Path.Combine(isolatedHome, ".config");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("protocol.allow=never");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("protocol.https.allow=always");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("protocol.file.allow=always");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add($"core.hooksPath={hooksDirectory}");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.askPass=");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("credential.helper=");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.longpaths=true");
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git");
        var standardOutput = ReadBoundedAsync(process.StandardOutput, MaximumGitOutputCharacters);
        var standardError = ReadBoundedAsync(process.StandardError, MaximumGitErrorCharacters);
        if (!process.WaitForExit(milliseconds: 120_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(milliseconds: 10_000);
            }
            catch (InvalidOperationException)
            {
                // The process exited while the timeout handler was stopping it.
            }

            throw new TimeoutException("git command exceeded the 120-second limit");
        }

        var output = standardOutput.GetAwaiter().GetResult();
        var errorOutput = standardError.GetAwaiter().GetResult();
        if (output.Truncated)
            throw new InvalidOperationException("git command output exceeded the 64 MiB limit");
        if (errorOutput.Truncated)
            throw new InvalidOperationException("git command error output exceeded the 2 MiB limit");
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {arguments[0]} failed with exit code {process.ExitCode}");

        _ = errorOutput;
        return output.Text;
    }

    private static async Task<BoundedOutput> ReadBoundedAsync(StreamReader reader, int maximumCharacters)
    {
        var text = new StringBuilder(Math.Min(maximumCharacters, 8192));
        var buffer = new char[8192];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) != 0)
        {
            var remaining = maximumCharacters - text.Length;
            if (remaining > 0)
                text.Append(buffer, 0, Math.Min(remaining, count));
            if (count > remaining)
                truncated = true;
        }

        return new BoundedOutput(text.ToString(), truncated);
    }

    private static void ValidateGitTree(string tree)
    {
        var fileCount = 0;
        long totalBytes = 0;
        foreach (var entry in tree.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0)
                throw new InvalidOperationException("Git returned a malformed tree entry");

            var header = entry[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length < 3)
                throw new InvalidOperationException("Git returned a malformed tree entry");
            if (header[0] == "120000")
                throw new InvalidOperationException("Git dependencies cannot contain symbolic links");
            if (header[0] == "160000" || header[1] == "commit")
                throw new InvalidOperationException("Git dependencies cannot contain submodules");
            if (header[1] != "blob" || header.Length < 4 ||
                !long.TryParse(header[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var fileBytes))
                throw new InvalidOperationException("Git tree contains a blob with an unknown file size");
            fileCount++;
            if (fileCount > MaximumCheckoutFiles)
                throw new InvalidOperationException($"Git checkout exceeds the {MaximumCheckoutFiles} file limit");
            if (fileBytes > MaximumCheckoutFileBytes)
                throw new InvalidOperationException("Git checkout contains a file larger than the 256 MiB limit");
            if (fileBytes < 0 || totalBytes > MaximumCheckoutTotalBytes - fileBytes)
                throw new InvalidOperationException("Git checkout exceeds the 2 GiB total file size limit");
            totalBytes += fileBytes;

            var path = entry[(tab + 1)..];
            ValidatePortableTreePath(path);
        }
    }

    private static void ValidateGitMetadataSize(string gitMetadata)
    {
        EnsureNoReparsePointOnPath(gitMetadata);
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(gitMetadata));
        long totalBytes = 0;
        var visited = 0;
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (IsReparsePoint(current))
                throw new InvalidOperationException("Git object database contains a reparse point");
            visited++;
            if (visited > 200_000)
                throw new InvalidOperationException("Git object database exceeds the 200,000 entry limit");
            if (Directory.Exists(current))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                    pending.Push(Path.GetFullPath(entry));
                continue;
            }

            if (!File.Exists(current))
                throw new InvalidOperationException("Git object database contains an unsupported filesystem entry");
            var length = new FileInfo(current).Length;
            if (length < 0 || totalBytes > MaximumCheckoutTotalBytes - length)
                throw new InvalidOperationException("Git object database exceeds the 2 GiB storage limit");
            totalBytes += length;
        }
    }

    private static void ValidatePortableTreePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Contains('\\') || Path.IsPathRooted(path))
            throw new InvalidOperationException("Git tree contains an invalid or rooted path");

        var segments = path.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".." ||
                                    segment.Equals(".git", StringComparison.OrdinalIgnoreCase) ||
                                    !PortablePackagePath.IsValidRelativeSegment(segment)))
            throw new InvalidOperationException("Git tree contains an invalid path segment or VCS metadata path");
    }

    private static List<(string RelativePath, string FullPath, long Length)> ValidateCheckoutTree(string root)
    {
        EnsureNoReparsePointOnPath(root);
        var fullRoot = Path.GetFullPath(root);
        var pending = new Stack<string>();
        var files = new List<(string RelativePath, string FullPath, long Length)>();
        var visited = 0;
        long totalBytes = 0;
        pending.Push(fullRoot);
        while (pending.Count != 0)
        {
            var directory = pending.Pop();
            visited++;
            if (visited > 200_000)
                throw new InvalidOperationException("Git checkout exceeds the 200,000 filesystem entry limit");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var fullEntry = Path.GetFullPath(entry);
                if (!IsWithin(fullRoot, fullEntry) || IsReparsePoint(fullEntry))
                    throw new InvalidOperationException("Git checkout contains a symbolic link, reparse point, or path escape");

                var relative = NormalizeRelative(Path.GetRelativePath(fullRoot, fullEntry));
                ValidatePortableTreePath(relative);
                if (Directory.Exists(fullEntry))
                {
                    pending.Push(fullEntry);
                    continue;
                }

                visited++;
                if (visited > 200_000)
                    throw new InvalidOperationException("Git checkout exceeds the 200,000 filesystem entry limit");
                if (!File.Exists(fullEntry))
                    throw new InvalidOperationException("Git checkout contains an unsupported filesystem entry");
                var length = new FileInfo(fullEntry).Length;
                if (length > MaximumCheckoutFileBytes)
                    throw new InvalidOperationException("Git checkout contains a file larger than the 256 MiB limit");
                if (files.Count >= MaximumCheckoutFiles || totalBytes > MaximumCheckoutTotalBytes - length)
                    throw new InvalidOperationException("Git checkout exceeds the configured file count or 2 GiB total size limit");
                totalBytes += length;
                files.Add((relative, fullEntry, length));
            }
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        return files;
    }

    private static string HashCheckoutTree(List<(string RelativePath, string FullPath, long Length)> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("LANG-GIT-CACHE-TREE\0v1"));
        Span<byte> lengthBytes = stackalloc byte[sizeof(ulong)];
        var buffer = new byte[64 * 1024];
        foreach (var file in files)
        {
            var pathBytes = StrictUtf8.GetBytes(file.RelativePath);
            BinaryPrimitives.WriteUInt64BigEndian(lengthBytes, (ulong)pathBytes.LongLength);
            hash.AppendData(lengthBytes);
            hash.AppendData(pathBytes);
            BinaryPrimitives.WriteUInt64BigEndian(lengthBytes, (ulong)file.Length);
            hash.AppendData(lengthBytes);
            using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan);
            if (stream.Length != file.Length)
                throw new InvalidOperationException("Git cache source changed while its content hash was being computed");
            long bytesRead = 0;
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
            {
                bytesRead += count;
                if (bytesRead > file.Length)
                    throw new InvalidOperationException("Git cache source changed while its content hash was being computed");
                hash.AppendData(buffer, 0, count);
            }

            if (bytesRead != file.Length)
                throw new InvalidOperationException("Git cache source changed while its content hash was being computed");
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void WriteAttestation(string path, string url, string commit, string contentHash)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2 }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schema_version", 1);
            writer.WriteString("url", url);
            writer.WriteString("commit", commit);
            writer.WriteString("content_sha256", contentHash);
            writer.WriteEndObject();
            writer.Flush();
        }

        var text = StrictUtf8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var bytes = StrictUtf8.GetBytes(text.TrimEnd('\n') + "\n");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(bytes);
        file.Flush(flushToDisk: true);
    }

    private static CacheAttestation? ReadAttestation(string path)
    {
        var info = new FileInfo(path);
        if (info.Length is <= 0 or > 4096)
            return null;
        var bytes = File.ReadAllBytes(path);
        _ = StrictUtf8.GetString(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4
        });
        var root = document.RootElement;
        string[] expected = ["schema_version", "url", "commit", "content_sha256"];
        if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != expected.Length ||
            !root.EnumerateObject().Select(property => property.Name).SequenceEqual(expected, StringComparer.Ordinal) ||
            !root.GetProperty("schema_version").TryGetInt32(out var version) || version != 1 ||
            root.GetProperty("url").ValueKind != JsonValueKind.String ||
            root.GetProperty("commit").ValueKind != JsonValueKind.String ||
            root.GetProperty("content_sha256").ValueKind != JsonValueKind.String)
            return null;

        var url = root.GetProperty("url").GetString()!;
        var commit = root.GetProperty("commit").GetString()!;
        var contentHash = root.GetProperty("content_sha256").GetString()!;
        if (!PackageDependencySpec.TryParseManifest($"git+{url}#{commit}", out var spec, out _) ||
            spec is not { Kind: PackageDependencyKind.Git } || spec.Url != url || spec.Commit != commit ||
            contentHash.Length != 64 || contentHash.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return null;

        return new CacheAttestation(url, commit, contentHash);
    }

    private static void DeleteGitMetadata(string checkoutRoot, string gitMetadata)
    {
        var fullCheckoutRoot = Path.GetFullPath(checkoutRoot);
        var fullGitMetadata = Path.GetFullPath(gitMetadata);
        if (!IsWithin(fullCheckoutRoot, fullGitMetadata) ||
            !string.Equals(Path.GetFileName(fullGitMetadata), ".git", StringComparison.OrdinalIgnoreCase) ||
            IsReparsePoint(fullGitMetadata))
            throw new InvalidOperationException("Temporary Git metadata path is unsafe");

        DeleteOwnedTree(fullCheckoutRoot, fullGitMetadata);
    }

    private static void DeleteOwnedTree(string ownerRoot, string target)
    {
        var fullOwnerRoot = Path.GetFullPath(ownerRoot);
        var fullTarget = Path.GetFullPath(target);
        if (!IsWithin(fullOwnerRoot, fullTarget) || IsReparsePoint(fullTarget))
            throw new InvalidOperationException("Temporary cleanup path is unsafe");

        if (File.Exists(fullTarget))
        {
            ClearReadOnly(fullTarget);
            File.Delete(fullTarget);
            return;
        }

        var pending = new Stack<(string Path, bool Exit)>();
        pending.Push((fullTarget, false));
        var visited = 0;
        while (pending.Count != 0)
        {
            var (current, exit) = pending.Pop();
            if (!IsWithinOrEqual(fullTarget, current) || IsReparsePoint(current))
                throw new InvalidOperationException("Temporary Git metadata contains a reparse point or path escape");

            if (!exit)
            {
                visited++;
                if (visited > 200_000)
                    throw new InvalidOperationException("Temporary Git data exceeds the cleanup entry limit");

                pending.Push((current, true));
                if (Directory.Exists(current))
                {
                    foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                        pending.Push((Path.GetFullPath(entry), false));
                }
                else if (!File.Exists(current))
                {
                    throw new InvalidOperationException("Temporary Git data contains an unsupported filesystem entry");
                }
                continue;
            }

            ClearReadOnly(current);
            if (Directory.Exists(current))
                Directory.Delete(current);
            else
                File.Delete(current);
        }
    }

    private static void ClearReadOnly(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReadOnly) != 0)
            File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
    }

    private static void EnsureNoReparsePointOnPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("Path has no filesystem root");
        var current = root;
        if (IsReparsePoint(current))
            throw new InvalidOperationException("Package cache cannot be reached through a reparse point");

        foreach (var segment in fullPath[root.Length..].Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
                continue;
            if (IsReparsePoint(current))
                throw new InvalidOperationException("Package cache cannot be reached through a reparse point");
        }
    }

    private static bool IsReparsePoint(string path)
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

    private static bool IsWithin(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(prefix, PathComparison);
    }

    private static bool IsWithinOrEqual(string root, string path) =>
        string.Equals(Path.GetFullPath(root), Path.GetFullPath(path), PathComparison) || IsWithin(root, path);

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string NormalizeRelative(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

    private static void AppendField(IncrementalHash hash, string value)
    {
        var bytes = StrictUtf8.GetBytes(value);
        Span<byte> length = stackalloc byte[sizeof(ulong)];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(length, (ulong)bytes.LongLength);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private static string SafeOperationalError(Exception error) => error switch
    {
        TimeoutException => "git command exceeded the 120-second limit",
        DecoderFallbackException => "Git output was not valid UTF-8",
        System.ComponentModel.Win32Exception => "could not start git",
        InvalidOperationException => error.Message,
        _ => "filesystem access failed"
    };

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private sealed record BoundedOutput(string Text, bool Truncated);
    private sealed record CacheAttestation(string Url, string Commit, string ContentHash);
}
