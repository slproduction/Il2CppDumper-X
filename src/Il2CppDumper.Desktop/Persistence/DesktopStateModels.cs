namespace Il2CppDumper.Desktop.Persistence;

using Il2CppDumper.Desktop.Updates;

public sealed record DesktopState
{
    public int Version { get; init; } = 1;
    public DesktopSettings Settings { get; init; } = new();
    public WindowPlacement Window { get; init; }
    public IReadOnlyList<JobHistoryEntry> History { get; init; } = [];
}

public sealed record WindowPlacement(int X, int Y, double Width, double Height, bool IsMaximized);

public sealed record DesktopSettings
{
    public int ContentDefaultsVersion { get; init; }
    public int SelectedPage { get; init; }
    public bool DetectedPackageMode { get; init; }
    public string PackagePath { get; init; } = string.Empty;
    public string BinaryPath { get; init; } = string.Empty;
    public string MetadataPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = string.Empty;
    public bool GenerateDumpCs { get; init; } = true;
    public bool GenerateStructures { get; init; } = true;
    public bool GenerateDummyDll { get; init; } = true;
    public bool FastMode { get; init; }
    public string AndroidArchitecture { get; init; } = "All detected";
    public string WorkerThreads { get; init; } = "Auto";
    public string CodeRegistration { get; init; } = string.Empty;
    public string MetadataRegistration { get; init; } = string.Empty;
    public string ImageBase { get; init; } = string.Empty;
    public bool DumpProperties { get; init; } = true;
    public bool DumpAttributes { get; init; } = true;
    public bool DumpFieldOffsets { get; init; } = true;
    public bool DumpMethodOffsets { get; init; } = true;
    public bool DumpTypeDefIndices { get; init; } = true;
    public bool DummyDllAddToken { get; init; } = true;
    public bool BatchStopOnError { get; init; }
    public string BatchParallelism { get; init; } = "1";
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
    public string SkippedUpdateVersion { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedAnalysisScripts { get; init; } = [];
}

public sealed record JobHistoryEntry(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string InputPath,
    string MetadataPath,
    string OutputDirectory,
    string Architecture,
    bool Success,
    string Error,
    int ArtifactCount);
