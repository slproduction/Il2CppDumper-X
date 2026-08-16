using Il2CppDumper.Desktop.Updates;

namespace Il2CppDumper.Tests;

public sealed class GitHubUpdateServiceTests
{
    [Fact]
    public void SelectUpdate_ReturnsMatchingArchiveAndReleaseChecksum()
    {
        var release = CreateReleaseWithBody("v2.4.0",
            "| `Il2CppDumper-win-x64.zip` | `0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef` |",
            "Il2CppDumper-win-x64.zip",
            "Il2CppDumper-linux-x64.tar.gz");

        var update = GitHubUpdateService.SelectUpdate(release, new Version(2, 3, 0), "win-x64", ".zip");

        Assert.NotNull(update);
        Assert.Equal(new Version(2, 4, 0), update.Version);
        Assert.Equal("Il2CppDumper-win-x64.zip", update.Archive.Name);
        Assert.Equal(64, update.ExpectedChecksum.Length);
    }

    [Fact]
    public void SelectUpdate_ReturnsMacOsDmg()
    {
        var release = CreateReleaseWithBody("v2.4.0",
            "| `Il2CppDumper-osx-arm64.dmg` | `0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef` |",
            "Il2CppDumper-osx-arm64.dmg",
            "Il2CppDumper-cli-osx-arm64.tar.gz");

        var update = GitHubUpdateService.SelectUpdate(release, new Version(2, 3, 0), "osx-arm64", ".dmg");

        Assert.NotNull(update);
        Assert.Equal("Il2CppDumper-osx-arm64.dmg", update.Archive.Name);
    }

    [Fact]
    public void SelectUpdate_ReturnsNullForCurrentOrOlderRelease()
    {
        var release = CreateRelease("v2.3.0",
            "Il2CppDumper-win-x64.zip");

        var update = GitHubUpdateService.SelectUpdate(release, new Version(2, 3, 0), "win-x64", ".zip");

        Assert.Null(update);
    }

    [Fact]
    public void SelectUpdate_RejectsReleaseWithoutReleaseChecksum()
    {
        var release = CreateRelease("v2.4.0", "Il2CppDumper-linux-arm64.tar.gz");

        var exception = Assert.Throws<InvalidDataException>(() =>
            GitHubUpdateService.SelectUpdate(release, new Version(2, 3, 0), "linux-arm64", ".tar.gz"));

        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectUpdate_RejectsReleaseWithoutCurrentPlatformArchive()
    {
        var release = CreateRelease("v2.4.0",
            "Il2CppDumper-win-x64.zip");

        Assert.Throws<PlatformNotSupportedException>(() =>
            GitHubUpdateService.SelectUpdate(release, new Version(2, 3, 0), "osx-arm64", ".dmg"));
    }

    private static GitHubRelease CreateRelease(string tag, params string[] assetNames) => new(
        tag,
        $"Il2CppDumper {tag}",
        new Uri("https://github.com/slproduction/Il2CppDumper-X/releases/tag/" + tag),
        false,
        false,
        string.Empty,
        DateTimeOffset.UtcNow,
        assetNames.Select(name => new ReleaseAsset(
            name,
            new Uri("https://github.com/slproduction/Il2CppDumper-X/releases/download/" + tag + "/" + name),
            1024)).ToArray());

    private static GitHubRelease CreateReleaseWithBody(string tag, string body, params string[] assetNames) =>
        CreateRelease(tag, assetNames) with { Body = body };
}
