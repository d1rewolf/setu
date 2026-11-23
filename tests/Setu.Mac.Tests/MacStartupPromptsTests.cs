using System.Net;
using Setu.Core;

namespace Setu.Mac.Tests;

public sealed class MacStartupPromptsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("setu-prompts-").FullName;
    private string LogPath => Path.Combine(_dir, "setu.log");
    private string Log => File.Exists(LogPath) ? File.ReadAllText(LogPath) : "";

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private const string RouteOutput = """
           route to: default
        destination: default
               mask: default
            gateway: 172.16.1.1
          interface: en0
              flags: <UP,GATEWAY,DONE,STATIC,PRCLONING,GLOBAL>
        """;

    private sealed class RouteRunner((int, string, string) answer) : ICommandRunner
    {
        public List<(string File, List<string> Args)> Calls { get; } = [];

        public (int Exit, string Stdout, string Stderr) RunCapture(string file, IReadOnlyList<string> args, TimeSpan timeout)
        {
            Calls.Add((file, args.ToList()));
            return answer;
        }

        public (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout) => throw new NotSupportedException();
    }

    [Fact]
    public void ParseGateway_ReadsTheGatewayLine()
    {
        Assert.Equal(IPAddress.Parse("172.16.1.1"), MacStartupPrompts.ParseGateway(RouteOutput));
        Assert.Null(MacStartupPrompts.ParseGateway("route: writing to routing socket: not in table"));
        Assert.Null(MacStartupPrompts.ParseGateway("    gateway: utun3\n"));
        Assert.Null(MacStartupPrompts.ParseGateway("    gateway: fe80::1%en0\n"));
    }

    [Fact]
    public void FallbackGateway_IsDotOneOfTheFirstLanAddress()
    {
        Assert.Equal(IPAddress.Parse("192.168.4.1"), MacStartupPrompts.FallbackGateway(
            [IPAddress.Loopback, IPAddress.IPv6Loopback, IPAddress.Parse("192.168.4.23"), IPAddress.Parse("10.0.0.5")]));
        Assert.Null(MacStartupPrompts.FallbackGateway([IPAddress.Loopback, IPAddress.Parse("169.254.3.4")]));
    }

    [Fact]
    public void LocalNetwork_SendsToTheGatewayPort9()
    {
        var runner = new RouteRunner((0, RouteOutput, ""));
        var sent = new List<IPEndPoint>();
        MacStartupPrompts.TriggerLocalNetwork(new RequestLog(LogPath), runner, () => [], sent.Add);

        Assert.Equal("/sbin/route", runner.Calls[0].File);
        Assert.Equal(["-n", "get", "default"], runner.Calls[0].Args);
        Assert.Equal([new IPEndPoint(IPAddress.Parse("172.16.1.1"), 9)], sent);
        Assert.Contains("local network: sent one UDP byte to 172.16.1.1:9 (default gateway)", Log);
    }

    [Fact]
    public void LocalNetwork_RouteFails_UsesFallback()
    {
        var runner = new RouteRunner((1, "", "route: not in table"));
        var sent = new List<IPEndPoint>();
        MacStartupPrompts.TriggerLocalNetwork(new RequestLog(LogPath), runner, () => [IPAddress.Parse("10.1.2.3")], sent.Add);

        Assert.Equal([new IPEndPoint(IPAddress.Parse("10.1.2.1"), 9)], sent);
        Assert.Contains("local network: no default gateway from route", Log);
        Assert.Contains("local network: sent one UDP byte to 10.1.2.1:9 (first LAN address .1)", Log);
    }

    [Fact]
    public void LocalNetwork_NoAddressOrSendFailure_IsLoggedNotThrown()
    {
        var runner = new RouteRunner((1, "", ""));
        MacStartupPrompts.TriggerLocalNetwork(new RequestLog(LogPath), runner, () => [], _ => throw new InvalidOperationException("nope"));
        Assert.Contains("local network: no LAN address found; nothing sent", Log);

        var good = new RouteRunner((0, RouteOutput, ""));
        MacStartupPrompts.TriggerLocalNetwork(new RequestLog(LogPath), good, () => [],
            _ => throw new System.Net.Sockets.SocketException(32));
        Assert.Contains("local network: UDP send to 172.16.1.1:9 failed:", Log);
    }
}
