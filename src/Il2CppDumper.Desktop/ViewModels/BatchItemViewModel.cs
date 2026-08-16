using CommunityToolkit.Mvvm.ComponentModel;
using Il2CppDumper.Packages;

namespace Il2CppDumper.Desktop.ViewModels;

public partial class BatchItemViewModel : ObservableObject
{
    public BatchItemViewModel(string inputPath, string outputPath)
    {
        InputPath = inputPath;
        OutputPath = outputPath;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public string InputPath { get; }
    public string Name => Path.GetFileNameWithoutExtension(InputPath);
    public string Type => Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();

    [ObservableProperty] private string _outputPath;
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _detail = "Waiting to run";
    [ObservableProperty] private bool _isFailed;
    [ObservableProperty] private string _inspection = "Inspecting package...";
    [ObservableProperty] private bool _isInspectionComplete;
    [ObservableProperty] private bool _isInspectionValid;

    public void ApplyInspection(PackageInspection inspection)
    {
        IsInspectionComplete = true;
        IsInspectionValid = inspection.IsComplete;
        var size = inspection.FileSize >= 1024 * 1024
            ? $"{inspection.FileSize / 1024d / 1024d:F1} MB"
            : $"{inspection.FileSize / 1024d:F1} KB";
        var architectures = inspection.Architectures.Count == 0 ? "no executable" : string.Join(", ", inspection.Architectures);
        Inspection = $"{inspection.ContainerType} · {size} · {architectures}";
        Detail = inspection.IsComplete ? Inspection : string.Join(" ", inspection.Warnings);
    }

    public void ApplyInspectionError(Exception exception)
    {
        IsInspectionComplete = true;
        IsInspectionValid = false;
        Inspection = "Inspection failed";
        Detail = exception.Message;
    }
}
