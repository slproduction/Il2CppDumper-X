using Il2CppDumper.Application;

namespace Il2CppDumper.Tests;

public sealed class DumpServiceTests
{
    [Fact]
    public async Task BatchAsync_ContinuesAfterInvalidJob()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var requests = new[]
        {
            new DumpRequest { PackagePath = Path.Combine(root, "missing-a.apk"), OutputDirectory = Path.Combine(root, "a") },
            new DumpRequest { PackagePath = Path.Combine(root, "missing-b.apk"), OutputDirectory = Path.Combine(root, "b") }
        };

        var result = await new DumpService().BatchAsync(
            new BatchRequest(requests),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Failed);
        Assert.Equal(2, result.Jobs.Count);
    }

    [Fact]
    public async Task BatchAsync_StopsAfterInvalidJobWhenRequested()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var requests = new[]
        {
            new DumpRequest { PackagePath = Path.Combine(root, "missing-a.apk"), OutputDirectory = Path.Combine(root, "a") },
            new DumpRequest { PackagePath = Path.Combine(root, "missing-b.apk"), OutputDirectory = Path.Combine(root, "b") }
        };

        var result = await new DumpService().BatchAsync(
            new BatchRequest(requests, ContinueOnError: false),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(result.Jobs);
    }
}
