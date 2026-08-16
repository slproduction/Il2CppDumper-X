using System.Reflection;

namespace Il2CppDumper.Application;

public sealed record AnalysisScript(
    string FileName,
    string Tool,
    string Description,
    bool RequiresStructures = false);

public static class AnalysisScriptCatalog
{
    public static IReadOnlyList<AnalysisScript> All { get; } =
    [
        new("ida_py3.py", "IDA Pro", "Rename methods and apply IL2CPP metadata in current IDA versions."),
        new("ida_with_struct_py3.py", "IDA Pro", "Import il2cpp.h structures and function signatures.", true),
        new("ghidra.py", "Ghidra", "Rename functions and annotate addresses from script.json."),
        new("ghidra_with_struct.py", "Ghidra", "Apply structures and signatures from il2cpp.h.", true),
        new("ghidra_wasm.py", "Ghidra", "Import WebAssembly IL2CPP symbols."),
        new("il2cpp_header_to_ghidra.py", "Ghidra", "Import generated il2cpp.h into the Data Type Manager.", true),
        new("il2cpp_header_to_binja.py", "Binary Ninja", "Convert il2cpp.h for Binary Ninja import.", true),
        new("hopper-py3.py", "Hopper", "Rename addresses and methods using script.json."),
        new("ida.py", "IDA Pro (legacy)", "Python 2 script for older IDA versions."),
        new("ida_with_struct.py", "IDA Pro (legacy)", "Python 2 structure import for older IDA versions.", true)
    ];

    public static IReadOnlyList<string> Export(
        IEnumerable<string> selectedFiles,
        string outputDirectory)
    {
        var known = All.ToDictionary(script => script.FileName, StringComparer.OrdinalIgnoreCase);
        var assembly = typeof(AnalysisScriptCatalog).Assembly;
        var exported = new List<string>();

        foreach (var fileName in selectedFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!known.ContainsKey(fileName))
                throw new ArgumentException($"Unknown analysis script: {fileName}");

            var resourceName = $"Il2CppDumper.Application.Scripts.{fileName}";
            using var source = assembly.GetManifestResourceStream(resourceName) ??
                throw new InvalidOperationException($"Embedded analysis script is missing: {fileName}");
            var destination = Path.Combine(outputDirectory, fileName);
            using var target = File.Create(destination);
            source.CopyTo(target);
            exported.Add(destination);
        }

        return exported;
    }
}
