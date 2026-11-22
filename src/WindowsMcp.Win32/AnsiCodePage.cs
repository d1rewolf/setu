namespace WindowsMcp.Win32;

/// <summary>The system ANSI code page (GetACP), which console programs that do not use UTF-8 write in.</summary>
public static class AnsiCodePage
{
    public static int Current => (int)NativeMethods.GetACP();
}
