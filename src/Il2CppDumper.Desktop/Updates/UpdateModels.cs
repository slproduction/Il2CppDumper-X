namespace Il2CppDumper.Desktop.Updates;

public enum UpdateChannel
{
    Stable,
    Prerelease
}

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size);

public sealed record GitHubRelease(
    string TagName,
    string Name,
    Uri HtmlUrl,
    bool IsPrerelease,
    bool IsDraft,
    string Body,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<ReleaseAsset> Assets)
{
    public string Version => TagName.TrimStart('v', 'V');
}

public sealed record AvailableUpdate(
    GitHubRelease Release,
    ReleaseAsset Archive,
    string ExpectedChecksum,
    Version Version);
