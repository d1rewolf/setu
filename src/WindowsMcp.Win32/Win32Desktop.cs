using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ModelContextProtocol;
using WindowsMcp.Core.Screen;

namespace WindowsMcp.Win32;

/// <summary>
/// IDesktop over Win32. Split into partial files: this one (session state, windows, focus),
/// Win32Desktop.Capture.cs (GDI screenshots) and Win32Desktop.Input.cs (SendInput).
/// The process is per-monitor DPI aware v2, so every coordinate here is a physical pixel on the virtual screen.
/// </summary>
public sealed partial class Win32Desktop : IDesktop
{
    public unsafe DesktopState State()
    {
        if (!NativeMethods.WTSQuerySessionInformation(
                0, NativeMethods.WTS_CURRENT_SESSION, NativeMethods.WTSConnectState, out var buffer, out var bytes))
            throw new McpException($"Could not query the session state: {new Win32Exception().Message}");
        int connectState;
        try
        {
            if (bytes < sizeof(int)) throw new McpException("Could not query the session state: Windows returned no data");
            connectState = *(int*)buffer;
        }
        finally
        {
            NativeMethods.WTSFreeMemory(buffer);
        }
        if (connectState != NativeMethods.WTSActive) return DesktopState.Disconnected;

        // While the screen is locked (or a UAC prompt is up) the input desktop is Winlogon, which a normal
        // process cannot open; when it can be opened, its name tells.
        var desktop = NativeMethods.OpenInputDesktop(0, false, NativeMethods.DESKTOP_READOBJECTS);
        if (desktop == 0) return DesktopState.Locked;
        try
        {
            return string.Equals(UserObjectName(desktop), "Default", StringComparison.OrdinalIgnoreCase)
                ? DesktopState.Available
                : DesktopState.Locked;
        }
        finally
        {
            _ = NativeMethods.CloseDesktop(desktop);
        }
    }

    public WindowInfo? ElevatedForegroundWindow()
    {
        var hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == 0 || NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
        var theirs = ProcessIntegrity(pid);
        // Unknown on either side: don't block; the input tools then behave as before.
        if (theirs is null || OwnIntegrity.Value is not { } ours || theirs <= ours) return null;
        _ = NativeMethods.GetWindowRect(hwnd, out var r);
        return new WindowInfo(
            hwnd, WindowTitle(hwnd), ProcessName(pid), (int)pid,
            r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, NativeMethods.IsIconic(hwnd), Foreground: true);
    }

    private static readonly Lazy<uint?> OwnIntegrity = new(() => TokenIntegrity(NativeMethods.GetCurrentProcess()));

    private static uint? ProcessIntegrity(uint pid)
    {
        var process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0) return null;
        try
        {
            return TokenIntegrity(process);
        }
        finally
        {
            _ = NativeMethods.CloseHandle(process);
        }
    }

    /// <summary>The mandatory integrity level RID (0x2000 medium, 0x3000 high, ...) of a process, or null if unreadable.</summary>
    private static unsafe uint? TokenIntegrity(nint process)
    {
        if (!NativeMethods.OpenProcessToken(process, NativeMethods.TOKEN_QUERY, out var token)) return null;
        try
        {
            // TOKEN_MANDATORY_LABEL: a SID_AND_ATTRIBUTES whose first field is the SID pointer, followed by the SID.
            const uint Size = 128;
            var buffer = stackalloc byte[(int)Size];
            if (!NativeMethods.GetTokenInformation(token, NativeMethods.TokenIntegrityLevel, buffer, Size, out _)) return null;
            var sid = *(nint*)buffer;
            var count = *NativeMethods.GetSidSubAuthorityCount(sid);
            return count == 0 ? null : *NativeMethods.GetSidSubAuthority(sid, (uint)(count - 1));
        }
        finally
        {
            _ = NativeMethods.CloseHandle(token);
        }
    }

    private static unsafe string? UserObjectName(nint handle)
    {
        const int Chars = 256;
        var name = stackalloc char[Chars];
        if (!NativeMethods.GetUserObjectInformation(handle, NativeMethods.UOI_NAME, name, Chars * sizeof(char), out _))
            return null;
        return new string(name);
    }

    public IReadOnlyList<WindowInfo> ListWindows()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        var names = new Dictionary<uint, string>();
        var result = new List<WindowInfo>();
        foreach (var hwnd in TopLevelWindows())
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || IsCloaked(hwnd)) continue;
            var title = WindowTitle(hwnd);
            if (string.IsNullOrEmpty(title)) continue;
            // The window can close between EnumWindows and here; skip it then.
            if (!NativeMethods.GetWindowRect(hwnd, out var r)) continue;
            if (NativeMethods.GetWindowThreadProcessId(hwnd, out var pid) == 0) continue;
            if (!names.TryGetValue(pid, out var processName))
                names[pid] = processName = ProcessName(pid);
            result.Add(new WindowInfo(
                hwnd, title, processName, (int)pid,
                r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
                NativeMethods.IsIconic(hwnd), hwnd == foreground));
        }
        return result;
    }

    public bool FocusWindow(long handle)
    {
        var hwnd = RequireWindow(handle);
        if (NativeMethods.IsIconic(hwnd)) _ = NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        // Already in front: do nothing more. Any synthetic input here could change its state.
        if (NativeMethods.GetForegroundWindow() == hwnd) return true;

        // Only succeeds when this process may set the foreground itself; failure is expected otherwise and
        // the outcome is judged by GetForegroundWindow below.
        _ = NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        // A zero-effect synthetic mouse event (no movement, no buttons) makes Windows treat this process as
        // having received the last input, which lifts the foreground lock. Unlike an Alt tap it cannot put
        // a window into menu mode. If it is blocked, SetForegroundWindow may still work; the result says.
        _ = TrySend([MouseInput(0, 0)]);

        _ = NativeMethods.SetForegroundWindow(hwnd);
        return NativeMethods.GetForegroundWindow() == hwnd;
    }

    private static nint RequireWindow(long handle)
    {
        var hwnd = (nint)handle;
        if (hwnd == 0 || !NativeMethods.IsWindow(hwnd))
            throw new McpException($"No window with handle {handle}; get a current handle from list_windows");
        return hwnd;
    }

    private static unsafe List<nint> TopLevelWindows()
    {
        var list = new List<nint>();
        var gc = GCHandle.Alloc(list);
        try
        {
            if (!NativeMethods.EnumWindows(&CollectWindow, GCHandle.ToIntPtr(gc)))
                throw new McpException($"Could not list windows: {new Win32Exception().Message}");
        }
        finally
        {
            gc.Free();
        }
        return list;
    }

    [UnmanagedCallersOnly]
    private static int CollectWindow(nint hwnd, nint state)
    {
        ((List<nint>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1; // keep enumerating
    }

    private static unsafe bool IsCloaked(nint hwnd)
    {
        int cloaked;
        var hr = NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, &cloaked, sizeof(int));
        // If DWM cannot answer, treat the window as not cloaked rather than hide it.
        return hr >= 0 && cloaked != 0;
    }

    private static unsafe string WindowTitle(nint hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0) return "";
        var buffer = new char[length + 1];
        int copied;
        fixed (char* p = buffer) copied = NativeMethods.GetWindowText(hwnd, p, buffer.Length);
        return copied <= 0 ? "" : new string(buffer, 0, copied);
    }

    private static string ProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return "?";
        }
    }
}
