using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Setu.Core;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Server;
using Setu.Win32;

// A WinExe has no console of its own; attach to the parent's so failure lines show when run from a terminal.
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool AttachConsole(int dwProcessId);
_ = AttachConsole(-1); // ATTACH_PARENT_PROCESS

var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "setu");
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

// One instance per user across all sessions: a copy started over SSH (a different, non-interactive
// session) must see the desktop copy, so the name is in the Global namespace and keyed by the user's SID.
// Held for the whole process lifetime (the using spans Run) so it is never collected.
var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
Mutex? instance;
bool createdNew;
try
{
    instance = new Mutex(initiallyOwned: true, $"Global\\setu-{userSid}", out createdNew);
}
catch (UnauthorizedAccessException)
{
    // The mutex exists but this session's token cannot open it: another copy holds it.
    instance = null;
    createdNew = false;
}
using var instanceHandle = instance;
if (!createdNew)
{
    // Best effort: the log may not exist or be writable; RequestLog swallows failures.
    log = new RequestLog(Path.Combine(data, "setu.log"));
    return Fail(2, "setu is already running for this user.");
}

var port = ServerOptions.DefaultPort;
try
{
    Directory.CreateDirectory(data);
    log = new RequestLog(Path.Combine(data, "setu.log"));
    var options = ServerOptions.Load(data);
    port = options.Port;
    var token = new TokenStore(data, new TokenFileAcl()).GetOrCreate();
    try
    {
        KeepAwake.Start();
    }
    catch (Exception ex)
    {
        // Sleep prevention is a convenience: the server still works without it.
        log.Write($"keep-awake failed, continuing without it: {ex.Message}");
    }

    var app = ServerHost.Build(new ServerHostSettings(
        IPAddress.Loopback, port, token, log, new FileToolsOptions(AnsiCodePage.Current, LineEndings.Crlf, ProtectedPaths.None),
        () => new JobObjectContainment(), new DetachedLauncher(log), new Win32Desktop(), KeyVocabulary.Windows));
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
