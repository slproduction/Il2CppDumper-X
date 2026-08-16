using Il2CppDumper.Desktop.Persistence;

namespace Il2CppDumper.Tests;

public sealed class JsonDesktopStateStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public JsonDesktopStateStoreTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void SaveAndLoad_PreservesWindowAndNestedOutputPath()
    {
        var path = Path.Combine(_directory, "state", "desktop-state.json");
        var outputPath = Path.Combine(_directory, "packages", "nested", "game_dumped");
        var store = new JsonDesktopStateStore(path);
        var expected = new DesktopState
        {
            Window = new WindowPlacement(120, 80, 1440, 860, true),
            Settings = new DesktopSettings { OutputPath = outputPath }
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected.Window, actual.Window);
        Assert.Equal(outputPath, actual.Settings.OutputPath);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
