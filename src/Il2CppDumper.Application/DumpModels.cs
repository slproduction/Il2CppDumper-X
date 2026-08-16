using Il2CppDumper.Packages;

namespace Il2CppDumper.Application;

public enum DumpStage
{
    Validating,
    Extracting,
    ReadingMetadata,
    ReadingBinary,
    Searching,
    GeneratingDump,
    GeneratingStructures,
    GeneratingDummyDll,
    ExportingScripts,
    Completed
}

public sealed record DumpProgress(
    DumpStage Stage,
    string Message,
    DiagnosticLevel Level = DiagnosticLevel.Information,
    string JobName = null);

public sealed record DumpOptions
{
    public bool GenerateDumpCs { get; init; } = true;
    public bool GenerateStructures { get; init; } = true;
    public bool GenerateDummyDll { get; init; } = true;
    public IReadOnlySet<string> AnalysisScripts { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool FastMode { get; init; }
    public int WorkerThreads { get; init; }
    public ulong? CodeRegistration { get; init; }
    public ulong? MetadataRegistration { get; init; }
    public ulong? ImageBase { get; init; }
    public Config Core { get; init; } = new();
}

public sealed record DumpRequest
{
    public string BinaryPath { get; init; }
    public string MetadataPath { get; init; }
    public string PackagePath { get; init; }
    public required string OutputDirectory { get; init; }
    public DumpOptions Options { get; init; } = new();
    public PackageOptions PackageOptions { get; init; } = new();

    public bool IsPackage => !string.IsNullOrWhiteSpace(PackagePath);
}

public sealed record DumpArtifact(string Path, string Kind);

public sealed record DumpJobResult(
    string Name,
    string Architecture,
    string OutputDirectory,
    bool Success,
    string Error,
    IReadOnlyList<DumpArtifact> Artifacts);

public sealed record DumpResult(IReadOnlyList<DumpJobResult> Jobs)
{
    public bool Success => Jobs.Count > 0 && Jobs.All(job => job.Success);
}

public sealed record BatchRequest(
    IReadOnlyList<DumpRequest> Jobs,
    bool ContinueOnError = true,
    int MaxDegreeOfParallelism = 1);

public sealed record BatchResult(IReadOnlyList<DumpJobResult> Jobs)
{
    public int Completed => Jobs.Count(job => job.Success);
    public int Failed => Jobs.Count - Completed;
}
