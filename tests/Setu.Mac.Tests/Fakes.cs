using Setu.Mac;

namespace Setu.Mac.Tests;

/// <summary>An IMacSystem whose every answer is set by the test. Defaults: on the console, unlocked, all granted.</summary>
internal sealed class FakeMacSystem : IMacSystem
{
    public bool Console { get; set; } = true;
    public bool Locked { get; set; }
    public bool Preflight { get; set; } = true;
    public bool PostEvents { get; set; } = true;
    public List<MacWindow> AllWindows { get; set; } = [];
    public List<MacWindow> OnScreenWindows { get; set; } = [];
    public (double X, double Y, double W, double H) Union { get; set; } = (0, 0, 1512, 982);
    public int OwnPid { get; set; } = 100;

    public bool OnConsole() => Console;
    public bool ScreenLocked() => Locked;
    public bool ScreenRecordingPreflight() => Preflight;
    public bool PostEventAllowed() => PostEvents;
    public IReadOnlyList<MacWindow> Windows(bool onScreenOnly) => onScreenOnly ? OnScreenWindows : AllWindows;
    public (double X, double Y, double W, double H) ScreenUnion() => Union;
}

/// <summary>Records each command; for screencapture, writes <see cref="Png"/> to the last argument (the out file).</summary>
internal sealed class FakeRunner : ICommandRunner
{
    public List<(string File, IReadOnlyList<string> Args)> Calls { get; } = [];
    public byte[] Png { get; set; } = PngBytes(3024, 1964);
    public byte[]? ResampledPng { get; set; }
    public (int Exit, string Stderr) CaptureResult { get; set; } = (0, "");

    public (int Exit, string Stderr) Run(string file, IReadOnlyList<string> args, TimeSpan timeout)
    {
        Calls.Add((file, args.ToList()));
        if (file == "/usr/sbin/screencapture")
        {
            if (CaptureResult.Exit == 0) File.WriteAllBytes(args[^1], Png);
            return CaptureResult;
        }
        if (file == "/usr/bin/sips" && ResampledPng is not null) File.WriteAllBytes(args[^1], ResampledPng);
        return (0, "");
    }

    /// <summary>Just the PNG signature and IHDR chunk, which is all MacDesktop reads.</summary>
    public static byte[] PngBytes(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .CopyTo(b, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), height);
        return b;
    }
}
