using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Setu.Core;
using static Setu.Mac.Native.ApplicationServices;
using static Setu.Mac.Native.CoreFoundation;
using static Setu.Mac.Native.CoreGraphics;

namespace Setu.Mac;

/// <summary>
/// Runs once Setu is listening: asks macOS for Screen Recording and Accessibility when they are not granted, and
/// touches the local network once so the Local Network prompt appears (only a first touch raises it). Every step
/// is logged and none can stop the server. It touches no protected folder: those denials were recorded by hand.
/// </summary>
public static partial class MacStartupPrompts
{
    private const int DiscardPort = 9;
    private static readonly TimeSpan RouteTimeout = TimeSpan.FromSeconds(5);

    public static void Run(RequestLog log)
    {
        Step(log, "screen recording", RequestScreenRecording);
        Step(log, "accessibility", RequestAccessibility);
        Step(log, "local network", l => TriggerLocalNetwork(l, new ProcessCommandRunner(), LanAddresses, SendOneByte));
    }

    private static void Step(RequestLog log, string name, Action<RequestLog> step)
    {
        try
        {
            step(log);
        }
        catch (Exception ex)
        {
            log.Write($"{name}: startup check failed: {ex.Message}");
        }
    }

    private static void RequestScreenRecording(RequestLog log)
    {
        if (CGPreflightScreenCaptureAccess())
        {
            log.Write("screen recording: granted");
            return;
        }
        var now = CGRequestScreenCaptureAccess();
        log.Write($"screen recording: not granted; asked macOS to prompt (granted now: {now})");
    }

    private static unsafe void RequestAccessibility(RequestLog log)
    {
        if (AXIsProcessTrusted())
        {
            log.Write("accessibility: granted");
            return;
        }
        var key = CFStringCreateWithCString(0, TrustedCheckOptionPrompt, kCFStringEncodingUTF8);
        if (key == 0) throw new InvalidOperationException("could not create the prompt option key");
        nint options = 0;
        try
        {
            var value = Constant("kCFBooleanTrue");
            options = CFDictionaryCreate(0, &key, &value, 1,
                ExportAddress("kCFTypeDictionaryKeyCallBacks"), ExportAddress("kCFTypeDictionaryValueCallBacks"));
            if (options == 0) throw new InvalidOperationException("could not create the prompt options");
            var now = AXIsProcessTrustedWithOptions(options);
            log.Write($"accessibility: not granted; asked macOS to prompt (granted now: {now})");
        }
        finally
        {
            if (options != 0) CFRelease(options);
            CFRelease(key);
        }
    }

    /// <summary>
    /// Sends one UDP byte to the default gateway's discard port, which is enough for macOS to ask for Local
    /// Network. The gateway comes from <c>route -n get default</c>; failing that, from the first LAN address
    /// with its last part set to 1. Logs each step and never throws.
    /// </summary>
    internal static void TriggerLocalNetwork(
        RequestLog log, ICommandRunner run, Func<IEnumerable<IPAddress>> lanAddresses, Action<IPEndPoint> send)
    {
        IPAddress? target = null;
        var source = "default gateway";
        try
        {
            var (exit, stdout, stderr) = run.RunCapture("/sbin/route", ["-n", "get", "default"], RouteTimeout);
            target = exit == 0 ? ParseGateway(stdout) : null;
            if (target is null)
                log.Write($"local network: no default gateway from route (exit {exit}: {stderr.Trim()})");
        }
        catch (Exception ex)
        {
            log.Write($"local network: route failed: {ex.Message}");
        }

        if (target is null)
        {
            try
            {
                target = FallbackGateway(lanAddresses());
                source = "first LAN address .1";
            }
            catch (Exception ex)
            {
                log.Write($"local network: could not list LAN addresses: {ex.Message}");
            }
        }
        if (target is null)
        {
            log.Write("local network: no LAN address found; nothing sent");
            return;
        }

        var endpoint = new IPEndPoint(target, DiscardPort);
        try
        {
            send(endpoint);
            log.Write($"local network: sent one UDP byte to {endpoint} ({source})");
        }
        catch (Exception ex)
        {
            // Before the grant is answered the send can fail (the spike saw "Broken pipe"); the prompt still shows.
            log.Write($"local network: UDP send to {endpoint} failed: {ex.Message}");
        }
    }

    /// <summary>The IPv4 address on the <c>gateway:</c> line, or null.</summary>
    internal static IPAddress? ParseGateway(string routeOutput)
    {
        var m = GatewayLine().Match(routeOutput);
        return m.Success && IPAddress.TryParse(m.Groups[1].Value, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork
            && IsDottedQuad(m.Groups[1].Value)
            ? ip
            : null;
    }

    /// <summary>x.x.x.1 for the first IPv4 address that is neither loopback nor link-local, or null.</summary>
    internal static IPAddress? FallbackGateway(IEnumerable<IPAddress> addresses)
    {
        foreach (var a in addresses)
        {
            if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a)) continue;
            var b = a.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254) continue;
            return new IPAddress([b[0], b[1], b[2], 1]);
        }
        return null;
    }

    private static bool IsDottedQuad(string text) =>
        text.Split('.') is { Length: 4 } parts && parts.All(p => byte.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out _));

    private static IEnumerable<IPAddress> LanAddresses() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses.Select(u => u.Address));

    private static void SendOneByte(IPEndPoint endpoint)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Connect(endpoint);
        socket.Send([0]);
    }

    [GeneratedRegex(@"^\s*gateway:\s*(\S+)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex GatewayLine();
}
