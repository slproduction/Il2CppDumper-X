using System.IO.Compression;

namespace Il2CppDumper.Packages;

public sealed class PackageResolver
{
    private static readonly string[] SupportedExtensions = [".apk", ".apks", ".apkm", ".xapk", ".zip", ".ipa"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public async Task<PackageInspection> InspectAsync(string packagePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Package was not found.", packagePath);
        if (!IsSupported(packagePath)) throw new NotSupportedException("Package type is not supported.");
        return await Task.Run(() => Inspect(packagePath, cancellationToken), cancellationToken);
    }

    private static PackageInspection Inspect(string packagePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(packagePath).ToLowerInvariant();
        var type = extension switch
        {
            ".apk" => PackageContainerType.Apk,
            ".apks" => PackageContainerType.SplitApkSet,
            ".apkm" => PackageContainerType.Apkm,
            ".xapk" => PackageContainerType.Xapk,
            ".ipa" => PackageContainerType.Ipa,
            _ => PackageContainerType.Zip
        };
        var warnings = new List<string>();
        var architectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var metadataPresent = false;
        using var archive = ZipFile.OpenRead(packagePath);
        InspectArchive(archive, type == PackageContainerType.Ipa, ref metadataPresent, architectures);

        foreach (var nestedEntry in archive.Entries.Where(entry => entry.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var memory = new MemoryStream();
                using (var source = nestedEntry.Open()) source.CopyTo(memory);
                memory.Position = 0;
                using var nested = new ZipArchive(memory, ZipArchiveMode.Read);
                InspectArchive(nested, false, ref metadataPresent, architectures);
            }
            catch (InvalidDataException)
            {
                warnings.Add($"Could not inspect nested package {nestedEntry.FullName}.");
            }
        }

        if (!metadataPresent) warnings.Add("global-metadata.dat was not found.");
        if (architectures.Count == 0) warnings.Add("No IL2CPP executable was found.");
        return new PackageInspection(
            packagePath,
            type,
            new FileInfo(packagePath).Length,
            metadataPresent,
            architectures.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            warnings);
    }

    private static void InspectArchive(
        ZipArchive archive,
        bool isIos,
        ref bool metadataPresent,
        ISet<string> architectures)
    {
        metadataPresent |= archive.Entries.Any(entry =>
            entry.FullName.EndsWith("/global-metadata.dat", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.Equals("global-metadata.dat", StringComparison.OrdinalIgnoreCase));
        foreach (var entry in archive.Entries.Where(entry => entry.FullName.EndsWith("/libil2cpp.so", StringComparison.OrdinalIgnoreCase)))
            architectures.Add(GetAndroidArchitecture(entry.FullName));
        if (isIos && FindIosBinary(archive) is not null)
            architectures.Add("arm64");
    }

    public async Task<PreparedPackage> PrepareAsync(
        string packagePath,
        PackageOptions options,
        IProgress<string> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(packagePath))
            throw new FileNotFoundException("Package was not found.", packagePath);

        var workspace = Path.Combine(Path.GetTempPath(), "Il2CppDumper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);

        try
        {
            return await Task.Run(() => Prepare(packagePath, workspace, options, progress, cancellationToken), cancellationToken);
        }
        catch
        {
            try { Directory.Delete(workspace, true); } catch { }
            throw;
        }
    }

    private static PreparedPackage Prepare(
        string packagePath,
        string workspace,
        PackageOptions options,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        progress?.Report($"Inspecting {Path.GetFileName(packagePath)}");
        using var archive = ZipFile.OpenRead(packagePath);

        var direct = FindInputs(packagePath, archive, workspace, options, progress, cancellationToken);
        if (direct.Count > 0)
            return new PreparedPackage(workspace, direct);

        var nestedPackages = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) ||
                            entry.FullName.EndsWith(".obb", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (nestedPackages.Length == 0)
            throw new InvalidDataException("The package does not contain an IL2CPP binary and global-metadata.dat.");

        var nestedDirectory = Path.Combine(workspace, "nested");
        Directory.CreateDirectory(nestedDirectory);
        var nestedPaths = new List<string>();
        foreach (var entry in nestedPackages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = SafeExtract(entry, nestedDirectory, Path.GetFileName(entry.FullName));
            nestedPaths.Add(path);
        }

        string metadataPath = null;
        var binaries = new List<(string Path, string Architecture)>();
        foreach (var nestedPath in nestedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var nested = ZipFile.OpenRead(nestedPath);
                metadataPath ??= ExtractMetadata(nested, workspace);
                binaries.AddRange(ExtractAndroidBinaries(nested, workspace, options));
            }
            catch (InvalidDataException) when (Path.GetExtension(nestedPath).Equals(".obb", StringComparison.OrdinalIgnoreCase))
            {
                // Some OBB files are not ZIP containers.
            }
        }

        return BuildPreparedPackage(packagePath, workspace, metadataPath, binaries);
    }

    private static List<PreparedInput> FindInputs(
        string packagePath,
        ZipArchive archive,
        string workspace,
        PackageOptions options,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadataPath = ExtractMetadata(archive, workspace);
        if (metadataPath is null)
            return [];

        var binaries = ExtractAndroidBinaries(archive, workspace, options);
        if (binaries.Count == 0)
        {
            var iosBinary = FindIosBinary(archive);
            if (iosBinary is not null)
            {
                var binaryPath = SafeExtract(iosBinary, Path.Combine(workspace, "inputs"), Path.GetFileName(iosBinary.FullName));
                binaries.Add((binaryPath, "arm64"));
            }
        }

        progress?.Report($"Found {binaries.Count} executable input(s)");
        return BuildPreparedPackage(packagePath, workspace, metadataPath, binaries).Inputs.ToList();
    }

    private static string ExtractMetadata(ZipArchive archive, string workspace)
    {
        var entry = archive.Entries.FirstOrDefault(item =>
            item.FullName.EndsWith("/global-metadata.dat", StringComparison.OrdinalIgnoreCase) ||
            item.FullName.Equals("global-metadata.dat", StringComparison.OrdinalIgnoreCase));
        return entry is null
            ? null
            : SafeExtract(entry, Path.Combine(workspace, "inputs"), "global-metadata.dat");
    }

    private static List<(string Path, string Architecture)> ExtractAndroidBinaries(
        ZipArchive archive,
        string workspace,
        PackageOptions options)
    {
        var result = new List<(string, string)>();
        foreach (var entry in archive.Entries.Where(item =>
                     item.FullName.EndsWith("/libil2cpp.so", StringComparison.OrdinalIgnoreCase)))
        {
            var architecture = GetAndroidArchitecture(entry.FullName);
            if (options.Architectures.Count > 0 && !options.Architectures.Contains(architecture))
                continue;

            var destination = Path.Combine(workspace, "inputs", architecture);
            result.Add((SafeExtract(entry, destination, "libil2cpp.so"), architecture));
        }
        return result;
    }

    private static ZipArchiveEntry FindIosBinary(ZipArchive archive)
    {
        var framework = archive.Entries.FirstOrDefault(item =>
            item.FullName.Contains(".app/Frameworks/UnityFramework.framework/", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.EndsWith("/UnityFramework", StringComparison.OrdinalIgnoreCase));
        if (framework is not null)
            return framework;

        return archive.Entries.FirstOrDefault(item =>
            item.FullName.StartsWith("Payload/", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.Contains(".app/", StringComparison.OrdinalIgnoreCase) &&
            item.FullName.Count(character => character == '/') == 2 &&
            item.Length > 0);
    }

    private static PreparedPackage BuildPreparedPackage(
        string packagePath,
        string workspace,
        string metadataPath,
        List<(string Path, string Architecture)> binaries)
    {
        if (metadataPath is null || binaries.Count == 0)
            throw new InvalidDataException("The package does not contain a complete IL2CPP input set.");

        var name = Path.GetFileNameWithoutExtension(packagePath);
        var inputs = binaries.Select(item => new PreparedInput(
            name,
            item.Path,
            metadataPath,
            item.Architecture,
            binaries.Count > 1 ? item.Architecture : string.Empty)).ToArray();
        return new PreparedPackage(workspace, inputs);
    }

    private static string GetAndroidArchitecture(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.Contains("arm64-v8a", StringComparison.OrdinalIgnoreCase) || normalized.Contains("arm64_v8a", StringComparison.OrdinalIgnoreCase)) return "arm64-v8a";
        if (normalized.Contains("armeabi-v7a", StringComparison.OrdinalIgnoreCase) || normalized.Contains("armeabi_v7a", StringComparison.OrdinalIgnoreCase)) return "armeabi-v7a";
        if (normalized.Contains("x86_64", StringComparison.OrdinalIgnoreCase) || normalized.Contains("x86-64", StringComparison.OrdinalIgnoreCase)) return "x86_64";
        if (normalized.Contains("/x86/", StringComparison.OrdinalIgnoreCase)) return "x86";
        return "unknown";
    }

    private static string SafeExtract(ZipArchiveEntry entry, string destinationDirectory, string fileName)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destination = Path.GetFullPath(Path.Combine(destinationDirectory, fileName));
        var root = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(root, StringComparison.Ordinal))
            throw new InvalidDataException("Archive entry points outside the extraction directory.");

        entry.ExtractToFile(destination, true);
        return destination;
    }
}
