using Setu.Mac.Native;
using static Setu.Mac.Native.CoreFoundation;

namespace Setu.Mac;

/// <summary>
/// Keeps the Mac and its display awake while Setu runs: two IOKit power assertions, held for the process
/// lifetime and released by macOS when the process ends. Keeping the display awake also keeps the screen from
/// locking on idle, so the screen tools work unattended. <c>pmset -g assertions</c> shows both.
/// </summary>
public static class MacKeepAwake
{
    internal const string AssertionName = "Setu keeps this Mac awake for agents";

    private static readonly Lock Gate = new();
    private static readonly List<uint> Held = [];

    /// <summary>Idempotent. Throws InvalidOperationException when macOS refuses an assertion.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (Held.Count == 0) Held.Add(Create("PreventUserIdleSystemSleep"));
            if (Held.Count == 1) Held.Add(Create("PreventUserIdleDisplaySleep"));
        }
    }

    private static uint Create(string type)
    {
        var cfType = CFStringCreateWithCString(0, type, kCFStringEncodingUTF8);
        var cfName = CFStringCreateWithCString(0, AssertionName, kCFStringEncodingUTF8);
        try
        {
            if (cfType == 0 || cfName == 0) throw new InvalidOperationException($"could not create the strings for {type}");
            var result = IOKit.IOPMAssertionCreateWithName(cfType, IOKit.kIOPMAssertionLevelOn, cfName, out var id);
            if (result != 0)
                throw new InvalidOperationException($"IOPMAssertionCreateWithName({type}) returned 0x{result:X8}");
            return id;
        }
        finally
        {
            if (cfType != 0) CFRelease(cfType);
            if (cfName != 0) CFRelease(cfName);
        }
    }
}
