using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WindowsMcp.Core.Files;
using WindowsMcp.Core.Server;
using WindowsMcp.Core.Shell;

namespace WindowsMcp.Core.Tests;

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
            new FileToolsOptions(1252), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop()));
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
                new FileToolsOptions(1252), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop()));
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
            new FileToolsOptions(1252), () => new DefaultContainment(), new FakeLauncher(), new FakeDesktop())));
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
}
