namespace Il2CppDumper;

public enum DiagnosticLevel
{
    Information,
    Warning,
    Error
}

public readonly record struct DiagnosticMessage(
    DateTimeOffset Timestamp,
    DiagnosticLevel Level,
    string Message);

public static class DumperDiagnostics
{
    private static readonly AsyncLocal<Action<DiagnosticMessage>> CurrentSink = new();

    public static IDisposable Push(Action<DiagnosticMessage> sink)
    {
        var previous = CurrentSink.Value;
        CurrentSink.Value = sink;
        return new Scope(() => CurrentSink.Value = previous);
    }

    public static void Information(string message, params object[] args) =>
        Write(DiagnosticLevel.Information, message, args);

    public static void Warning(string message, params object[] args) =>
        Write(DiagnosticLevel.Warning, message, args);

    public static void Error(string message, params object[] args) =>
        Write(DiagnosticLevel.Error, message, args);

    private static void Write(DiagnosticLevel level, string message, object[] args)
    {
        var formatted = args.Length == 0 ? message : string.Format(message, args);
        CurrentSink.Value?.Invoke(new DiagnosticMessage(DateTimeOffset.UtcNow, level, formatted));
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
