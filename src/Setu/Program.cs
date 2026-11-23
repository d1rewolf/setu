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

// One instance per user across all sessions: a copy started over SSH (a different, non-interactive
// session) must see the desktop copy, so the name is in the Global namespace and keyed by the user's SID.
// SetuStartup holds the returned handle for the whole run, so it is never collected.
static IDisposable? AcquireSingleInstance()
{
    var userSid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
    Mutex instance;
    bool createdNew;
    try
    {
        instance = new Mutex(initiallyOwned: true, $"Global\\setu-{userSid}", out createdNew);
    }
    catch (UnauthorizedAccessException)
    {
        // The mutex exists but this session's token cannot open it: another copy holds it.
        return null;
    }
    if (createdNew) return instance;
    instance.Dispose();
    return null;
}

// The builder overload builds the Win32 parts inside startup's failure handling and hands them the one
// RequestLog the whole run writes setu.log through.
return SetuStartup.Run(data, log => new PlatformParts(
    data,
    AcquireSingleInstance,
    new TokenFileAcl(),
    KeepAwake.Start,
    () => new JobObjectContainment(),
    new DetachedLauncher(log),
    new Win32Desktop(),
    new FileToolsOptions(AnsiCodePage.Current, LineEndings.Crlf, ProtectedPaths.None),
    KeyVocabulary.Windows,
    ShellFlavor.Windows,
    null));
