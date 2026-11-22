namespace WindowsMcp.Core;

public sealed class RequestLog
{
    public static RequestLog Null { get; } = new();

    private static readonly System.Text.RegularExpressions.Regex ControlRuns =
        new(@"\p{Cc}+", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    private readonly string? _path;
    private readonly object _gate = new();

    public RequestLog(string path) => _path = path;

    private RequestLog() { }

    public void Write(string line)
    {
        if (_path is null) return;
        // One call, one log line: a line break or other control character in a message would split it.
        var text = $"{DateTimeOffset.Now:O} {ControlRuns.Replace(line, " ")}{Environment.NewLine}";
        try
        {
            lock (_gate) File.AppendAllText(_path, text);
        }
        catch (Exception)
        {
            // Logging is a side channel: it must never change a tool's outcome.
        }
    }
}
