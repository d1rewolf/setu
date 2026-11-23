using ModelContextProtocol;
using Setu.Core;
using Setu.Core.Screen;
using Setu.Mac;

namespace Setu.Mac.Tests;

public sealed class MacDesktopCaptureTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("setu-mac-tests-").FullName;

    public void Dispose() => Directory.Delete(dir, recursive: true);

    private MacDesktop Desktop(FakeMacSystem sys, FakeRunner run) => new(sys, run, dir);

    [Fact]
    public void Screen_RunsScreencaptureIntoDataFolder_ReadsSize_DeletesFile()
    {
        var run = new FakeRunner();
        var cap = Desktop(new FakeMacSystem(), run).Capture(new ScreenTarget(), null);

        var (file, args) = Assert.Single(run.Calls);
        Assert.Equal("/usr/sbin/screencapture", file);
        Assert.Equal(["-x", "-t", "png", "-R", "0,0,1512,982"], args.Take(5));
        Assert.Matches(@"/capture-[0-9a-f-]{36}\.png$", args[^1]);
        Assert.Equal(dir, Path.GetDirectoryName(args[^1]));
        Assert.False(File.Exists(args[^1]));
        Assert.Equal(run.Png, cap.Png);
        Assert.Equal((0, 0, 3024, 1964, 2.0), (cap.OriginX, cap.OriginY, cap.PixelWidth, cap.PixelHeight, cap.Scale));
    }

    [Fact]
    public void MaxWidth_RunsSips_AndReadsResampledSize()
    {
        var run = new FakeRunner { ResampledPng = FakeRunner.PngBytes(1000, 649) };
        var cap = Desktop(new FakeMacSystem(), run).Capture(new ScreenTarget(), 1000);

        Assert.Equal(2, run.Calls.Count);
        var (file, args) = run.Calls[1];
        Assert.Equal("/usr/bin/sips", file);
        Assert.Equal(["--resampleWidth", "1000", run.Calls[0].Args[^1]], args);
        Assert.Equal((1000, 649, 1000 / 1512.0), (cap.PixelWidth, cap.PixelHeight, cap.Scale));
    }

    [Fact]
    public void Window_UsesWindowOriginAndWidth()
    {
        var sys = new FakeMacSystem
        {
            AllWindows = [new MacWindow(42, 200, "TextEdit", "notes", 0, 100, 50, 756, 400, true)],
        };
        var cap = Desktop(sys, new FakeRunner { Png = FakeRunner.PngBytes(1512, 800) }).Capture(new WindowTarget(42), null);
        Assert.Equal((100, 50, 2.0), (cap.OriginX, cap.OriginY, cap.Scale));
    }

    [Fact]
    public void Window_Unknown_ThrowsInputError_WithoutRunning()
    {
        var run = new FakeRunner();
        var ex = Assert.Throws<ToolInputException>(() => Desktop(new FakeMacSystem(), run).Capture(new WindowTarget(7), null));
        Assert.Equal("No window with handle 7", ex.Message);
        Assert.Empty(run.Calls);
    }

    [Fact]
    public void Region_OriginIsRegion()
    {
        var cap = Desktop(new FakeMacSystem(), new FakeRunner { Png = FakeRunner.PngBytes(200, 100) })
            .Capture(new RegionTarget(30, 40, 100, 50), null);
        Assert.Equal((30, 40, 2.0), (cap.OriginX, cap.OriginY, cap.Scale));
    }

    [Fact]
    public void NonZeroExit_Throws_WithStderr_AndLeavesNoFile()
    {
        var run = new FakeRunner { CaptureResult = (1, "could not create image from display\n") };
        var ex = Assert.Throws<McpException>(() => Desktop(new FakeMacSystem(), run).Capture(new ScreenTarget(), null));
        Assert.Equal("screencapture failed (exit 1): could not create image from display", ex.Message);
        Assert.Empty(Directory.GetFiles(dir));
    }

    [Fact]
    public void EmptyFile_Throws()
    {
        var run = new FakeRunner { Png = [] };
        var ex = Assert.Throws<McpException>(() => Desktop(new FakeMacSystem(), run).Capture(new ScreenTarget(), null));
        Assert.Equal("screencapture failed (exit 0): no image was written", ex.Message);
        Assert.Empty(Directory.GetFiles(dir));
    }
}
