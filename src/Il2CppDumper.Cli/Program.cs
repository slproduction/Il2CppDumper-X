using System.Globalization;
using Il2CppDumper;
using Il2CppDumper.Application;
using Il2CppDumper.Packages;

return await Cli.RunAsync(args);

internal static class Cli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
        {
            PrintHelp();
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "dump" => await RunDumpAsync(args[1..], cancellation.Token),
                "batch" => await RunBatchAsync(args[1..], cancellation.Token),
                "version" or "--version" => PrintVersion(),
                _ => Fail($"Unknown command: {args[0]}")
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 2;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunDumpAsync(string[] args, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Positionals.Count is < 1 or > 2)
            throw new ArgumentException("dump requires a package or an executable and metadata pair.");

        var output = GetRequired(parsed, "output", "o");
        var options = CreateDumpOptions(parsed);
        DumpRequest request;
        if (parsed.Positionals.Count == 1)
        {
            request = new DumpRequest
            {
                PackagePath = Path.GetFullPath(parsed.Positionals[0]),
                OutputDirectory = Path.GetFullPath(output),
                Options = options,
                PackageOptions = new PackageOptions { Architectures = GetValues(parsed, "arch").ToHashSet(StringComparer.OrdinalIgnoreCase) }
            };
        }
        else
        {
            request = new DumpRequest
            {
                BinaryPath = Path.GetFullPath(parsed.Positionals[0]),
                MetadataPath = Path.GetFullPath(parsed.Positionals[1]),
                OutputDirectory = Path.GetFullPath(output),
                Options = options
            };
        }

        var result = await new DumpService().DumpAsync(request, CreateProgress(), cancellationToken);
        PrintResult(result.Jobs);
        return result.Success ? 0 : 1;
    }

    private static async Task<int> RunBatchAsync(string[] args, CancellationToken cancellationToken)
    {
        var parsed = Parse(args);
        if (parsed.Positionals.Count != 1)
            throw new ArgumentException("batch requires one input directory.");

        var inputDirectory = Path.GetFullPath(parsed.Positionals[0]);
        if (!Directory.Exists(inputDirectory))
            throw new DirectoryNotFoundException($"Input directory was not found: {inputDirectory}");

        var output = Path.GetFullPath(GetRequired(parsed, "output", "o"));
        var recursive = HasFlag(parsed, "recursive", "r");
        var search = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var packageOptions = new PackageOptions { Architectures = GetValues(parsed, "arch").ToHashSet(StringComparer.OrdinalIgnoreCase) };
        var options = CreateDumpOptions(parsed);
        var jobs = Directory.EnumerateFiles(inputDirectory, "*", search)
            .Where(PackageResolver.IsSupported)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new DumpRequest
            {
                PackagePath = path,
                OutputDirectory = Path.Combine(output, Path.GetFileNameWithoutExtension(path)),
                Options = options,
                PackageOptions = packageOptions
            })
            .ToArray();

        if (jobs.Length == 0)
            throw new ArgumentException("No supported packages were found.");

        Console.WriteLine($"Queued {jobs.Length} package(s).");
        var result = await new DumpService().BatchAsync(
            new BatchRequest(jobs, !HasFlag(parsed, "stop-on-error")),
            CreateProgress(),
            cancellationToken);
        PrintResult(result.Jobs);
        Console.WriteLine($"Summary: {result.Completed} completed, {result.Failed} failed.");
        return result.Failed == 0 ? 0 : 1;
    }

    private static DumpOptions CreateDumpOptions(ParsedArgs args)
    {
        var noDump = HasFlag(args, "no-dump-cs");
        var noStruct = HasFlag(args, "no-struct");
        var noDll = HasFlag(args, "no-dummy-dll");
        return new DumpOptions
        {
            GenerateDumpCs = !noDump,
            GenerateStructures = !noStruct,
            GenerateDummyDll = !noDll,
            FastMode = HasFlag(args, "fast"),
            WorkerThreads = GetInt(args, "threads") ?? 0,
            CodeRegistration = GetHex(args, "code-registration"),
            MetadataRegistration = GetHex(args, "metadata-registration"),
            ImageBase = GetHex(args, "image-base"),
            Core = new Config
            {
                GenerateStruct = !noStruct,
                GenerateDummyDll = !noDll,
                ForceDump = GetHex(args, "image-base").HasValue
            }
        };
    }

    private static IProgress<DumpProgress> CreateProgress() => new Progress<DumpProgress>(item =>
    {
        var prefix = item.Level switch
        {
            DiagnosticLevel.Warning => "warning",
            DiagnosticLevel.Error => "error",
            _ => item.Stage.ToString().ToLowerInvariant()
        };
        var job = string.IsNullOrEmpty(item.JobName) ? string.Empty : $"[{item.JobName}] ";
        Console.WriteLine($"{prefix,-20} {job}{item.Message}");
    });

    private static void PrintResult(IEnumerable<DumpJobResult> jobs)
    {
        foreach (var job in jobs)
        {
            var status = job.Success ? "completed" : "failed";
            var architecture = string.IsNullOrEmpty(job.Architecture) ? string.Empty : $" ({job.Architecture})";
            Console.WriteLine($"{status,-20} {job.Name}{architecture}: {job.OutputDirectory}");
            if (!job.Success)
                Console.Error.WriteLine(job.Error);
        }
    }

    private static ParsedArgs Parse(string[] args)
    {
        var result = new ParsedArgs();
        for (var index = 0; index < args.Length; index++)
        {
            var token = args[index];
            if (!token.StartsWith('-'))
            {
                result.Positionals.Add(token);
                continue;
            }

            var key = token.TrimStart('-');
            var equals = key.IndexOf('=');
            if (equals >= 0)
            {
                result.Add(key[..equals], key[(equals + 1)..]);
                continue;
            }

            if (index + 1 < args.Length && !args[index + 1].StartsWith('-'))
                result.Add(key, args[++index]);
            else
                result.Add(key, null);
        }
        return result;
    }

    private static string GetRequired(ParsedArgs args, params string[] names) =>
        names.SelectMany(name => GetValues(args, name)).LastOrDefault() ??
        throw new ArgumentException($"Missing required option --{names[0]}.");

    private static IReadOnlyList<string> GetValues(ParsedArgs args, string name) =>
        args.Options.TryGetValue(name, out var values) ? values.Where(value => value is not null).ToArray() : [];

    private static bool HasFlag(ParsedArgs args, params string[] names) => names.Any(name => args.Options.ContainsKey(name));

    private static int? GetInt(ParsedArgs args, string name)
    {
        var value = GetValues(args, name).LastOrDefault();
        return value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static ulong? GetHex(ParsedArgs args, string name)
    {
        var value = GetValues(args, name).LastOrDefault();
        if (value is null) return null;
        return ulong.Parse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
            NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    private static int PrintVersion()
    {
        Console.WriteLine(typeof(Cli).Assembly.GetName().Version?.ToString(3));
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Run 'il2cppdumper help' for usage.");
        return 2;
    }

    private static void PrintHelp() => Console.WriteLine("""
        Il2CppDumper CLI

        Usage:
          il2cppdumper dump <binary> <global-metadata.dat> -o <directory> [options]
          il2cppdumper dump <package> -o <directory> [options]
          il2cppdumper batch <directory> -o <directory> [options]

        Package types:
          APK, APKS, APKM, XAPK, ZIP, decrypted IPA

        Options:
          -o, --output <path>               Output directory
          --arch <name>                     Android architecture (repeatable)
          --fast                            Skip metadata usage scan
          --threads <count>                 Worker thread count, 0 = automatic
          --code-registration <hex>         Manual CodeRegistration address
          --metadata-registration <hex>     Manual MetadataRegistration address
          --image-base <hex>                Image base for memory-dumped ELF
          --no-dump-cs                      Do not generate dump.cs
          --no-struct                       Do not generate structure files
          --no-dummy-dll                    Do not generate dummy assemblies
          -r, --recursive                   Scan batch input recursively
          --stop-on-error                   Stop batch after the first failure
        """);

    private sealed class ParsedArgs
    {
        public List<string> Positionals { get; } = [];
        public Dictionary<string, List<string>> Options { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string key, string value)
        {
            if (!Options.TryGetValue(key, out var values))
                Options[key] = values = [];
            values.Add(value);
        }
    }
}
