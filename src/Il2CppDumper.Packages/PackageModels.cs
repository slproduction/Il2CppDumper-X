namespace Il2CppDumper.Packages;

public sealed record PackageOptions
{
    public IReadOnlySet<string> Architectures { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public bool ExtractBinary { get; init; }
    public bool ExtractMetadata { get; init; }
}

public sealed record PreparedInput(
    string Name,
    string BinaryPath,
    string MetadataPath,
    string Architecture,
    string RelativeOutputPath);

public sealed class PreparedPackage : IAsyncDisposable
{
    public PreparedPackage(string workspace, IReadOnlyList<PreparedInput> inputs)
    {
        Workspace = workspace;
        Inputs = inputs;
    }

    public string Workspace { get; }
    public IReadOnlyList<PreparedInput> Inputs { get; }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(Workspace))
                Directory.Delete(Workspace, true);
        }
        catch (IOException)
        {
            // Temporary files can remain locked briefly by antivirus/indexing services.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
