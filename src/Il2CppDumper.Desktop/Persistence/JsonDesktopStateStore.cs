using System.Text.Json;
using System.Text.Json.Serialization;

namespace Il2CppDumper.Desktop.Persistence;

public interface IDesktopStateStore
{
    DesktopState Load();
    void Save(DesktopState state);
}

public sealed class JsonDesktopStateStore : IDesktopStateStore
{
    private const int CurrentVersion = 1;
    private readonly string _path;

    public JsonDesktopStateStore(string path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Il2CppDumper",
            "desktop-state.json");
    }

    public DesktopState Load()
    {
        try
        {
            if (!File.Exists(_path)) return new DesktopState();
            var state = JsonSerializer.Deserialize(File.ReadAllText(_path), DesktopStateJsonContext.Default.DesktopState);
            return state is { Version: CurrentVersion } ? TrimHistory(state) : new DesktopState();
        }
        catch
        {
            return new DesktopState();
        }
    }

    public void Save(DesktopState state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("State directory is missing.");
        Directory.CreateDirectory(directory);
        var temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(TrimHistory(state), DesktopStateJsonContext.Default.DesktopState));
        File.Move(temporaryPath, _path, true);
    }

    private static DesktopState TrimHistory(DesktopState state) => state with
    {
        History = (state.History ?? []).Take(50).ToArray()
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(DesktopState))]
internal partial class DesktopStateJsonContext : JsonSerializerContext;
