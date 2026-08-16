using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Controls.ApplicationLifetimes;
using Il2CppDumper.Desktop.Persistence;
using Il2CppDumper.Desktop.ViewModels;

namespace Il2CppDumper.Desktop;

public partial class MainWindow : Window
{
    private GridLength _logsHeight = new(200);
    private MainWindowViewModel ViewModel => (MainWindowViewModel)DataContext;
    private RowDefinition LogsRow => ((Grid)Content).RowDefinitions[2];

    public MainWindow() : this(null)
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? new MainWindowViewModel();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.RequestShutdown += Shutdown;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        Opened += OnOpened;
        Activated += (_, _) => ViewModel.RefreshHistoryOutputAvailability();
        Closing += (_, _) => CaptureWindowPlacement();
    }

    private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsLogsVisible))
            return;

        if (ViewModel.IsLogsVisible)
        {
            LogsRow.Height = _logsHeight;
        }
        else
        {
            if (LogsRow.Height.IsAbsolute && LogsRow.Height.Value >= 180)
                _logsHeight = LogsRow.Height;
            LogsRow.Height = new GridLength(0);
        }
    }

    private async void OnOpened(object sender, EventArgs e)
    {
        ApplyWindowPlacement();
        LogsRow.Height = ViewModel.IsLogsVisible ? _logsHeight : new GridLength(0);
        await ViewModel.CheckForUpdatesOnStartupAsync();
    }

    private void ApplyWindowPlacement()
    {
        var placement = ViewModel.WindowPlacement;
        if (placement is null || placement.Width < MinWidth || placement.Height < MinHeight)
            return;

        var position = new PixelPoint(placement.X, placement.Y);
        if (Screens.ScreenFromPoint(position) is null)
            return;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = position;
        Width = placement.Width;
        Height = placement.Height;
        if (placement.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void CaptureWindowPlacement()
    {
        var previous = ViewModel.WindowPlacement;
        var maximized = WindowState == WindowState.Maximized;
        var width = maximized && previous is not null ? previous.Width : Bounds.Width;
        var height = maximized && previous is not null ? previous.Height : Bounds.Height;
        var position = maximized && previous is not null ? new PixelPoint(previous.X, previous.Y) : Position;
        ViewModel.SetWindowPlacement(new WindowPlacement(position.X, position.Y, width, height, maximized));
    }

    private async void BrowsePackage(object sender, RoutedEventArgs e) => ViewModel.SelectPackage(await PickFileAsync("Choose package", PackageTypes()));
    private async void BrowseBinary(object sender, RoutedEventArgs e) => ViewModel.SelectBinary(await PickFileAsync("Choose executable"));
    private async void BrowseMetadata(object sender, RoutedEventArgs e) => ViewModel.SelectMetadata(await PickFileAsync("Choose global-metadata.dat", [new FilePickerFileType("IL2CPP metadata") { Patterns = ["global-metadata.dat", "*metadata*.dat"] }]));
    private async void BrowseOutput(object sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose output directory", AllowMultiple = false });
        if (folders.Count > 0) ViewModel.OutputPath = folders[0].Path.LocalPath;
    }

    private async void AddBatchFiles(object sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Add packages", AllowMultiple = true, FileTypeFilter = PackageTypes() });
        ViewModel.AddBatchFiles(files.Select(file => file.Path.LocalPath));
    }

    private async void ExportDiagnostics(object sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export diagnostics",
            SuggestedFileName = $"il2cppdumper-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt",
            FileTypeChoices = [new FilePickerFileType("Text report") { Patterns = ["*.txt"] }]
        });
        if (file is not null) ViewModel.ExportDiagnostics(file.Path.LocalPath);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles()?.Select(file => file.Path.LocalPath).ToArray() ?? [];
        ViewModel.UseDroppedFiles(paths);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        var hasFiles = e.DataTransfer.TryGetFiles()?.Any() == true;
        e.DragEffects = hasFiles ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async Task<string> PickFileAsync(string title, IReadOnlyList<FilePickerFileType> types = null)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false, FileTypeFilter = types });
        return files.Count > 0 ? files[0].Path.LocalPath : string.Empty;
    }

    private static IReadOnlyList<FilePickerFileType> PackageTypes() =>
    [
        new FilePickerFileType("IL2CPP packages") { Patterns = ["*.apk", "*.apks", "*.apkm", "*.xapk", "*.zip", "*.ipa"] }
    ];

    private static void Shutdown()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
