namespace Il2CppDumper.Packages;

public enum PackageContainerType
{
    Apk,
    SplitApkSet,
    Apkm,
    Xapk,
    Ipa,
    Zip
}

public sealed record PackageInspection(
    string Path,
    PackageContainerType ContainerType,
    long FileSize,
    bool MetadataPresent,
    IReadOnlyList<string> Architectures,
    IReadOnlyList<string> Warnings)
{
    public bool IsComplete => MetadataPresent && Architectures.Count > 0;
}
