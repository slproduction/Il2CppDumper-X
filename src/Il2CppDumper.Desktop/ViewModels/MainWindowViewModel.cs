using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Il2CppDumper.Application;
using Il2CppDumper.Desktop.Diagnostics;
using Il2CppDumper.Desktop.Persistence;
using Il2CppDumper.Desktop.Shell;
using Il2CppDumper.Desktop.Updates;
using Il2CppDumper.Packages;

namespace Il2CppDumper.Desktop.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly DumpService _service = new();
    private readonly PackageResolver _packageResolver = new();
    private readonly GitHubUpdateService _updates = new();
    private readonly IDesktopStateStore _stateStore;
    private readonly IPathLauncher _pathLauncher;
    private readonly DiagnosticExportService _diagnosticExport;
    private CancellationTokenSource _cancellation;
    private CancellationTokenSource _packageInspectionCancellation;
    private AvailableUpdate _availableUpdate;

    public WindowPlacement WindowPlacement { get; private set; }

    public MainWindowViewModel()
        : this(new JsonDesktopStateStore(), new ProcessPathLauncher(), new DiagnosticExportService())
    {
    }

    public MainWindowViewModel(IDesktopStateStore stateStore, IPathLauncher pathLauncher, DiagnosticExportService diagnosticExport)
    {
        _stateStore = stateStore;
        _pathLauncher = pathLauncher;
        _diagnosticExport = diagnosticExport;
    }

    [ObservableProperty] private int _selectedPage;
    [ObservableProperty] private bool _detectedPackageMode;
    [ObservableProperty] private string _packagePath = string.Empty;
    [ObservableProperty] private string _binaryPath = string.Empty;
    [ObservableProperty] private string _metadataPath = string.Empty;
    [ObservableProperty] private string _outputPath = string.Empty;
    [ObservableProperty] private bool _generateDumpCs = true;
    [ObservableProperty] private bool _generateStructures = true;
    [ObservableProperty] private bool _generateDummyDll = true;
    [ObservableProperty] private bool _fastMode;
    [ObservableProperty] private bool _arm64 = true;
    [ObservableProperty] private bool _armV7;
    [ObservableProperty] private bool _x64;
    [ObservableProperty] private bool _x86;
    [ObservableProperty] private string _androidArchitecture = "All detected";
    [ObservableProperty] private bool _isAndroidPackage;
    [ObservableProperty] private bool _isPackageInspectionComplete;
    [ObservableProperty] private bool _isPackageInspectionValid;
    [ObservableProperty] private string _packageInspectionSummary = "Choose a package to inspect its target.";
    [ObservableProperty] private string _workerThreads = "Auto";
    [ObservableProperty] private string _codeRegistration = string.Empty;
    [ObservableProperty] private string _metadataRegistration = string.Empty;
    [ObservableProperty] private string _imageBase = string.Empty;
    [ObservableProperty] private bool _dumpProperties = true;
    [ObservableProperty] private bool _dumpAttributes = true;
    [ObservableProperty] private bool _dumpFieldOffsets = true;
    [ObservableProperty] private bool _dumpMethodOffsets = true;
    [ObservableProperty] private bool _dumpTypeDefIndices = true;
    [ObservableProperty] private bool _dummyDllAddToken = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _stage = "Ready";
    [ObservableProperty] private string _status = "Add an executable and metadata pair, or switch to package mode.";
    [ObservableProperty] private string _logText = string.Empty;
    [ObservableProperty] private string _updateStatus = $"Version {GitHubUpdateService.CurrentVersion().ToString(3)}";
    [ObservableProperty] private UpdateChannel _updateChannel = UpdateChannel.Stable;
    [ObservableProperty] private string _skippedUpdateVersion = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _isUpdateAvailable;
    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private string _releaseNotes = string.Empty;
    [ObservableProperty] private bool _isUpdateDetailsVisible;
    [ObservableProperty] private bool _isLogsVisible;
    [ObservableProperty] private BatchItemViewModel _selectedBatchItem;
    [ObservableProperty] private JobHistoryItemViewModel _selectedHistoryItem;
    [ObservableProperty] private bool _batchStopOnError;
    [ObservableProperty] private string _batchParallelism = "1";

    public ObservableCollection<BatchItemViewModel> BatchItems { get; } = [];
    public ObservableCollection<AnalysisScriptViewModel> AnalysisScripts { get; } =
        new(AnalysisScriptCatalog.All.Select(script => new AnalysisScriptViewModel(script)));
    public ObservableCollection<string> AndroidArchitectureOptions { get; } = ["All detected"];
    public IReadOnlyList<string> WorkerThreadOptions { get; } = ["Auto", "1", "2", "4", "8"];
    public IReadOnlyList<string> BatchParallelismOptions { get; } = ["1", "2", "3", "4"];
    public IReadOnlyList<UpdateChannel> UpdateChannelOptions { get; } = [UpdateChannel.Stable, UpdateChannel.Prerelease];
    public ObservableCollection<ActivityItem> ActivityItems { get; } = [];
    public ObservableCollection<JobHistoryItemViewModel> JobHistory { get; } = [];
    public bool CanOpenOutput => Directory.Exists(OutputPath);
    public bool CanExportDiagnostics => ActivityItems.Count > 0;
    public string BatchSummary => BatchItems.Count == 0 ? "Queue is empty" : $"{BatchItems.Count} package(s) queued";
    public string HistorySummary => JobHistory.Count == 0 ? "No completed jobs yet" : $"{JobHistory.Count} recent job(s)";
    public bool HasHistory => JobHistory.Count > 0;
    public string ActivitySummary => ActivityItems.Count == 0 ? "No log entries" : $"{ActivityItems.Count} log entries";
    public string ActivityBadge => $"{ActivityItems.Count} events";
    public bool HasActivity => ActivityItems.Count > 0;
    public bool HasBatchSelection => SelectedBatchItem is not null;
    public bool HasBatchItems => BatchItems.Count > 0;
    public string LogsButtonText => IsLogsVisible ? "Close logs" : "Open logs";

    partial void OnSelectedPageChanged(int value)
    {
        OnPropertyChanged(nameof(IsDumpPage));
        OnPropertyChanged(nameof(IsBatchPage));
        OnPropertyChanged(nameof(IsHistoryPage));
        OnPropertyChanged(nameof(IsSettingsPage));
    }

    public bool IsDumpPage => SelectedPage == 0;
    public bool IsBatchPage => SelectedPage == 1;
    public bool IsHistoryPage => SelectedPage == 2;
    public bool IsSettingsPage => SelectedPage == 3;
    public bool IsPackageMode => DetectedPackageMode;
    public bool IsDirectMode => !IsPackageMode;
    public string ValidationMessage => GetValidationMessage();
    public string DisplayStatus => ValidationMessage ?? Status;
    public string ValidationState => IsRunning
        ? Stage
        : ValidationMessage is null
            ? "Ready"
            : IsPackageMode && !IsPackageInspectionComplete && File.Exists(PackagePath)
                ? "Inspecting"
                : "Action required";
    public bool CanCheckForUpdates => !IsCheckingForUpdates && !IsInstallingUpdate && !IsRunning;

    partial void OnDetectedPackageModeChanged(bool value)
    {
        OnPropertyChanged(nameof(PackageMode));
        OnPropertyChanged(nameof(ValidationState));
        BeginPackageInspection(PackagePath);
        RefreshInputState();
    }
    partial void OnPackagePathChanged(string value)
    {
        RefreshInputState();
        BeginPackageInspection(value);
    }
    partial void OnBinaryPathChanged(string value) => RefreshValidation();
    partial void OnMetadataPathChanged(string value) => RefreshValidation();
    partial void OnOutputPathChanged(string value) => RefreshValidation();
    partial void OnAndroidArchitectureChanged(string value) => RefreshValidation();
    partial void OnCodeRegistrationChanged(string value) => RefreshValidation();
    partial void OnMetadataRegistrationChanged(string value) => RefreshValidation();
    partial void OnImageBaseChanged(string value) => RefreshValidation();
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(DisplayStatus));
    partial void OnStageChanged(string value) => OnPropertyChanged(nameof(ValidationState));
    partial void OnIsRunningChanged(bool value)
    {
        RefreshValidation();
        StartBatchCommand.NotifyCanExecuteChanged();
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
        MoveSelectedBatchUpCommand.NotifyCanExecuteChanged();
        MoveSelectedBatchDownCommand.NotifyCanExecuteChanged();
        RemoveSelectedBatchCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsCheckingForUpdatesChanged(bool value) => RefreshUpdateCommands();
    partial void OnIsInstallingUpdateChanged(bool value) => RefreshUpdateCommands();
    partial void OnIsUpdateAvailableChanged(bool value) => InstallUpdateCommand.NotifyCanExecuteChanged();
    partial void OnIsLogsVisibleChanged(bool value) => OnPropertyChanged(nameof(LogsButtonText));
    partial void OnSelectedBatchItemChanged(BatchItemViewModel value)
    {
        OnPropertyChanged(nameof(HasBatchSelection));
        MoveSelectedBatchUpCommand.NotifyCanExecuteChanged();
        MoveSelectedBatchDownCommand.NotifyCanExecuteChanged();
        RemoveSelectedBatchCommand.NotifyCanExecuteChanged();
    }
    partial void OnBatchStopOnErrorChanged(bool value) => StartBatchCommand.NotifyCanExecuteChanged();
    partial void OnBatchParallelismChanged(string value) => StartBatchCommand.NotifyCanExecuteChanged();
    partial void OnUpdateChannelChanged(UpdateChannel value) => _ = CheckForUpdatesAsync(false);

    public bool PackageMode
    {
        get => DetectedPackageMode;
        set => DetectedPackageMode = value;
    }

    [RelayCommand] private void ShowDump() => SelectedPage = 0;
    [RelayCommand] private void ShowBatch() => SelectedPage = 1;
    [RelayCommand]
    private void ShowHistory()
    {
        SelectedPage = 2;
        RefreshHistoryOutputAvailability();
    }
    [RelayCommand] private void ShowSettings() => SelectedPage = 3;
    [RelayCommand] private void SelectPackageMode() => PackageMode = true;
    [RelayCommand] private void SelectDirectMode() => PackageMode = false;
    [RelayCommand] private void ShowLogs() => IsLogsVisible = !IsLogsVisible;
    [RelayCommand] private void DismissLogs() => IsLogsVisible = false;
    [RelayCommand]
    private void ClearLogs()
    {
        ActivityItems.Clear();
        LogText = string.Empty;
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ActivityBadge));
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(CanExportDiagnostics));
    }

    public async Task CheckForUpdatesOnStartupAsync()
    {
        await Task.Delay(750);
        await CheckForUpdatesAsync(false);
    }

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private Task CheckForUpdatesAsync() => CheckForUpdatesAsync(true);

    private async Task CheckForUpdatesAsync(bool reportUpToDate)
    {
        IsCheckingForUpdates = true;
        if (reportUpToDate) UpdateStatus = "Checking for updates...";
        try
        {
            _availableUpdate = await _updates.CheckAsync(UpdateChannel, SkippedUpdateVersion);
            IsUpdateAvailable = _availableUpdate is not null;
            ReleaseNotes = _availableUpdate?.Release.Body ?? string.Empty;
            IsUpdateDetailsVisible = _availableUpdate is not null;
            UpdateStatus = _availableUpdate is null
                ? reportUpToDate ? "You are using the latest available version." : UpdateStatus
                : $"Version {_availableUpdate.Version} is available.";
        }
        catch (Exception exception)
        {
            if (reportUpToDate) UpdateStatus = $"Update check failed: {exception.Message}";
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private async Task InstallUpdateAsync()
    {
        IsInstallingUpdate = true;
        try
        {
            var progress = new Progress<string>(message => UpdateStatus = message);
            await _updates.InstallAsync(_availableUpdate, progress);
            RequestShutdown?.Invoke();
        }
        catch (Exception exception)
        {
            UpdateStatus = $"Update installation failed: {exception.Message}";
            IsInstallingUpdate = false;
        }
    }

    private bool CanInstallUpdate() => IsUpdateAvailable && !IsCheckingForUpdates && !IsInstallingUpdate && !IsRunning;
    public event Action RequestShutdown;

    [RelayCommand]
    private void SkipUpdate()
    {
        if (_availableUpdate is null) return;
        SkippedUpdateVersion = _availableUpdate.Release.Version;
        _availableUpdate = null;
        IsUpdateAvailable = false;
        IsUpdateDetailsVisible = false;
        UpdateStatus = $"Version {SkippedUpdateVersion} skipped.";
        SaveState();
    }

    [RelayCommand] private void DismissUpdateDetails() => IsUpdateDetailsVisible = false;

    public void LoadState(DesktopState state)
    {
        var settings = state?.Settings ?? new DesktopSettings();
        WindowPlacement = state?.Window;
        SelectedPage = settings.SelectedPage;
        DetectedPackageMode = settings.DetectedPackageMode;
        PackagePath = settings.PackagePath;
        BinaryPath = settings.BinaryPath;
        MetadataPath = settings.MetadataPath;
        OutputPath = settings.OutputPath;
        GenerateDumpCs = settings.GenerateDumpCs;
        GenerateStructures = settings.GenerateStructures;
        GenerateDummyDll = settings.GenerateDummyDll;
        FastMode = settings.FastMode;
        AndroidArchitecture = settings.AndroidArchitecture;
        WorkerThreads = settings.WorkerThreads;
        CodeRegistration = settings.CodeRegistration;
        MetadataRegistration = settings.MetadataRegistration;
        ImageBase = settings.ImageBase;
        DumpProperties = settings.ContentDefaultsVersion == 0 || settings.DumpProperties;
        DumpAttributes = settings.ContentDefaultsVersion == 0 || settings.DumpAttributes;
        DumpFieldOffsets = settings.DumpFieldOffsets;
        DumpMethodOffsets = settings.DumpMethodOffsets;
        DumpTypeDefIndices = settings.DumpTypeDefIndices;
        DummyDllAddToken = settings.DummyDllAddToken;
        BatchStopOnError = settings.BatchStopOnError;
        BatchParallelism = settings.BatchParallelism;
        UpdateChannel = settings.UpdateChannel;
        SkippedUpdateVersion = settings.SkippedUpdateVersion;
        foreach (var script in AnalysisScripts)
            script.IsSelected = settings.SelectedAnalysisScripts.Contains(script.FileName, StringComparer.OrdinalIgnoreCase);
        JobHistory.Clear();
        foreach (var entry in state?.History ?? []) JobHistory.Add(new JobHistoryItemViewModel(entry));
        RefreshInputState();
    }

    public void SaveState()
    {
        try { _stateStore.Save(CaptureState()); } catch { }
    }

    public DesktopState CaptureState() => new()
    {
        Window = WindowPlacement,
        Settings = new DesktopSettings
        {
            ContentDefaultsVersion = 1,
            SelectedPage = SelectedPage,
            DetectedPackageMode = DetectedPackageMode,
            PackagePath = PackagePath,
            BinaryPath = BinaryPath,
            MetadataPath = MetadataPath,
            OutputPath = OutputPath,
            GenerateDumpCs = GenerateDumpCs,
            GenerateStructures = GenerateStructures,
            GenerateDummyDll = GenerateDummyDll,
            FastMode = FastMode,
            AndroidArchitecture = AndroidArchitecture,
            WorkerThreads = WorkerThreads,
            CodeRegistration = CodeRegistration,
            MetadataRegistration = MetadataRegistration,
            ImageBase = ImageBase,
            DumpProperties = DumpProperties,
            DumpAttributes = DumpAttributes,
            DumpFieldOffsets = DumpFieldOffsets,
            DumpMethodOffsets = DumpMethodOffsets,
            DumpTypeDefIndices = DumpTypeDefIndices,
            DummyDllAddToken = DummyDllAddToken,
            BatchStopOnError = BatchStopOnError,
            BatchParallelism = BatchParallelism,
            UpdateChannel = UpdateChannel,
            SkippedUpdateVersion = SkippedUpdateVersion,
            SelectedAnalysisScripts = AnalysisScripts.Where(script => script.IsSelected).Select(script => script.FileName).ToArray()
        },
        History = JobHistory.Select(item => item.Entry).ToArray()
    };

    public void SetWindowPlacement(WindowPlacement placement) => WindowPlacement = placement;

    [RelayCommand] private void OpenCurrentOutput() => _pathLauncher.TryOpenDirectory(OutputPath);
    [RelayCommand] private void OpenHistoryOutput(JobHistoryItemViewModel item) => TryOpenHistoryOutput(item);
    [RelayCommand] private void ClearHistory()
    {
        JobHistory.Clear();
        OnPropertyChanged(nameof(HistorySummary));
        OnPropertyChanged(nameof(HasHistory));
        SaveState();
    }
    partial void OnSelectedHistoryItemChanged(JobHistoryItemViewModel value)
    {
        value?.RefreshOutputAvailability();
        OpenSelectedHistoryOutputCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedHistoryOutput))]
    private void OpenSelectedHistoryOutput() => TryOpenHistoryOutput(SelectedHistoryItem);

    private bool CanOpenSelectedHistoryOutput() => SelectedHistoryItem?.IsOutputAvailable == true;

    private void TryOpenHistoryOutput(JobHistoryItemViewModel item)
    {
        item?.RefreshOutputAvailability();
        if (item is null || !item.IsOutputAvailable || !_pathLauncher.TryOpenDirectory(item.OutputDirectory))
        {
            Status = "The output directory no longer exists.";
            OpenSelectedHistoryOutputCommand.NotifyCanExecuteChanged();
        }
    }

    public void RefreshHistoryOutputAvailability()
    {
        foreach (var item in JobHistory) item.RefreshOutputAvailability();
        OpenSelectedHistoryOutputCommand.NotifyCanExecuteChanged();
    }
    public void ExportDiagnostics(string path) => _diagnosticExport.Export(path, ActivityItems, CaptureState());

    private void AddHistory(IEnumerable<DumpJobResult> results, DateTimeOffset startedAt, string inputPath, string metadataPath)
    {
        foreach (var result in results)
        {
            JobHistory.Insert(0, new JobHistoryItemViewModel(new JobHistoryEntry(
                Guid.NewGuid(), startedAt, DateTimeOffset.UtcNow, inputPath, metadataPath,
                result.OutputDirectory, result.Architecture, result.Success, result.Error, result.Artifacts.Count)));
        }
        while (JobHistory.Count > 50) JobHistory.RemoveAt(JobHistory.Count - 1);
        OnPropertyChanged(nameof(HistorySummary));
        OnPropertyChanged(nameof(HasHistory));
    }

    private void RefreshUpdateCommands()
    {
        OnPropertyChanged(nameof(CanCheckForUpdates));
        CheckForUpdatesCommand.NotifyCanExecuteChanged();
        InstallUpdateCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        var startedAt = DateTimeOffset.UtcNow;
        var inputPath = IsPackageMode ? PackagePath : BinaryPath;
        _cancellation = new CancellationTokenSource();
        IsRunning = true;
        LogText = string.Empty;
        ActivityItems.Clear();
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ActivityBadge));
        OnPropertyChanged(nameof(HasActivity));
        Status = "Preparing input";
        try
        {
            var result = await _service.DumpAsync(CreateRequest(), CreateProgress(), _cancellation.Token);
            AddHistory(result.Jobs, startedAt, inputPath, IsPackageMode ? string.Empty : MetadataPath);
            Status = result.Success ? $"Completed. {result.Jobs.Sum(job => job.Artifacts.Count)} artifacts created." : "Completed with errors.";
        }
        catch (OperationCanceledException)
        {
            Status = "Operation cancelled.";
        }
        catch (Exception exception)
        {
            Status = exception.Message;
            AppendLog("ERROR", exception.ToString());
        }
        finally
        {
            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
            StartCommand.NotifyCanExecuteChanged();
            SaveState();
        }
    }

    private bool CanStart() => !IsRunning && GetValidationMessage() is null;

    [RelayCommand] private void Cancel() => _cancellation?.Cancel();

    [RelayCommand]
    private void ClearBatch()
    {
        BatchItems.Clear();
        SelectedBatchItem = null;
        StartBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BatchSummary));
        OnPropertyChanged(nameof(HasBatchItems));
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedBatch))]
    private void RemoveSelectedBatch()
    {
        if (SelectedBatchItem is null || IsRunning) return;
        BatchItems.Remove(SelectedBatchItem);
        SelectedBatchItem = null;
        StartBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BatchSummary));
        OnPropertyChanged(nameof(HasBatchItems));
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedBatch))]
    private void MoveSelectedBatchUp()
    {
        var index = SelectedBatchItem is null ? -1 : BatchItems.IndexOf(SelectedBatchItem);
        if (index <= 0 || IsRunning) return;
        BatchItems.Move(index, index - 1);
    }

    [RelayCommand(CanExecute = nameof(CanModifySelectedBatch))]
    private void MoveSelectedBatchDown()
    {
        var index = SelectedBatchItem is null ? -1 : BatchItems.IndexOf(SelectedBatchItem);
        if (index < 0 || index >= BatchItems.Count - 1 || IsRunning) return;
        BatchItems.Move(index, index + 1);
    }

    private bool CanModifySelectedBatch() => HasBatchSelection && !IsRunning;

    [RelayCommand]
    private async Task RetryFailedAsync()
    {
        var failed = BatchItems.Where(item => item.IsFailed).ToArray();
        if (failed.Length > 0) await RunBatchAsync(failed);
    }

    [RelayCommand(CanExecute = nameof(CanStartBatch))]
    private async Task StartBatchAsync()
        => await RunBatchAsync(BatchItems.ToArray());

    private async Task RunBatchAsync(IReadOnlyList<BatchItemViewModel> items)
    {
        var startedAt = DateTimeOffset.UtcNow;
        _cancellation = new CancellationTokenSource();
        IsRunning = true;
        LogText = string.Empty;
        ActivityItems.Clear();
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ActivityBadge));
        OnPropertyChanged(nameof(HasActivity));
        try
        {
            foreach (var item in items) item.Status = "Queued";
            var requests = items.Select(item => CreatePackageRequest(
                item.InputPath,
                item.OutputPath)).ToArray();
            var result = await _service.BatchAsync(new BatchRequest(
                requests,
                !BatchStopOnError,
                int.Parse(BatchParallelism, CultureInfo.InvariantCulture)), CreateProgress(), _cancellation.Token);
            foreach (var item in items)
                AddHistory(result.Jobs.Where(job => job.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)), startedAt, item.InputPath, string.Empty);
            foreach (var item in items)
            {
                var jobs = result.Jobs.Where(job => job.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                item.IsFailed = jobs.Length == 0 || jobs.Any(job => !job.Success);
                item.Status = item.IsFailed ? "Failed" : "Completed";
                item.Detail = item.IsFailed ? jobs.FirstOrDefault(job => !job.Success)?.Error?.Split('\n')[0] ?? "No result" : $"{jobs.Sum(job => job.Artifacts.Count)} artifacts";
            }
            Status = $"Batch complete: {result.Completed} completed, {result.Failed} failed.";
        }
        catch (OperationCanceledException)
        {
            Status = "Batch cancelled.";
        }
        finally
        {
            IsRunning = false;
            _cancellation.Dispose();
            _cancellation = null;
            StartBatchCommand.NotifyCanExecuteChanged();
            SaveState();
        }
    }

    private bool CanStartBatch() => !IsRunning && BatchItems.Count > 0 &&
        BatchItems.All(item => item.IsInspectionComplete && item.IsInspectionValid) &&
        !(BatchStopOnError && BatchParallelism != "1") &&
        HaveDistinctBatchOutputs() &&
        GetAdvancedValidationMessage() is null;

    private bool HaveDistinctBatchOutputs()
    {
        var outputs = BatchItems.Select(item => Path.GetFullPath(item.OutputPath).TrimEnd(Path.DirectorySeparatorChar)).ToArray();
        for (var left = 0; left < outputs.Length; left++)
        for (var right = left + 1; right < outputs.Length; right++)
        {
            if (outputs[left].Equals(outputs[right], StringComparison.OrdinalIgnoreCase)) return false;
            if (outputs[left].StartsWith(outputs[right] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            if (outputs[right].StartsWith(outputs[left] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    public void AddBatchFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(PackageResolver.IsSupported))
        {
            if (BatchItems.Any(item => item.InputPath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            var directory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory;
            var item = new BatchItemViewModel(
                path,
                Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(path)}_dumped"));
            BatchItems.Add(item);
            _ = InspectBatchItemAsync(item);
        }
        StartBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(BatchSummary));
        OnPropertyChanged(nameof(HasBatchItems));
    }

    private async Task InspectBatchItemAsync(BatchItemViewModel item)
    {
        try
        {
            item.ApplyInspection(await _packageResolver.InspectAsync(item.InputPath));
        }
        catch (Exception exception)
        {
            item.ApplyInspectionError(exception);
        }
        finally
        {
            StartBatchCommand.NotifyCanExecuteChanged();
        }
    }

    private void BeginPackageInspection(string path)
    {
        _packageInspectionCancellation?.Cancel();
        _packageInspectionCancellation?.Dispose();
        _packageInspectionCancellation = null;
        IsPackageInspectionComplete = false;
        IsPackageInspectionValid = false;
        IsAndroidPackage = false;
        AndroidArchitectureOptions.Clear();
        AndroidArchitectureOptions.Add("All detected");

        if (!IsPackageMode || string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !PackageResolver.IsSupported(path))
        {
            PackageInspectionSummary = IsPackageMode
                ? "Choose a supported package to inspect its target."
                : "Architecture is detected from the selected executable.";
            RefreshValidation();
            return;
        }

        PackageInspectionSummary = "Inspecting package...";
        _packageInspectionCancellation = new CancellationTokenSource();
        _ = InspectSelectedPackageAsync(path, _packageInspectionCancellation.Token);
        RefreshValidation();
    }

    private async Task InspectSelectedPackageAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var inspection = await _packageResolver.InspectAsync(path, cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsPackageMode ||
                !PackagePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return;

            IsPackageInspectionComplete = true;
            IsPackageInspectionValid = inspection.IsComplete;
            IsAndroidPackage = IsPackageMode && inspection.ContainerType != PackageContainerType.Ipa &&
                inspection.Architectures.Any(IsAndroidArchitecture);
            foreach (var architecture in inspection.Architectures.Where(IsAndroidArchitecture))
                AndroidArchitectureOptions.Add(architecture);
            if (!AndroidArchitectureOptions.Contains(AndroidArchitecture))
                AndroidArchitecture = "All detected";
            var architectures = inspection.Architectures.Count == 0
                ? "No executable detected"
                : string.Join(", ", inspection.Architectures);
            PackageInspectionSummary = inspection.IsComplete
                ? $"{inspection.ContainerType} · {architectures}"
                : string.Join(" ", inspection.Warnings);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            if (cancellationToken.IsCancellationRequested || !IsPackageMode ||
                !PackagePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return;
            IsPackageInspectionComplete = true;
            IsPackageInspectionValid = false;
            PackageInspectionSummary = $"Inspection failed: {exception.Message}";
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested && IsPackageMode &&
                PackagePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                RefreshInputState();
        }
    }

    private static bool IsAndroidArchitecture(string architecture) =>
        architecture is "arm64-v8a" or "armeabi-v7a" or "x86_64" or "x86" or "unknown";

    public void UseDroppedFiles(IReadOnlyList<string> paths)
    {
        var directories = paths.Where(Directory.Exists).ToArray();
        var files = paths.Where(File.Exists)
            .Concat(directories.SelectMany(EnumeratePackages))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
            return;

        var packages = files.Where(PackageResolver.IsSupported).ToArray();
        var metadata = files.FirstOrDefault(IsMetadataFile);
        var binary = files.FirstOrDefault(path => !PackageResolver.IsSupported(path) && !IsMetadataFile(path));

        if (SelectedPage == 1 || directories.Length > 0 || packages.Length > 1)
        {
            SelectedPage = 1;
            AddBatchFiles(packages);
            return;
        }

        SelectedPage = 0;
        if (packages.Length == 1)
        {
            DetectedPackageMode = true;
            PackagePath = packages[0];
            SetSuggestedOutput(packages[0]);
            Status = "Package detected. Review options and start the dump.";
        }
        else
        {
            DetectedPackageMode = false;
            if (metadata is not null)
                MetadataPath = metadata;
            if (binary is not null)
                BinaryPath = binary;
            SetSuggestedOutput(binary ?? metadata);
            Status = metadata is not null && binary is not null
                ? "Binary and metadata detected. Ready to dump."
                : "Direct input detected. Add the matching binary or metadata file.";
        }
        RefreshInputState();
    }

    private static IEnumerable<string> EnumeratePackages(string directory) =>
        Directory.EnumerateFiles(directory, "*", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true
        }).Where(PackageResolver.IsSupported);

    private DumpRequest CreateRequest() => IsPackageMode
        ? CreatePackageRequest(PackagePath, OutputPath)
        : new DumpRequest
        {
            BinaryPath = BinaryPath,
            MetadataPath = MetadataPath,
            OutputDirectory = OutputPath,
            Options = CreateOptions()
        };

    private DumpRequest CreatePackageRequest(string package, string output) => new()
    {
        PackagePath = package,
        OutputDirectory = output,
        Options = CreateOptions(),
        PackageOptions = new PackageOptions { Architectures = GetArchitectures() }
    };

    private DumpOptions CreateOptions() => new()
    {
        GenerateDumpCs = GenerateDumpCs,
        GenerateStructures = GenerateStructures,
        GenerateDummyDll = GenerateDummyDll,
        FastMode = FastMode,
        WorkerThreads = WorkerThreads == "Auto" ? 0 : int.Parse(WorkerThreads, CultureInfo.InvariantCulture),
        CodeRegistration = ParseHex(CodeRegistration),
        MetadataRegistration = ParseHex(MetadataRegistration),
        ImageBase = ParseHex(ImageBase),
        AnalysisScripts = AnalysisScripts.Where(script => script.IsSelected).Select(script => script.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase),
        Core = new Config
        {
            GenerateStruct = GenerateStructures,
            GenerateDummyDll = GenerateDummyDll,
            DumpProperty = DumpProperties,
            DumpAttribute = DumpAttributes,
            DumpFieldOffset = DumpFieldOffsets,
            DumpMethodOffset = DumpMethodOffsets,
            DumpTypeDefIndex = DumpTypeDefIndices,
            DummyDllAddToken = DummyDllAddToken
        }
    };

    private static ulong? ParseHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        return ulong.Parse(normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    }

    private static bool IsValidHex(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) normalized = normalized[2..];
        return normalized.Length > 0 && ulong.TryParse(
            normalized, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out _);
    }

    private IReadOnlySet<string> GetArchitectures()
    {
        if (AndroidArchitecture.Equals("All detected", StringComparison.OrdinalIgnoreCase))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return new HashSet<string>([AndroidArchitecture], StringComparer.OrdinalIgnoreCase);
    }

    private IProgress<DumpProgress> CreateProgress() => new Progress<DumpProgress>(progress =>
    {
        Stage = progress.Stage.ToString();
        Status = progress.Message;
        AppendLog(progress.Level.ToString().ToUpperInvariant(), progress.Message, progress.JobName);
        if (!string.IsNullOrEmpty(progress.JobName))
        {
            var item = BatchItems.FirstOrDefault(candidate => candidate.Name.Equals(progress.JobName, StringComparison.OrdinalIgnoreCase));
            if (item is not null)
            {
                item.Status = progress.Stage == DumpStage.Completed ? "Completed" : "Running";
                item.Detail = progress.Message;
            }
        }
    });

    private void AppendLog(string level, string message, string job = null)
    {
        var item = new ActivityItem(DateTime.Now.ToString("HH:mm:ss"), level, message, job);
        ActivityItems.Add(item);
        OnPropertyChanged(nameof(ActivitySummary));
        OnPropertyChanged(nameof(ActivityBadge));
        OnPropertyChanged(nameof(HasActivity));
        OnPropertyChanged(nameof(CanExportDiagnostics));
        LogText += item.ToString() + Environment.NewLine;
    }

    public void SelectPackage(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        DetectedPackageMode = true;
        PackagePath = path;
        SetSuggestedOutput(path);
        RefreshInputState();
    }

    public void SelectBinary(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        DetectedPackageMode = false;
        BinaryPath = path;
        SetSuggestedOutput(path);
        RefreshInputState();
    }

    public void SelectMetadata(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        DetectedPackageMode = false;
        MetadataPath = path;
        SetSuggestedOutput(path);
        RefreshInputState();
    }

    private void SetSuggestedOutput(string inputPath)
    {
        if (!string.IsNullOrWhiteSpace(OutputPath) || string.IsNullOrWhiteSpace(inputPath)) return;
        var directory = Path.GetDirectoryName(inputPath);
        if (string.IsNullOrEmpty(directory)) return;
        OutputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(inputPath)}_dumped");
    }

    private static bool IsMetadataFile(string path) =>
        Path.GetFileName(path).Equals("global-metadata.dat", StringComparison.OrdinalIgnoreCase) ||
        Path.GetFileName(path).Contains("metadata", StringComparison.OrdinalIgnoreCase) &&
        Path.GetExtension(path).Equals(".dat", StringComparison.OrdinalIgnoreCase);

    private string GetValidationMessage()
    {
        var advancedValidationMessage = GetAdvancedValidationMessage();
        if (advancedValidationMessage is not null) return advancedValidationMessage;
        if (string.IsNullOrWhiteSpace(OutputPath)) return "Choose an output directory.";
        if (IsPackageMode)
        {
            if (!File.Exists(PackagePath) || !PackageResolver.IsSupported(PackagePath))
                return "Choose a supported application package.";
            if (!IsPackageInspectionComplete)
                return "Inspecting the selected package.";
            if (!IsPackageInspectionValid)
                return "The package does not contain a complete IL2CPP input set.";
            return null;
        }
        if (!File.Exists(BinaryPath)) return "Choose an IL2CPP executable.";
        if (!File.Exists(MetadataPath)) return "Choose global-metadata.dat.";
        return null;
    }

    private string GetAdvancedValidationMessage()
    {
        if (!IsValidHex(CodeRegistration)) return "CodeRegistration must be a hexadecimal address.";
        if (!IsValidHex(MetadataRegistration)) return "MetadataRegistration must be a hexadecimal address.";
        if (!IsValidHex(ImageBase)) return "Image base must be a hexadecimal address.";
        if (string.IsNullOrWhiteSpace(CodeRegistration) != string.IsNullOrWhiteSpace(MetadataRegistration))
            return "Provide both CodeRegistration and MetadataRegistration, or leave both empty.";
        return null;
    }

    private void RefreshInputState()
    {
        OnPropertyChanged(nameof(IsPackageMode));
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsAndroidPackage));
        OnPropertyChanged(nameof(ValidationState));
        RefreshValidation();
    }

    private void RefreshValidation()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(DisplayStatus));
        OnPropertyChanged(nameof(ValidationState));
        StartCommand.NotifyCanExecuteChanged();
        StartBatchCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanOpenOutput));
        OnPropertyChanged(nameof(CanExportDiagnostics));
    }
}

public sealed record ActivityItem(string Time, string Level, string Message, string JobName)
{
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.Now;
    public string LevelText => Level;
    public override string ToString() => $"{Time}  {Level,-11}{(string.IsNullOrEmpty(JobName) ? string.Empty : $"[{JobName}] ")}{Message}";
}
