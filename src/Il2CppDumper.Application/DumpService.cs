using System.Text;
using Il2CppDumper.Packages;

namespace Il2CppDumper.Application;

public sealed class DumpService
{
    private readonly PackageResolver _packages = new();

    public async Task<DumpResult> DumpAsync(
        DumpRequest request,
        IProgress<DumpProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        Validate(request);
        Directory.CreateDirectory(request.OutputDirectory);

        if (!request.IsPackage)
        {
            var result = await RunJobAsync(
                Path.GetFileNameWithoutExtension(request.BinaryPath),
                string.Empty,
                request.BinaryPath,
                request.MetadataPath,
                request.OutputDirectory,
                request.Options,
                progress,
                cancellationToken);
            return new DumpResult([result]);
        }

        progress?.Report(new DumpProgress(DumpStage.Extracting, $"Opening {Path.GetFileName(request.PackagePath)}"));
        var packageProgress = new Progress<string>(message =>
            progress?.Report(new DumpProgress(DumpStage.Extracting, message)));
        await using var prepared = await _packages.PrepareAsync(
            request.PackagePath,
            request.PackageOptions,
            packageProgress,
            cancellationToken);

        var results = new List<DumpJobResult>();
        foreach (var input in prepared.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var output = string.IsNullOrEmpty(input.RelativeOutputPath)
                ? request.OutputDirectory
                : Path.Combine(request.OutputDirectory, input.RelativeOutputPath);
            results.Add(await RunJobAsync(
                input.Name,
                input.Architecture,
                input.BinaryPath,
                input.MetadataPath,
                output,
                request.Options,
                progress,
                cancellationToken));
        }

        return new DumpResult(results);
    }

    public async Task<BatchResult> BatchAsync(
        BatchRequest request,
        IProgress<DumpProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (request.MaxDegreeOfParallelism is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(request.MaxDegreeOfParallelism), "Batch parallelism must be between 1 and 4.");
        if (!request.ContinueOnError && request.MaxDegreeOfParallelism > 1)
            throw new ArgumentException("Stop-on-error requires sequential batch processing.");

        if (request.MaxDegreeOfParallelism == 1)
        {
            var sequentialResults = new List<DumpJobResult>();
            foreach (var job in request.Jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var results = await RunBatchJobAsync(job, progress, cancellationToken);
                sequentialResults.AddRange(results);
                if (results.Any(result => !result.Success) && !request.ContinueOnError) break;
            }
            return new BatchResult(sequentialResults);
        }

        using var semaphore = new SemaphoreSlim(request.MaxDegreeOfParallelism);
        var tasks = request.Jobs.Select(async (job, index) =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return (index, Results: await RunBatchJobAsync(job, progress, cancellationToken));
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();
        var completed = await Task.WhenAll(tasks);
        return new BatchResult(completed.OrderBy(item => item.index).SelectMany(item => item.Results).ToArray());
    }

    private async Task<IReadOnlyList<DumpJobResult>> RunBatchJobAsync(
        DumpRequest job,
        IProgress<DumpProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await DumpAsync(job, progress, cancellationToken)).Jobs;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            progress?.Report(new DumpProgress(DumpStage.Completed, exception.Message, DiagnosticLevel.Error, GetRequestName(job)));
            return [new DumpJobResult(GetRequestName(job), string.Empty, job.OutputDirectory, false, exception.Message, [])];
        }
    }

    private static async Task<DumpJobResult> RunJobAsync(
        string name,
        string architecture,
        string binaryPath,
        string metadataPath,
        string outputDirectory,
        DumpOptions options,
        IProgress<DumpProgress> progress,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(() => RunJob(
                name, architecture, binaryPath, metadataPath, outputDirectory, options, progress, cancellationToken), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            progress?.Report(new DumpProgress(DumpStage.Completed, exception.Message, DiagnosticLevel.Error, name));
            return new DumpJobResult(name, architecture, outputDirectory, false, exception.ToString(), []);
        }
    }

    private static DumpJobResult RunJob(
        string name,
        string architecture,
        string binaryPath,
        string metadataPath,
        string outputDirectory,
        DumpOptions options,
        IProgress<DumpProgress> progress,
        CancellationToken cancellationToken)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Directory.CreateDirectory(outputDirectory);
        using var diagnostics = DumperDiagnostics.Push(message =>
            progress?.Report(new DumpProgress(DumpStage.Searching, message.Message, message.Level, name)));

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new DumpProgress(DumpStage.ReadingMetadata, "Reading global metadata", JobName: name));
        var metadata = new Metadata(new MemoryStream(File.ReadAllBytes(metadataPath)));
        DumperDiagnostics.Information("Metadata version: {0}", metadata.Version);
        DumperDiagnostics.Information("Detected Unity version: {0}", UnityVersionMap.GetUnityVersionRange(metadata.Version));

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new DumpProgress(DumpStage.ReadingBinary, "Reading executable", JobName: name));
        var il2Cpp = Il2CppBinaryFactory.Create(File.ReadAllBytes(binaryPath));
        var version = options.Core.ForceIl2CppVersion ? options.Core.ForceVersion : metadata.Version;
        il2Cpp.SetProperties(version, metadata.metadataUsagesCount);

        if (options.ImageBase.HasValue && il2Cpp is ElfBase elf)
        {
            il2Cpp.ImageBase = options.ImageBase.Value;
            il2Cpp.IsDumped = true;
            if (!options.Core.NoRedirectedPointer)
                elf.Reload();
        }

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new DumpProgress(DumpStage.Searching, "Searching registration addresses", JobName: name));
        var initialized = false;
        if (options.CodeRegistration.HasValue && options.MetadataRegistration.HasValue)
        {
            il2Cpp.Init(options.CodeRegistration.Value, options.MetadataRegistration.Value);
            initialized = true;
        }
        else
        {
            initialized = il2Cpp.PlusSearch(
                metadata.methodDefs.Count(method => method.methodIndex >= 0),
                metadata.typeDefs.Length,
                metadata.imageDefs.Length);
            if (!initialized && OperatingSystem.IsWindows() && il2Cpp is PE)
            {
                il2Cpp = PELoader.Load(binaryPath);
                il2Cpp.SetProperties(version, metadata.metadataUsagesCount);
                initialized = il2Cpp.PlusSearch(
                    metadata.methodDefs.Count(method => method.methodIndex >= 0),
                    metadata.typeDefs.Length,
                    metadata.imageDefs.Length);
            }
            initialized = initialized || il2Cpp.Search() || il2Cpp.SymbolSearch();
        }

        if (!initialized)
            throw new InvalidOperationException("Registration addresses could not be detected. Provide CodeRegistration and MetadataRegistration manually.");

        if (il2Cpp.Version >= 27 && il2Cpp.IsDumped)
        {
            var typeDef = metadata.typeDefs[0];
            metadata.ImageBase = il2Cpp.types[typeDef.byvalTypeIndex].data.typeHandle - metadata.header.typeDefinitionsOffset;
        }

        var executor = new Il2CppExecutor(metadata, il2Cpp);
        if (options.GenerateDumpCs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DumpProgress(DumpStage.GeneratingDump, "Generating dump.cs", JobName: name));
            new Il2CppDecompiler(executor).Decompile(options.Core, outputDirectory);
        }
        if (options.GenerateStructures && options.Core.GenerateStruct)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DumpProgress(DumpStage.GeneratingStructures, "Generating structures", JobName: name));
            new StructGenerator(executor).WriteScript(outputDirectory, options.FastMode, options.WorkerThreads);
        }
        if (options.GenerateDummyDll && options.Core.GenerateDummyDll)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DumpProgress(DumpStage.GeneratingDummyDll, "Generating dummy assemblies", JobName: name));
            DummyAssemblyExporter.Export(executor, outputDirectory, options.Core.DummyDllAddToken);
        }
        if (options.AnalysisScripts.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DumpProgress(
                DumpStage.ExportingScripts,
                $"Exporting {options.AnalysisScripts.Count} analysis script(s)",
                JobName: name));
            AnalysisScriptCatalog.Export(options.AnalysisScripts, outputDirectory);
        }

        var artifacts = DiscoverArtifacts(outputDirectory);
        progress?.Report(new DumpProgress(DumpStage.Completed, $"Completed with {artifacts.Count} artifact(s)", JobName: name));
        return new DumpJobResult(name, architecture, outputDirectory, true, null, artifacts);
    }

    private static IReadOnlyList<DumpArtifact> DiscoverArtifacts(string outputDirectory) =>
        Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .Select(path => new DumpArtifact(path, GetArtifactKind(path)))
            .ToArray();

    private static string GetArtifactKind(string path) => Path.GetFileName(path).ToLowerInvariant() switch
    {
        "dump.cs" => "C# dump",
        "script.json" => "Analysis script data",
        "stringliteral.json" => "String literals",
        "il2cpp.h" => "C header",
        _ when Path.GetExtension(path).Equals(".py", StringComparison.OrdinalIgnoreCase) => "Analysis script",
        _ when Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) => "Dummy assembly",
        _ => "File"
    };

    private static void Validate(DumpRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OutputDirectory))
            throw new ArgumentException("Output directory is required.");
        if (request.IsPackage)
        {
            if (!File.Exists(request.PackagePath)) throw new FileNotFoundException("Package was not found.", request.PackagePath);
            if (!PackageResolver.IsSupported(request.PackagePath)) throw new NotSupportedException("Package type is not supported.");
            return;
        }
        if (!File.Exists(request.BinaryPath)) throw new FileNotFoundException("Executable was not found.", request.BinaryPath);
        if (!File.Exists(request.MetadataPath)) throw new FileNotFoundException("Metadata was not found.", request.MetadataPath);
        if (optionsHaveIncompleteOffsets(request.Options)) throw new ArgumentException("Both registration offsets must be provided together.");
    }

    private static bool optionsHaveIncompleteOffsets(DumpOptions options) =>
        options.CodeRegistration.HasValue != options.MetadataRegistration.HasValue;

    private static string GetRequestName(DumpRequest request) => Path.GetFileNameWithoutExtension(
        request.IsPackage ? request.PackagePath : request.BinaryPath);
}
