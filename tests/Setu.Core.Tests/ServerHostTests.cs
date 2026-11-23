using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Server;
using Setu.Core.Shell;

namespace Setu.Core.Tests;

public sealed class ServerHostTests : IAsyncLifetime
{
    private const string Token = "s3cret-token-value";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wmcp-host-" + Guid.NewGuid().ToString("N"));
    private WebApplication _app = null!;
    private string _baseUrl = "";

    private sealed class FakeLauncher : IDetachedLauncher
    {
        public int Start(string path, string? arguments, string? workingDirectory) => 1234;
    }

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _app = ServerHost.Build(new ServerHostSettings(
            IPAddress.Loopback, 0, Token, new RequestLog(Path.Combine(_dir, "requests.log")),
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(), KeyVocabulary.Windows, ShellFlavor.Windows, null));
        await _app.StartAsync(TestContext.Current.CancellationToken);
        _baseUrl = _app.Urls.Single().TrimEnd('/');
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private async Task<HttpResponseMessage> PostInitialize(string? authorization)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (authorization is not null) req.Headers.TryAddWithoutValidation("Authorization", authorization);
        return await http.SendAsync(req, TestContext.Current.CancellationToken);
    }

    private Task<McpClient> Connect() =>
        McpClient.CreateAsync(
            new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(_baseUrl + "/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Token },
            }),
            cancellationToken: TestContext.Current.CancellationToken);

    [Fact]
    public async Task NoToken_401()
    {
        using var res = await PostInitialize(null);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains("401 POST /mcp", File.ReadAllText(Path.Combine(_dir, "requests.log")));
    }

    [Fact]
    public async Task WrongToken_401()
    {
        using var res = await PostInitialize("Bearer s3cret-token-valuX");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task WrongLengthToken_401()
    {
        using var res = await PostInitialize("Bearer " + Token + "x");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task OtherPath_WithoutToken_401()
    {
        using var http = new HttpClient();
        using var res = await http.GetAsync(_baseUrl + "/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Unauthorized_PathWithNewline_LogsOneEscapedLine()
    {
        using var http = new HttpClient();
        using var res = await http.GetAsync(_baseUrl + "/a%0Ainjected%0D%0Ab", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var lines = File.ReadAllLines(Path.Combine(_dir, "requests.log"));
        var line = Assert.Single(lines);
        Assert.Contains("401 GET /a%0Ainjected%0D%0Ab", line);
    }

    [Fact]
    public async Task Unauthorized_LongPath_IsCapped()
    {
        using var http = new HttpClient();
        using var res = await http.GetAsync(_baseUrl + "/" + new string('x', 5000), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var line = Assert.Single(File.ReadAllLines(Path.Combine(_dir, "requests.log")));
        Assert.True(line.Length < 300, line.Length.ToString());
    }

    [Fact]
    public void BoundToExactlyOneLoopbackUrl()
    {
        var url = Assert.Single(_app.Urls);
        Assert.StartsWith("http://127.0.0.1:", url);
    }

    [Fact]
    public async Task KestrelEndpointsFromEnvironment_AreIgnored()
    {
        const string name = "Kestrel__Endpoints__Extra__Url";
        var old = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, "http://0.0.0.0:0");
        WebApplication? app = null;
        try
        {
            app = ServerHost.Build(new ServerHostSettings(
                IPAddress.Loopback, 0, Token, RequestLog.Null,
                new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(), KeyVocabulary.Windows, ShellFlavor.Windows, null));
            await app.StartAsync(TestContext.Current.CancellationToken);
            var url = Assert.Single(app.Urls);
            Assert.StartsWith("http://127.0.0.1:", url);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, old);
            if (app is not null)
            {
                await app.StopAsync(TestContext.Current.CancellationToken);
                await app.DisposeAsync();
            }
        }
    }

    [Fact]
    public void Build_NonLoopbackAddress_Throws()
    {
        Assert.Throws<ArgumentException>(() => ServerHost.Build(new ServerHostSettings(
            IPAddress.Any, 0, Token, RequestLog.Null,
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(), KeyVocabulary.Windows, ShellFlavor.Windows, null)));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000", true)]
    [InlineData("http://[::1]:5000", true)]
    [InlineData("http://0.0.0.0:5000", false)]
    [InlineData("http://[::]:5000", false)]
    [InlineData("http://192.168.1.10:5000", false)]
    [InlineData("http://localhost:5000", false)]
    public void LoopbackGuard_ChecksEveryBoundAddress(string url, bool ok)
    {
        var urls = new[] { "http://127.0.0.1:1", url };
        if (ok) LoopbackGuard.EnsureLoopback(urls);
        else Assert.Throws<InvalidOperationException>(() => LoopbackGuard.EnsureLoopback(urls));
    }

    [Fact]
    public async Task RightToken_LowercaseScheme_Accepted()
    {
        using var res = await PostInitialize("bearer " + Token);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task RightToken_ListsAllTools()
    {
        await using var client = await Connect();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var expected = new HashSet<string>
        {
            "read_file", "write_file", "edit_file", "list_dir", "search", "powershell", "cmd", "start_process",
            "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
        };
        Assert.True(expected.SetEquals(tools.Select(t => t.Name)), string.Join(",", tools.Select(t => t.Name)));
    }

    [Fact]
    public async Task ShellTools_CancellationToken_NotInInputSchema()
    {
        await using var client = await Connect();
        var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        foreach (var name in new[] { "powershell", "cmd" })
        {
            var schema = tools.Single(t => t.Name == name).JsonSchema.GetRawText();
            Assert.Contains("\"command\"", schema);
            Assert.DoesNotContain("ancellation", schema);
        }
    }

    [Fact]
    public async Task ToolError_ReachesClientWithPlainMessage()
    {
        await using var client = await Connect();
        var missing = Path.Combine(_dir, "missing.txt");
        var result = await client.CallToolAsync("read_file", new Dictionary<string, object?> { ["path"] = missing },
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(result.IsError);
        var text = Assert.IsType<TextContentBlock>(result.Content.Single()).Text;
        Assert.StartsWith("Not found:", text);
    }

    private static readonly string[] WindowsTools =
    [
        "read_file", "write_file", "edit_file", "list_dir", "search", "powershell", "cmd", "start_process",
        "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
    ];

    private static readonly string[] MacTools =
    [
        "read_file", "write_file", "edit_file", "list_dir", "search", "shell", "start_process",
        "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
    ];

    [Theory]
    [InlineData(ShellFlavor.Windows)]
    [InlineData(ShellFlavor.Zsh)]
    public async Task ServerHost_ToolLists(ShellFlavor flavor)
    {
        var app = ServerHost.Build(new ServerHostSettings(
            IPAddress.Loopback, 0, Token, RequestLog.Null,
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(),
            flavor == ShellFlavor.Zsh ? KeyVocabulary.Mac : KeyVocabulary.Windows, flavor, null));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(app.Urls.Single().TrimEnd('/') + "/mcp"),
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Token },
                }),
                cancellationToken: TestContext.Current.CancellationToken);
            var names = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Select(t => t.Name).ToList();
            var expected = flavor == ShellFlavor.Zsh ? MacTools : WindowsTools;
            Assert.Equal(expected.Length, names.Count);
            Assert.True(expected.ToHashSet().SetEquals(names), string.Join(",", names));
            if (flavor == ShellFlavor.Zsh)
            {
                Assert.DoesNotContain("powershell", names);
                Assert.DoesNotContain("cmd", names);
                var schema = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken))
                    .Single(t => t.Name == "shell").JsonSchema.GetRawText();
                Assert.Contains("\"command\"", schema);
                Assert.DoesNotContain("ancellation", schema);
            }
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    /// <summary>The tool text a host for <paramref name="flavor"/> gives an agent, as listed over MCP.</summary>
    private static async Task<IList<McpClientTool>> ListTools(ShellFlavor flavor)
    {
        var app = ServerHost.Build(new ServerHostSettings(
            IPAddress.Loopback, 0, Token, RequestLog.Null,
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop(),
            flavor == ShellFlavor.Zsh ? KeyVocabulary.Mac : KeyVocabulary.Windows, flavor, null));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(app.Urls.Single().TrimEnd('/') + "/mcp"),
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Token },
                }),
                cancellationToken: TestContext.Current.CancellationToken);
            return await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }

    private static string ParamDescription(McpClientTool tool, string parameter) =>
        tool.JsonSchema.GetProperty("properties").GetProperty(parameter).GetProperty("description").GetString()!;

    private const string WindowsCwd = "Optional absolute working directory. %VARS% are expanded.";

    /// <summary>
    /// The Windows text, pinned word for word. powershell, cmd and start_process's parameters are as on main;
    /// start_process says "a shell command's own background launch" (the plan's neutral wording) and send_keys
    /// names the Mac modifiers too (also the plan's).
    /// </summary>
    [Fact]
    public async Task WindowsHost_ToolText_Pinned()
    {
        var tools = await ListTools(ShellFlavor.Windows);
        McpClientTool Tool(string name) => tools.Single(t => t.Name == name);

        var start = Tool("start_process");
        Assert.Equal("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not a shell command's own background launch, for GUI apps or anything you want to keep running after the call returns.", start.Description);
        Assert.Equal("Program path or name on PATH, e.g. notepad.exe.", ParamDescription(start, "path"));
        Assert.Equal("Optional command-line arguments as one string.", ParamDescription(start, "arguments"));
        Assert.Equal(WindowsCwd, ParamDescription(start, "cwd"));

        var keys = Tool("send_keys");
        Assert.Equal("Presses key chords in the focused window. Chords are separated by spaces; the parts of one chord are joined with +, case-insensitive. Modifiers on Windows: ctrl alt shift win. On macOS: cmd ctrl option shift (alt = option). Keys: a-z, 0-9, f1-f24, enter tab esc backspace delete insert space up down left right home end pageup pagedown printscreen. Examples: \"ctrl+s\", \"alt+f4\", \"ctrl+a delete\".", keys.Description);
        Assert.Equal("Space-separated chords, e.g. \"ctrl+shift+s enter\".", ParamDescription(keys, "keys"));

        var ps = Tool("powershell");
        Assert.Equal("Runs a Windows PowerShell command and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.", ps.Description);
        Assert.Equal("PowerShell script text.", ParamDescription(ps, "command"));
        Assert.Equal(WindowsCwd, ParamDescription(ps, "cwd"));
        Assert.Equal("Timeout in seconds, 1 to 600.", ParamDescription(ps, "timeout_s"));

        var cmd = Tool("cmd");
        Assert.Equal("Runs a cmd.exe command line and waits for it to finish. Non-interactive: anything that waits for input fails instead of waiting. On timeout the command and everything it started is killed and the partial output is returned. Output is capped at 64 KiB per stream. Programs this command starts may be stopped when the server restarts; use start_process for apps that must keep running.", cmd.Description);
        Assert.Equal("cmd.exe command line.", ParamDescription(cmd, "command"));
        Assert.Equal(WindowsCwd, ParamDescription(cmd, "cwd"));
        Assert.Equal("Timeout in seconds, 1 to 600.", ParamDescription(cmd, "timeout_s"));
    }

    [Fact]
    public async Task MacHost_ToolText_IsTheMacs()
    {
        var tools = await ListTools(ShellFlavor.Zsh);
        McpClientTool Tool(string name) => tools.Single(t => t.Name == name);

        var start = Tool("start_process");
        Assert.Equal("Starts a program in the user's desktop session and returns its PID without waiting for it. Use this, not a shell command's own background launch, for GUI apps or anything you want to keep running after the call returns. A path ending in .app, or an app name that is not a program on PATH (e.g. TextEdit), starts through LaunchServices; if that app is already running it is brought to the front and its existing PID is returned.", start.Description);
        Assert.Equal("Program path or name on PATH, or an app name or .app path, e.g. TextEdit. A bare name is looked up on /usr/bin:/bin:/usr/sbin:/sbin; give the full path for anything installed elsewhere (e.g. Homebrew).", ParamDescription(start, "path"));
        Assert.Equal("Optional command-line arguments as one string, split into arguments like a POSIX shell does (single quotes, double quotes and backslash), with no variable, ~ or wildcard expansion. For an app, absolute paths of existing files are opened as documents and the rest are passed to the app.", ParamDescription(start, "arguments"));
        Assert.Equal("Optional absolute working directory; a leading ~ is your home folder. Ignored when starting an app.", ParamDescription(start, "cwd"));

        var keys = Tool("send_keys");
        Assert.Equal("Presses key chords in the focused window. Chords are separated by spaces; the parts of one chord are joined with +, case-insensitive. Modifiers on Windows: ctrl alt shift win. On macOS: cmd ctrl option shift (alt = option). Keys: a-z, 0-9, f1-f20, enter tab esc backspace delete space up down left right home end pageup pagedown. Examples: \"cmd+s\", \"cmd+w\", \"cmd+a delete\".", keys.Description);
        Assert.Equal("Space-separated chords, e.g. \"ctrl+shift+s enter\".", ParamDescription(keys, "keys"));

        // The file tools' neutral path wording names both platforms on purpose; these three are the Mac's own.
        // send_keys's "Modifiers on Windows: ..." sentence names both platforms on purpose too, so "on Windows"
        // itself is not on this list; Windows-only keys, examples, shells and path syntax are.
        var own = string.Join("\n", new[] { start, keys, Tool("shell") }.Select(t => t.Description + t.JsonSchema.GetRawText()));
        foreach (var windowsOnly in new[] { "ctrl+s\"", "alt+f4", "ctrl+a", "insert", "printscreen", "f24", "notepad", "%VARS%", "powershell", "cmd.exe" })
            Assert.DoesNotContain(windowsOnly, own, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>send_keys registered by hand still runs, with a ScreenTools from the request's services.</summary>
    [Theory]
    [InlineData(ShellFlavor.Windows)]
    [InlineData(ShellFlavor.Zsh)]
    public async Task SendKeys_Call_Works(ShellFlavor flavor)
    {
        var desktop = new FakeDesktop();
        var app = ServerHost.Build(new ServerHostSettings(
            IPAddress.Loopback, 0, Token, RequestLog.Null,
            new FileToolsOptions(1252, LineEndings.Crlf, ProtectedPaths.None), () => new DefaultContainment(), new FakeLauncher(), desktop,
            flavor == ShellFlavor.Zsh ? KeyVocabulary.Mac : KeyVocabulary.Windows, flavor, null));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(app.Urls.Single().TrimEnd('/') + "/mcp"),
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + Token },
                }),
                cancellationToken: TestContext.Current.CancellationToken);
            var result = await client.CallToolAsync("send_keys", new Dictionary<string, object?> { ["keys"] = "ctrl+s" },
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.NotEqual(true, result.IsError);
            Assert.Equal("Sent", Assert.IsType<TextContentBlock>(result.Content.Single()).Text);
            Assert.NotNull(desktop.LastChords);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
