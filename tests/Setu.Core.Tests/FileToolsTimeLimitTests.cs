using ModelContextProtocol;
using Setu.Core.Files;

namespace Setu.Core.Tests;

public sealed class FileToolsTimeLimitTests
{
    private static FileTools Tools(TimeSpan? limit)
        => new(RequestLog.Null, new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None, limit));

    [Fact]
    public void CallTimeLimit_DefaultsToNone()
        => Assert.Null(new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None).CallTimeLimit);

    [Fact]
    public void SlowCall_PastTheLimit_ReturnsTimeLimitMessage()
    {
        using var release = new ManualResetEventSlim();
        try
        {
            var ex = Assert.Throws<McpException>(() =>
                Tools(TimeSpan.FromMilliseconds(100)).Run("search", "/x", () => { release.Wait(TimeSpan.FromSeconds(5)); return "done"; }));
            Assert.Equal(
                "search did not finish within 0 seconds. A folder it touched may be waiting on a macOS permission prompt at the Mac, or the folder is very large. Narrow the path, or ask someone at the Mac to check its screen.",
                ex.Message);
        }
        finally
        {
            release.Set(); // let the abandoned work finish
        }
    }

    [Fact]
    public async Task SlowCall_PastTheLimit_IsCancelled()
    {
        var abandoned = new TaskCompletionSource<Task>();
        var tools = new FileTools(RequestLog.Null, new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None, TimeSpan.FromMilliseconds(50)))
        {
            OnAbandoned = t => abandoned.TrySetResult(t),
        };
        var sawCancel = false;
        Assert.Throws<McpException>(() => tools.Run("search", "/x", ct =>
        {
            sawCancel = ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(5));
            return "done";
        }));
        await Finished(await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(sawCancel);
    }

    [Fact]
    public async Task Search_PastTheLimit_StopsTouchingFiles()
    {
        var dir = Directory.CreateTempSubdirectory("wmcp-tl-").FullName;
        using var gate = new ManualResetEventSlim();
        try
        {
            for (var i = 0; i < 50; i++)
            {
                var sub = Directory.CreateDirectory(Path.Combine(dir, $"d{i % 5}")).FullName;
                File.WriteAllText(Path.Combine(sub, $"f{i}.txt"), "hello\nworld\n");
            }
            var abandoned = new TaskCompletionSource<Task>();
            var visited = 0;
            var tools = new FileTools(RequestLog.Null, new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None, TimeSpan.FromMilliseconds(50)))
            {
                OnAbandoned = t => abandoned.TrySetResult(t),
                OnSearchFile = _ =>
                {
                    if (Interlocked.Increment(ref visited) == 1) gate.Wait(TimeSpan.FromSeconds(5));
                },
            };

            Assert.Throws<McpException>(() => tools.Search(dir, content_regex: "hello"));
            gate.Set();
            var work = await abandoned.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await Finished(work);
            Assert.True(work.IsCanceled || work.IsFaulted);
            Assert.Equal(1, visited);
        }
        finally
        {
            gate.Set();
            Directory.Delete(dir, true);
        }
    }

    /// <summary>Waits up to 5 s for abandoned work to end, however it ended.</summary>
    private static async Task Finished(Task work)
    {
        await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(work.IsCompleted, "the abandoned work did not stop");
    }

    [Fact]
    public void TimeLimitMessage_UsesWholeSeconds()
        => Assert.StartsWith("search did not finish within 120 seconds.", FileTools.TimeLimitMessage("search", TimeSpan.FromSeconds(120)));

    [Fact]
    public void SlowCall_WithoutLimit_Completes()
        => Assert.Equal("done", Tools(null).Run("search", "/x", () => { Thread.Sleep(300); return "done"; }));

    [Fact]
    public void FastCall_WithLimit_IsUnaffected()
        => Assert.Equal("done", Tools(TimeSpan.FromSeconds(5)).Run("search", "/x", () => "done"));

    [Fact]
    public void ErrorInsideLimit_IsSurfacedAsBefore()
    {
        var ex = Assert.Throws<McpException>(() =>
            Tools(TimeSpan.FromSeconds(5)).Run<string>("read_file", "/x", () => throw new ToolInputException("boom")));
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void RealTool_WithLimit_StillWorks()
    {
        var dir = Directory.CreateTempSubdirectory("wmcp-tl-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "hello\n");
            var output = Tools(TimeSpan.FromSeconds(5)).ReadFile(Path.Combine(dir, "a.txt"));
            Assert.Contains("hello", output);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
