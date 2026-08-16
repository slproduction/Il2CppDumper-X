using System.Reflection;
using System.Text;
using Il2CppDumper.Desktop.Persistence;
using Il2CppDumper.Desktop.ViewModels;

namespace Il2CppDumper.Desktop.Diagnostics;

public sealed class DiagnosticExportService
{
    public void Export(string path, IReadOnlyCollection<ActivityItem> entries, DesktopState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Il2CppDumper diagnostic report");
        builder.AppendLine($"Generated: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Application version: {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "unknown"}");
        builder.AppendLine();
        builder.AppendLine("Current input");
        builder.AppendLine($"  Package: {state.Settings.PackagePath}");
        builder.AppendLine($"  Binary: {state.Settings.BinaryPath}");
        builder.AppendLine($"  Metadata: {state.Settings.MetadataPath}");
        builder.AppendLine($"  Output: {state.Settings.OutputPath}");
        builder.AppendLine();
        builder.AppendLine("History");
        foreach (var item in state.History)
            builder.AppendLine($"  {item.FinishedAt:O}  {(item.Success ? "Completed" : "Failed")}  {item.InputPath}  {item.OutputDirectory}");
        builder.AppendLine();
        builder.AppendLine("Diagnostics");
        foreach (var entry in entries)
            builder.AppendLine($"  {entry.Timestamp:O}  {entry.LevelText,-11}  {(string.IsNullOrEmpty(entry.JobName) ? string.Empty : $"[{entry.JobName}] ")}{entry.Message}");

        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, builder.ToString(), new UTF8Encoding(false));
        File.Move(temporaryPath, path, true);
    }
}
