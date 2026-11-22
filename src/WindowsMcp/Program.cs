using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using WindowsMcp.Core;
using WindowsMcp.Core.Files;
using WindowsMcp.Core.Server;
using WindowsMcp.Win32;

// A WinExe has no console of its own; attach to the parent's so failure lines show when run from a terminal.
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AttachConsole(int dwProcessId);
_ = AttachConsole(-1); // ATTACH_PARENT_PROCESS

var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "windows-mcp");
var log = RequestLog.Null;

int Fail(int code, string message)
{
    log.Write($"startup failed: {message}");
    Console.Error.WriteLine(message);
    return code;
}

static bool IsAddressInUse(Exception? ex)
{
    for (; ex is not null; ex = ex.InnerException)
        if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }) return true;
    return false;
}

// Held for the whole process lifetime (the using spans Run) so it is never collected.
using var instance = new Mutex(initiallyOwned: true, "Local\\windows-mcp", out var createdNew);
if (!createdNew)
    return Fail(2, "windows-mcp is already running for this user.");

var port = ServerOptions.DefaultPort;
try
{
    Directory.CreateDirectory(data);
    log = new RequestLog(Path.Combine(data, "windows-mcp.log"));
    var options = ServerOptions.Load(data);
    port = options.Port;
    var token = new TokenStore(data, new TokenFileAcl()).GetOrCreate();
    KeepAwake.Start();

    var app = ServerHost.Build(new ServerHostSettings(
        IPAddress.Loopback, port, token, log, new FileToolsOptions(AnsiCodePage.Current),
        () => new JobObjectContainment(), new DetachedLauncher(log), new Win32Desktop()));
    var started = log;
    app.Lifetime.ApplicationStarted.Register(() => started.Write($"listening on http://127.0.0.1:{port}/mcp"));
    app.Run();
    return 0;
}
catch (Exception ex) when (IsAddressInUse(ex))
{
    return Fail(3, $"Port {port} is in use; set \"port\" in {Path.Combine(data, "config.json")}");
}
catch (Exception ex)
{
    return Fail(1, ex.Message.ReplaceLineEndings(" "));
}
