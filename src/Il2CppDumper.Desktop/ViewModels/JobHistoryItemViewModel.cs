using CommunityToolkit.Mvvm.ComponentModel;
using Il2CppDumper.Desktop.Persistence;

namespace Il2CppDumper.Desktop.ViewModels;

public sealed partial class JobHistoryItemViewModel : ObservableObject
{
    public JobHistoryItemViewModel(JobHistoryEntry entry)
    {
        Entry = entry;
        RefreshOutputAvailability();
    }

    public JobHistoryEntry Entry { get; }
    public Guid Id => Entry.Id;
    public DateTimeOffset FinishedAt => Entry.FinishedAt;
    public string InputPath => Entry.InputPath;
    public string OutputDirectory => Entry.OutputDirectory;
    public string Architecture => Entry.Architecture;
    public bool Success => Entry.Success;
    public bool Failed => !Success;
    public int ArtifactCount => Entry.ArtifactCount;
    public string Status => Success ? $"Completed, {ArtifactCount} artifacts" : $"Failed: {Entry.Error?.Split('\n')[0]}";
    [ObservableProperty] private bool _isOutputAvailable;
    public string OutputStatus => IsOutputAvailable ? OutputDirectory : "Output directory no longer exists";
    public bool IsOutputMissing => !IsOutputAvailable;

    partial void OnIsOutputAvailableChanged(bool value)
    {
        OnPropertyChanged(nameof(OutputStatus));
        OnPropertyChanged(nameof(IsOutputMissing));
    }

    public void RefreshOutputAvailability() => IsOutputAvailable = Directory.Exists(OutputDirectory);
}
