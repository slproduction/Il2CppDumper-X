using CommunityToolkit.Mvvm.ComponentModel;
using Il2CppDumper.Application;

namespace Il2CppDumper.Desktop.ViewModels;

public partial class AnalysisScriptViewModel(AnalysisScript script) : ObservableObject
{
    public string FileName { get; } = script.FileName;
    public string Tool { get; } = script.Tool;
    public string Description { get; } = script.Description;
    public bool RequiresStructures { get; } = script.RequiresStructures;

    [ObservableProperty] private bool _isSelected;
}
