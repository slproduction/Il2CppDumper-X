using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using NuGet.Versioning;

namespace Il2CppDumper.Desktop.Updates;

public sealed class GitHubUpdateService : IDisposable
{
    private const string Owner = "slproduction";
    private const string Repository = "Il2CppDumper-X";
    private readonly HttpClient _httpClient;

    public GitHubUpdateService(
        HttpClient httpClient = null,
        string owner = Owner,
        string repository = Repository)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri($"https://api.github.com/repos/{owner}/{repository}/");
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Il2CppDumper", CurrentVersion().ToString(3)));
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
    }

    public static Version CurrentVersion()
    {
        var version = typeof(GitHubUpdateService).Assembly.GetName().Version;
        return version is { Major: >= 0 } ? new Version(version.Major, version.Minor, version.Build < 0 ? 0 : version.Build) : new Version(0, 0, 0);
    }

    public async Task<AvailableUpdate> CheckAsync(
        UpdateChannel channel = UpdateChannel.Stable,
        string skippedVersion = null,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("releases?per_page=30", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var rid = GetRuntimeIdentifier();
        var archiveSuffix = OperatingSystem.IsWindows() ? ".zip" : OperatingSystem.IsMacOS() ? ".dmg" : ".tar.gz";
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var current = NuGetVersion.Parse(CurrentVersion().ToString(3));
        return document.RootElement.EnumerateArray()
            .Select(ParseRelease)
            .Where(release => !release.IsDraft && (channel == UpdateChannel.Prerelease || !release.IsPrerelease))
            .Select(release => SelectUpdate(release, current, rid, archiveSuffix))
            .Where(update => update is not null && !update.Release.Version.Equals(skippedVersion, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(update => NuGetVersion.Parse(update.Release.Version))
            .FirstOrDefault();
    }

    public static AvailableUpdate SelectUpdate(GitHubRelease release, Version currentVersion, string rid, string archiveSuffix)
        => SelectUpdate(release, NuGetVersion.Parse(currentVersion.ToString()), rid, archiveSuffix);

    private static AvailableUpdate SelectUpdate(
        GitHubRelease release,
        NuGetVersion currentVersion,
        string rid,
        string archiveSuffix)
    {
        if (!NuGetVersion.TryParse(release.Version, out var version) || version <= currentVersion)
            return null;
        var archive = release.Assets.FirstOrDefault(asset =>
            asset.Name.EndsWith($"-{rid}{archiveSuffix}", StringComparison.OrdinalIgnoreCase));
        if (archive is null)
            throw new PlatformNotSupportedException($"Release {release.Version} does not contain an archive for {rid}.");
        var checksum = FindChecksum(release.Body, archive.Name);
        if (checksum is null)
            throw new InvalidDataException($"Release {release.Version} does not contain a checksum for {archive.Name}.");
        return new AvailableUpdate(
            release,
            archive,
            checksum,
            new Version(version.Major, version.Minor, version.Patch));
    }

    public async Task InstallAsync(AvailableUpdate update, IProgress<string> progress = null, CancellationToken cancellationToken = default)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "Il2CppDumper", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var archivePath = Path.Combine(temporaryDirectory, update.Archive.Name);
        try
        {
            progress?.Report("Downloading update");
            await DownloadAsync(update.Archive.DownloadUrl, archivePath, cancellationToken);
            progress?.Report("Verifying update checksum");
            await VerifyChecksumAsync(archivePath, update.ExpectedChecksum, update.Archive.Name, cancellationToken);

            var applicationPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(applicationPath))
                throw new InvalidOperationException("The application path could not be determined.");
            var applicationDirectory = Path.GetDirectoryName(applicationPath);
            if (string.IsNullOrWhiteSpace(applicationDirectory))
                throw new InvalidOperationException("The application directory could not be determined.");
            var installDirectory = OperatingSystem.IsMacOS()
                ? GetMacOSApplicationBundle(applicationPath)
                : applicationDirectory;
            VerifyDirectoryIsWritable(installDirectory);

            var installerPath = Path.Combine(temporaryDirectory, OperatingSystem.IsWindows() ? "install-update.ps1" : "install-update.sh");
            await File.WriteAllTextAsync(installerPath, CreateInstallerScript(applicationPath, installDirectory, archivePath, temporaryDirectory), cancellationToken);
            progress?.Report("Restarting with the updated version");
            StartInstaller(installerPath);
        }
        catch
        {
            try { Directory.Delete(temporaryDirectory, true); } catch { }
            throw;
        }
    }

    private static GitHubRelease ParseRelease(JsonElement root)
    {
        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => new ReleaseAsset(
                asset.GetProperty("name").GetString() ?? string.Empty,
                new Uri(asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Release asset URL is missing.")),
                asset.GetProperty("size").GetInt64()))
            .ToArray();
        return new GitHubRelease(
            root.GetProperty("tag_name").GetString() ?? string.Empty,
            root.GetProperty("name").GetString() ?? string.Empty,
            new Uri(root.GetProperty("html_url").GetString() ?? throw new InvalidDataException("Release URL is missing.")),
            root.GetProperty("prerelease").GetBoolean(),
            root.GetProperty("draft").GetBoolean(),
            root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty,
            root.TryGetProperty("published_at", out var published) && published.ValueKind == JsonValueKind.String
                ? published.GetDateTimeOffset()
                : null,
            assets);
    }

    private static string GetRuntimeIdentifier()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Only x64 and arm64 updates are supported.")
        };
        return $"{os}-{architecture}";
    }

    private async Task DownloadAsync(Uri url, string path, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(path);
        await source.CopyToAsync(target, cancellationToken);
    }

    private static string FindChecksum(string releaseBody, string archiveName)
    {
        if (string.IsNullOrWhiteSpace(releaseBody)) return null;
        var escapedName = Regex.Escape(archiveName);
        var match = Regex.Match(
            releaseBody,
            $@"(?im)^\|\s*`?{escapedName}`?\s*\|\s*`?([0-9a-f]{{64}})`?\s*\|\s*$");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static async Task VerifyChecksumAsync(string archivePath, string expected, string archiveName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
            throw new InvalidDataException("The release checksum is invalid.");
        await using var archive = File.OpenRead(archivePath);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken));
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(expected), Convert.FromHexString(actual)))
            throw new InvalidDataException($"Checksum verification failed for {archiveName}.");
    }

    private static string CreateInstallerScript(string applicationPath, string installDirectory, string archivePath, string temporaryDirectory)
    {
        var stageDirectory = Path.Combine(temporaryDirectory, "stage");
        var backupDirectory = Path.Combine(temporaryDirectory, "backup");
        if (OperatingSystem.IsWindows())
            return $"$ErrorActionPreference = 'Stop'\n$processId = {Environment.ProcessId}\n$stage = '{EscapePowerShell(stageDirectory)}'\n$backup = '{EscapePowerShell(backupDirectory)}'\nwhile (Get-Process -Id $processId -ErrorAction SilentlyContinue) {{ Start-Sleep -Milliseconds 250 }}\nExpand-Archive -LiteralPath '{EscapePowerShell(archivePath)}' -DestinationPath $stage -Force\nNew-Item -ItemType Directory -Path $backup -Force | Out-Null\nCopy-Item -Path '{EscapePowerShell(Path.Combine(installDirectory, "*"))}' -Destination $backup -Recurse -Force\ntry {{\n  Copy-Item -Path (Join-Path $stage '*') -Destination '{EscapePowerShell(installDirectory)}' -Recurse -Force\n  Start-Process -FilePath '{EscapePowerShell(applicationPath)}'\n  Remove-Item -LiteralPath '{EscapePowerShell(temporaryDirectory)}' -Recurse -Force -ErrorAction SilentlyContinue\n}} catch {{\n  Copy-Item -Path (Join-Path $backup '*') -Destination '{EscapePowerShell(installDirectory)}' -Recurse -Force\n  Start-Process -FilePath '{EscapePowerShell(applicationPath)}'\n  throw\n}}\n";
        if (OperatingSystem.IsMacOS())
            return $"#!/bin/sh\nset -e\nmount_point='{EscapeShell(stageDirectory)}'\napp_dir='{EscapeShell(installDirectory)}'\narchive='{EscapeShell(archivePath)}'\nwhile kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.25; done\nmkdir -p \"$mount_point\"\nhdiutil attach \"$archive\" -nobrowse -readonly -mountpoint \"$mount_point\"\ntrap 'hdiutil detach \"$mount_point\" >/dev/null 2>&1 || true' EXIT\nsource_app=$(find \"$mount_point\" -maxdepth 1 -name '*.app' -type d -print -quit)\n[ -n \"$source_app\" ]\nrm -rf \"$app_dir\"\ncp -R \"$source_app\" \"$app_dir\"\nopen \"$app_dir\"\nhdiutil detach \"$mount_point\"\ntrap - EXIT\nrm -rf '{EscapeShell(temporaryDirectory)}'\n";
        return $"#!/bin/sh\nset -e\nstage='{EscapeShell(stageDirectory)}'\nbackup='{EscapeShell(backupDirectory)}'\napp_dir='{EscapeShell(installDirectory)}'\nwhile kill -0 {Environment.ProcessId} 2>/dev/null; do sleep 0.25; done\nmkdir -p \"$stage\" \"$backup\"\ntar -xzf '{EscapeShell(archivePath)}' -C \"$stage\"\ncp -R \"$app_dir/.\" \"$backup\"\nif ! cp -R \"$stage/.\" \"$app_dir\"; then\n  cp -R \"$backup/.\" \"$app_dir\"\n  exec '{EscapeShell(applicationPath)}'\nfi\n'{EscapeShell(applicationPath)}' &\nrm -rf '{EscapeShell(temporaryDirectory)}'\n";
    }

    private static string GetMacOSApplicationBundle(string applicationPath)
    {
        var marker = $".app{Path.DirectorySeparatorChar}Contents{Path.DirectorySeparatorChar}MacOS{Path.DirectorySeparatorChar}";
        var markerIndex = applicationPath.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            throw new InvalidOperationException("The macOS application is not running from an app bundle.");
        return applicationPath[..(markerIndex + ".app".Length)];
    }

    private static void VerifyDirectoryIsWritable(string directory)
    {
        var probe = Path.Combine(directory, $".il2cppdumper-update-{Guid.NewGuid():N}.tmp");
        try
        {
            using (File.Create(probe)) { }
        }
        catch (Exception exception)
        {
            throw new UnauthorizedAccessException($"The installation directory is not writable: {directory}", exception);
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }
    }

    private static void StartInstaller(string installerPath)
    {
        if (OperatingSystem.IsWindows())
        {
            var startInfo = new ProcessStartInfo("powershell") { UseShellExecute = false, CreateNoWindow = true };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(installerPath);
            Process.Start(startInfo);
            return;
        }
        File.SetUnixFileMode(installerPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var unixStartInfo = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true };
        unixStartInfo.ArgumentList.Add(installerPath);
        Process.Start(unixStartInfo);
    }

    private static string EscapePowerShell(string value) => value.Replace("'", "''");
    private static string EscapeShell(string value) => value.Replace("'", "'\\''");

    public void Dispose() => _httpClient.Dispose();
}
