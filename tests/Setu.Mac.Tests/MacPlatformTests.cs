using System.Net;
using ModelContextProtocol.Client;
using Setu.Core;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Server;
using Setu.Core.Shell;

namespace Setu.Mac.Tests;

/// <summary>The parts the Mac Program hands to SetuStartup, with the desktop faked (MacSystem needs macOS).</summary>
public sealed class MacPlatformTests : IDisposable
{
    private const string Home = "/Users/jb";
    private readonly string _data = Path.Combine(Path.GetTempPath(), "setu-macparts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_data)) Directory.Delete(_data, recursive: true);
    }

    private PlatformParts Parts(RequestLog log) =>
        MacPlatform.Create(Home, _data, log, new MacDesktop(new FakeMacSystem(), new FakeRunner(), _data));

    [Fact]
    public void Parts_AreTheMacOnes()
    {
        var parts = Parts(RequestLog.Null);

        Assert.Equal(_data, parts.DataDirectory);
        Assert.IsType<UnixTokenFileProtector>(parts.TokenProtector);
        Assert.IsType<ProcessGroupContainment>(parts.Containment());
        Assert.IsType<MacDetachedLauncher>(parts.Launcher);
        Assert.IsType<MacDesktop>(parts.Desktop);
        Assert.Equal(28591, parts.FileOptions.FallbackCodePage);
        Assert.Equal(LineEndings.Lf, parts.FileOptions.NewFileLineEndings);
        Assert.NotNull(parts.FileOptions.Protected.Match($"{Home}/Documents/x"));
        Assert.Same(KeyVocabulary.Mac, parts.Keys);
        Assert.Equal(ShellFlavor.Zsh, parts.Shell);
        Assert.Null(parts.Zsh);
        Assert.NotNull(parts.AfterStart);
    }

    [Fact]
    public void Parts_SingleInstance_IsTheLockFileInTheDataFolder()
    {
        var parts = Parts(RequestLog.Null);
        using (var first = parts.AcquireSingleInstance())
        {
            Assert.NotNull(first);
            Assert.True(File.Exists(Path.Combine(_data, "setu.lock")));
            Assert.Null(parts.AcquireSingleInstance());
        }
        using var again = parts.AcquireSingleInstance();
        Assert.NotNull(again);
    }

    [Fact]
    public async Task Parts_GiveExactlyTheFourteenMacTools()
    {
        string[] expected =
        [
            "read_file", "write_file", "edit_file", "list_dir", "search", "shell", "start_process",
            "list_windows", "screenshot", "click", "type_text", "send_keys", "scroll", "focus_window",
        ];
        const string token = "mac-parts-token";
        var parts = Parts(RequestLog.Null);
        var app = ServerHost.Build(new ServerHostSettings(
            IPAddress.Loopback, 0, token, RequestLog.Null, parts.FileOptions, parts.Containment, parts.Launcher,
            parts.Desktop, parts.Keys, parts.Shell, parts.Zsh));
        try
        {
            await app.StartAsync(TestContext.Current.CancellationToken);
            await using var client = await McpClient.CreateAsync(
                new HttpClientTransport(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(app.Urls.Single().TrimEnd('/') + "/mcp"),
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                }),
                cancellationToken: TestContext.Current.CancellationToken);
            var tools = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
            var names = tools.Select(t => t.Name).ToList();

            Assert.Equal(14, names.Count);
            Assert.True(expected.ToHashSet().SetEquals(names), string.Join(",", names));

            var start = tools.Single(t => t.Name == "start_process");
            Assert.Contains("existing PID", start.Description);
            Assert.Contains("single quotes, double quotes and backslash", start.JsonSchema.GetRawText());
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
            await app.DisposeAsync();
        }
    }
}
