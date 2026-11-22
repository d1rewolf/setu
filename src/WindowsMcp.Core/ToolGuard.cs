using System.Diagnostics;
using ModelContextProtocol;

namespace WindowsMcp.Core;

/// <summary>
/// Runs a tool body and maps failures to McpException, whose message the SDK passes to the agent
/// (it hides the message of any other exception type). Logs one line per call.
/// </summary>
public static class ToolGuard
{
    public static T Run<T>(RequestLog log, string tool, Func<T> body)
    {
        var sw = Stopwatch.StartNew();
        T result;
        try
        {
            result = body();
        }
        catch (Exception ex)
        {
            var mapped = Map(ex);
            log.Write($"{tool} {sw.ElapsedMilliseconds}ms error: {mapped.Message}");
            if (ReferenceEquals(mapped, ex)) throw;
            throw mapped;
        }
        log.Write($"{tool} {sw.ElapsedMilliseconds}ms ok");
        return result;
    }

    public static async Task<T> RunAsync<T>(RequestLog log, string tool, Func<Task<T>> body)
    {
        var sw = Stopwatch.StartNew();
        T result;
        try
        {
            result = await body();
        }
        catch (Exception ex)
        {
            var mapped = Map(ex);
            log.Write($"{tool} {sw.ElapsedMilliseconds}ms error: {mapped.Message}");
            if (ReferenceEquals(mapped, ex)) throw;
            throw mapped;
        }
        log.Write($"{tool} {sw.ElapsedMilliseconds}ms ok");
        return result;
    }

    private static Exception Map(Exception ex) => ex switch
    {
        McpException => ex,
        FileNotFoundException f => new McpException($"Not found: {f.FileName ?? f.Message}", ex),
        DirectoryNotFoundException => new McpException($"Not found: {ex.Message}", ex),
        UnauthorizedAccessException => new McpException($"Access denied: {ex.Message}", ex),
        ToolInputException => new McpException(ex.Message, ex),
        _ => new McpException($"{ex.GetType().Name}: {ex.Message}", ex),
    };
}
