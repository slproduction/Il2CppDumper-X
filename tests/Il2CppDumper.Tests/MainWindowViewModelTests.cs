using System.IO.Compression;
using Il2CppDumper.Desktop.Diagnostics;
using Il2CppDumper.Desktop.Persistence;
using Il2CppDumper.Desktop.Shell;
using Il2CppDumper.Desktop.ViewModels;

namespace Il2CppDumper.Tests;

public sealed class MainWindowViewModelTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public MainWindowViewModelTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task UseDroppedFiles_DetectsSinglePackage()
    {
        var package = CreatePackage("game.ipa", true);
        var viewModel = new MainWindowViewModel();

        viewModel.UseDroppedFiles([package]);
        await WaitForPackageInspectionAsync(viewModel);

        Assert.True(viewModel.IsPackageMode);
        Assert.Equal(package, viewModel.PackagePath);
        Assert.False(viewModel.IsAndroidPackage);
        Assert.Equal(["All detected"], viewModel.AndroidArchitectureOptions);
        Assert.Contains("arm64", viewModel.PackageInspectionSummary);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectPackage_OffersOnlyDetectedAndroidArchitectures()
    {
        var package = CreatePackage("game.apk", androidArchitectures: ["arm64-v8a", "x86_64"]);
        var viewModel = new MainWindowViewModel();

        viewModel.SelectPackage(package);
        await WaitForPackageInspectionAsync(viewModel);

        Assert.True(viewModel.IsAndroidPackage);
        Assert.Equal(["All detected", "arm64-v8a", "x86_64"], viewModel.AndroidArchitectureOptions);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public void UseDroppedFiles_DetectsBinaryAndMetadataPair()
    {
        var binary = CreateFile("UnityFramework");
        var metadata = CreateFile("global-metadata.dat");
        var viewModel = new MainWindowViewModel();

        viewModel.UseDroppedFiles([metadata, binary]);

        Assert.True(viewModel.IsDirectMode);
        Assert.Equal(binary, viewModel.BinaryPath);
        Assert.Equal(metadata, viewModel.MetadataPath);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task UseDroppedFiles_SendsMultiplePackagesToBatch()
    {
        var first = CreatePackage("one.apk");
        var second = CreatePackage("two.ipa", true);
        var viewModel = new MainWindowViewModel();

        viewModel.UseDroppedFiles([first, second]);
        await WaitForBatchInspectionAsync(viewModel);

        Assert.True(viewModel.IsBatchPage);
        Assert.Equal(2, viewModel.BatchItems.Count);
        Assert.True(viewModel.StartBatchCommand.CanExecute(null));
    }

    [Fact]
    public async Task UseDroppedFiles_RecursivelyAddsFolderPackagesToBatch()
    {
        var nestedDirectory = Path.Combine(_directory, "nested");
        Directory.CreateDirectory(nestedDirectory);
        var first = CreateFile("one.apk");
        var second = CreateFile(Path.Combine("nested", "two.ipa"));
        var viewModel = new MainWindowViewModel
        {
            OutputPath = Path.Combine(_directory, "dump-tab-output")
        };

        viewModel.UseDroppedFiles([_directory]);
        await WaitForBatchInspectionAsync(viewModel);

        Assert.True(viewModel.IsBatchPage);
        Assert.Equal(2, viewModel.BatchItems.Count);
        Assert.Contains(viewModel.BatchItems, item => item.InputPath == first);
        Assert.Contains(viewModel.BatchItems, item => item.InputPath == second);
        Assert.Equal(
            Path.Combine(_directory, "one_dumped"),
            viewModel.BatchItems.Single(item => item.InputPath == first).OutputPath);
        Assert.Equal(
            Path.Combine(nestedDirectory, "two_dumped"),
            viewModel.BatchItems.Single(item => item.InputPath == second).OutputPath);
    }

    [Fact]
    public void StartCommand_RemainsDisabledForIncompleteDirectInput()
    {
        var viewModel = new MainWindowViewModel
        {
            BinaryPath = CreateFile("GameAssembly.dll"),
            OutputPath = Path.Combine(_directory, "output")
        };

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Equal("Choose global-metadata.dat.", viewModel.ValidationMessage);
    }

    [Fact]
    public void StartCommand_RejectsIncompleteRegistrationAddressPair()
    {
        var viewModel = CreateValidDirectViewModel();
        viewModel.CodeRegistration = "0x1234";

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Equal(
            "Provide both CodeRegistration and MetadataRegistration, or leave both empty.",
            viewModel.ValidationMessage);
    }

    [Fact]
    public void StartCommand_RejectsInvalidHexAddress()
    {
        var viewModel = CreateValidDirectViewModel();
        viewModel.ImageBase = "not-hex";

        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.Equal("Image base must be a hexadecimal address.", viewModel.ValidationMessage);
    }

    [Fact]
    public void StartCommand_AcceptsValidRecoveryAddresses()
    {
        var viewModel = CreateValidDirectViewModel();
        viewModel.CodeRegistration = "0x1234";
        viewModel.MetadataRegistration = "ABCD";
        viewModel.ImageBase = "0x100000000";

        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.Null(viewModel.ValidationMessage);
    }

    [Fact]
    public async Task StartBatchCommand_RejectsInvalidRecoveryAddress()
    {
        var package = CreateFile("game.apk");
        var viewModel = new MainWindowViewModel();
        viewModel.AddBatchFiles([package]);
        await WaitForBatchInspectionAsync(viewModel);
        viewModel.CodeRegistration = "invalid";

        Assert.False(viewModel.StartBatchCommand.CanExecute(null));
    }

    [Fact]
    public void LoadState_MigratesOutputContentToLegacyGuiDefaults()
    {
        var viewModel = CreateTestViewModel();

        viewModel.LoadState(new DesktopState { Settings = new DesktopSettings() });

        Assert.True(viewModel.GenerateDumpCs);
        Assert.True(viewModel.GenerateStructures);
        Assert.True(viewModel.GenerateDummyDll);
        Assert.True(viewModel.DumpProperties);
        Assert.True(viewModel.DumpAttributes);
        Assert.True(viewModel.DumpFieldOffsets);
        Assert.True(viewModel.DumpMethodOffsets);
        Assert.True(viewModel.DumpTypeDefIndices);
        Assert.True(viewModel.DummyDllAddToken);
        Assert.Equal(1, viewModel.CaptureState().Settings.ContentDefaultsVersion);
    }

    [Fact]
    public void LoadState_PreservesUserContentChoicesAfterMigration()
    {
        var viewModel = CreateTestViewModel();
        var settings = new DesktopSettings
        {
            ContentDefaultsVersion = 1,
            DumpProperties = false,
            DumpAttributes = false,
            DummyDllAddToken = false
        };

        viewModel.LoadState(new DesktopState { Settings = settings });

        Assert.False(viewModel.DumpProperties);
        Assert.False(viewModel.DumpAttributes);
        Assert.False(viewModel.DummyDllAddToken);
    }

    [Fact]
    public void OpenHistoryOutput_DisablesWhenOutputDirectoryWasDeleted()
    {
        var output = Path.Combine(_directory, "history-output");
        Directory.CreateDirectory(output);
        var entry = new JobHistoryEntry(
            Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            "input.apk", string.Empty, output, "arm64-v8a", true, null, 3);
        var viewModel = CreateTestViewModel();
        viewModel.LoadState(new DesktopState { History = [entry] });
        viewModel.SelectedHistoryItem = Assert.Single(viewModel.JobHistory);

        Assert.True(viewModel.OpenSelectedHistoryOutputCommand.CanExecute(null));

        Directory.Delete(output);
        viewModel.RefreshHistoryOutputAvailability();

        Assert.False(viewModel.OpenSelectedHistoryOutputCommand.CanExecute(null));
        Assert.Equal("Output directory no longer exists", viewModel.SelectedHistoryItem.OutputStatus);
    }

    private MainWindowViewModel CreateValidDirectViewModel() => new()
    {
        BinaryPath = CreateFile("GameAssembly.dll"),
        MetadataPath = CreateFile("global-metadata.dat"),
        OutputPath = Path.Combine(_directory, "output")
    };

    private static MainWindowViewModel CreateTestViewModel() => new(
        new MemoryStateStore(),
        new NoOpPathLauncher(),
        new DiagnosticExportService());

    private string CreateFile(string name)
    {
        var path = Path.Combine(_directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0]);
        return path;
    }

    private string CreatePackage(
        string name,
        bool isIos = false,
        IReadOnlyList<string> androidArchitectures = null)
    {
        var path = Path.Combine(_directory, name);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        AddArchiveEntry(archive, "assets/bin/Data/Managed/Metadata/global-metadata.dat");
        if (isIos)
            AddArchiveEntry(archive, "Payload/Game.app/Frameworks/UnityFramework.framework/UnityFramework");
        else
            foreach (var architecture in androidArchitectures ?? ["arm64-v8a"])
                AddArchiveEntry(archive, $"lib/{architecture}/libil2cpp.so");
        return path;
    }

    private static void AddArchiveEntry(ZipArchive archive, string name)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.WriteByte(1);
    }

    private static async Task WaitForBatchInspectionAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.BatchItems.Any(item => !item.IsInspectionComplete) && DateTime.UtcNow < timeout)
            await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    private static async Task WaitForPackageInspectionAsync(MainWindowViewModel viewModel)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (!viewModel.IsPackageInspectionComplete && DateTime.UtcNow < timeout)
            await Task.Delay(10, TestContext.Current.CancellationToken);
    }

    public void Dispose() => Directory.Delete(_directory, true);

    private sealed class MemoryStateStore : IDesktopStateStore
    {
        public DesktopState Load() => new();
        public void Save(DesktopState state) { }
    }

    private sealed class NoOpPathLauncher : IPathLauncher
    {
        public bool TryOpenDirectory(string path) => true;
    }
}
