using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Il2CppDumper.Desktop.Diagnostics;
using Il2CppDumper.Desktop.Persistence;
using Il2CppDumper.Desktop.Shell;
using Il2CppDumper.Desktop.ViewModels;

namespace Il2CppDumper.Desktop;

public partial class App : Avalonia.Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var stateStore = new JsonDesktopStateStore();
            var viewModel = new MainWindowViewModel(stateStore, new ProcessPathLauncher(), new DiagnosticExportService());
            viewModel.LoadState(stateStore.Load());
            desktop.MainWindow = new MainWindow(viewModel);
            desktop.Exit += (_, _) => viewModel.SaveState();
        }
        base.OnFrameworkInitializationCompleted();
    }
}
