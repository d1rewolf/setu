namespace WindowsMcp.Core;

public sealed class RequestLog
{
    public static RequestLog Null { get; } = new();

    private readonly string? _path;
    private readonly object _gate = new();

    public RequestLog(string path) => _path = path;

    private RequestLog() { }

    public void Write(string line)
    {
        if (_path is null) return;
        var text = $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}";
        try
        {
            lock (_gate) File.AppendAllText(_path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging is a side channel: it must never change a tool's outcome.
        }
    }
}
