using System.IO.Compression;
using Il2CppDumper.Packages;

namespace Il2CppDumper.Tests;

public sealed class PackageResolverTests
{
    [Fact]
    public async Task PrepareAsync_FindsAllSelectedAndroidArchitectures()
    {
        var package = CreateArchive(new Dictionary<string, byte[]>
        {
            ["assets/bin/Data/Managed/Metadata/global-metadata.dat"] = [1, 2, 3],
            ["lib/arm64-v8a/libil2cpp.so"] = [4, 5, 6],
            ["lib/x86_64/libil2cpp.so"] = [7, 8, 9]
        }, ".apk");

        try
        {
            await using var prepared = await new PackageResolver().PrepareAsync(
                package,
                new PackageOptions { Architectures = new HashSet<string>(["arm64-v8a", "x86_64"]) },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(2, prepared.Inputs.Count);
            Assert.Contains(prepared.Inputs, input => input.Architecture == "arm64-v8a");
            Assert.Contains(prepared.Inputs, input => input.Architecture == "x86_64");
            Assert.All(prepared.Inputs, input => Assert.True(File.Exists(input.MetadataPath)));
        }
        finally
        {
            File.Delete(package);
        }
    }

    [Fact]
    public async Task PrepareAsync_ResolvesSplitApkContainer()
    {
        var baseApk = CreateArchive(new Dictionary<string, byte[]>
        {
            ["assets/bin/Data/Managed/Metadata/global-metadata.dat"] = [1]
        }, ".apk");
        var configApk = CreateArchive(new Dictionary<string, byte[]>
        {
            ["lib/arm64-v8a/libil2cpp.so"] = [2]
        }, ".apk");
        var split = CreateArchive(new Dictionary<string, byte[]>
        {
            ["base.apk"] = await File.ReadAllBytesAsync(baseApk, TestContext.Current.CancellationToken),
            ["config.arm64_v8a.apk"] = await File.ReadAllBytesAsync(configApk, TestContext.Current.CancellationToken)
        }, ".apks");

        try
        {
            await using var prepared = await new PackageResolver().PrepareAsync(
                split,
                new PackageOptions(),
                cancellationToken: TestContext.Current.CancellationToken);
            var input = Assert.Single(prepared.Inputs);
            Assert.Equal("arm64-v8a", input.Architecture);
            Assert.True(File.Exists(input.BinaryPath));
            Assert.True(File.Exists(input.MetadataPath));
        }
        finally
        {
            File.Delete(baseApk);
            File.Delete(configApk);
            File.Delete(split);
        }
    }

    [Theory]
    [InlineData("game.apk")]
    [InlineData("game.APKS")]
    [InlineData("game.apkm")]
    [InlineData("game.xapk")]
    [InlineData("game.zip")]
    [InlineData("game.ipa")]
    public void IsSupported_RecognizesDocumentedPackages(string path) =>
        Assert.True(PackageResolver.IsSupported(path));

    private static string CreateArchive(IReadOnlyDictionary<string, byte[]> entries, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}{extension}");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var stream = entry.Open();
            stream.Write(content);
        }
        return path;
    }
}
