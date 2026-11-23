using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Setu.Core.Screen;

namespace Setu.Core.Tests;

public sealed class ScreenToolsTests
{
    private static (ScreenTools Tools, FakeDesktop Desktop) Make()
    {
        var d = new FakeDesktop();
        return (new ScreenTools(RequestLog.Null, d), d);
    }

    public static TheoryData<ScreenUse, string> ToolsByUse() => new()
    {
        { ScreenUse.See, "screenshot" },
        { ScreenUse.List, "list_windows" },
        { ScreenUse.Pointer, "click" },
        { ScreenUse.Pointer, "scroll" },
        { ScreenUse.Pointer, "focus_window" },
        { ScreenUse.Keyboard, "type_text" },
        { ScreenUse.Keyboard, "send_keys" },
    };

    private static void RunTool(ScreenTools t, string tool)
    {
        switch (tool)
        {
            case "screenshot": t.Screenshot().ToList(); break;
            case "list_windows": t.ListWindows(); break;
            case "click": t.Click(1, 2); break;
            case "scroll": t.Scroll(0, 0, 1); break;
            case "focus_window": t.FocusWindow(7); break;
            case "type_text": t.TypeText("x"); break;
            case "send_keys": t.SendKeys("enter"); break;
            default: throw new ArgumentException(tool);
        }
    }

    [Theory]
    [MemberData(nameof(ToolsByUse))]
    public void Check_Blocks_ThrowsMessageVerbatim_WithoutCallingDesktop(ScreenUse use, string tool)
    {
        var (t, d) = Make();
        d.Checks[use] = new("nope", true);
        var ex = Assert.Throws<McpException>(() => RunTool(t, tool));
        Assert.Equal("nope", ex.Message);
        Assert.Empty(d.Calls);
    }

    [Fact]
    public void Check_Note_AppendsToResult()
    {
        var (t, d) = Make();
        d.Checks[ScreenUse.Keyboard] = new("secure input on", false);
        Assert.Equal("Typed\nnote: secure input on", t.TypeText("x"));
        Assert.Contains("TypeText x", d.Calls);
    }

    [Fact]
    public void Check_Note_GoesInScreenshotHeaderBlock()
    {
        var (t, d) = Make();
        d.Checks[ScreenUse.See] = new("dim display", false);
        var text = Assert.IsType<TextContentBlock>(t.Screenshot().First());
        Assert.EndsWith("\nnote: dim display", text.Text);
    }

    [Fact]
    public void Check_OtherUseBlocked_DoesNotAffectThisTool()
    {
        var (t, d) = Make();
        d.Checks[ScreenUse.Keyboard] = new("nope", true);
        Assert.Equal("Clicked", t.Click(1, 2));
        Assert.Single(d.Calls);
    }

    [Fact]
    public void ListWindows_TabSeparatedLines()
    {
        var (t, d) = Make();
        d.Windows =
        [
            new(100, "Untitled - Notepad", "notepad", 12, 10, 20, 800, 600, false, true),
            new(200, "Docs", "chrome", 34, -5, 0, 100, 50, true, false),
        ];
        var lines = t.ListWindows().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Equal("100\t12\tnotepad\t10,20,800,600\tfg\tUntitled - Notepad", lines[0]);
        Assert.Equal("200\t34\tchrome\t-5,0,100,50\tmin\tDocs", lines[1]);
    }

    [Fact]
    public void ListWindows_ControlCharsInTitleAndProcess_BecomeSpaces()
    {
        var (t, d) = Make();
        d.Windows = [new(100, "a\tb\r\nc\u0001d", "no\ttepad", 12, 10, 20, 800, 600, false, false)];
        Assert.Equal("100\t12\tno tepad\t10,20,800,600\t\ta b  c d\n", t.ListWindows());
    }

    [Fact]
    public void ListWindows_NoWindows_SaysSo()
    {
        var (t, _) = Make();
        Assert.Equal("No windows.", t.ListWindows());
    }

    [Fact]
    public void Screenshot_ReturnsOriginScaleTextThenPng()
    {
        var (t, d) = Make();
        d.NextCapture = new([9, 8, 7], -1920, 0, 960, 540, 0.5);
        var blocks = t.Screenshot().ToList();
        Assert.Equal(2, blocks.Count);
        var text = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Equal("origin=(-1920,0) scale=0.5 size=960x540; screen_x = origin_x + image_x / scale", text.Text);
        var img = Assert.IsType<ImageContentBlock>(blocks[1]);
        Assert.Equal("image/png", img.MimeType);
        Assert.Equal(new byte[] { 9, 8, 7 }, img.DecodedData.ToArray());
        Assert.IsType<ScreenTarget>(d.LastCapture!.Value.Target);
    }

    [Fact]
    public void Screenshot_ScaleHasSixDecimals()
    {
        var (t, d) = Make();
        d.NextCapture = new([1], 0, 0, 1000, 500, 1000.0 / 2561);
        var text = Assert.IsType<TextContentBlock>(t.Screenshot().First());
        Assert.StartsWith("origin=(0,0) scale=0.390472 size=1000x500;", text.Text);
    }

    [Fact]
    public void Screenshot_ParsesWindowAndRegionTargets_AndMaxWidth()
    {
        var (t, d) = Make();
        t.Screenshot("window:123", 640).ToList();
        Assert.Equal(new WindowTarget(123), d.LastCapture!.Value.Target);
        Assert.Equal(640, d.LastCapture!.Value.MaxWidth);
        t.Screenshot("region:10,20,300,200").ToList();
        Assert.Equal(new RegionTarget(10, 20, 300, 200), d.LastCapture!.Value.Target);
        Assert.Null(d.LastCapture!.Value.MaxWidth);
    }

    [Theory]
    [InlineData("window:abc")]
    [InlineData("window:")]
    [InlineData("region:1,2,3")]
    [InlineData("region:1,2,0,5")]
    [InlineData("region:1,2,5,0")]
    [InlineData("region:a,b,c,d")]
    [InlineData("monitor")]
    public void Screenshot_BadTarget_Error(string target)
    {
        var (t, d) = Make();
        var ex = Assert.Throws<McpException>(() => t.Screenshot(target).ToList());
        Assert.Contains("target", ex.Message);
        Assert.DoesNotContain("Capture", d.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Screenshot_BadMaxWidth_Error(int w)
    {
        var (t, _) = Make();
        var ex = Assert.Throws<McpException>(() => t.Screenshot("screen", w).ToList());
        Assert.Contains("max_width", ex.Message);
    }

    [Fact]
    public void Click_BadButton_Error()
    {
        var (t, d) = Make();
        var ex = Assert.Throws<McpException>(() => t.Click(1, 2, "side"));
        Assert.Contains("button", ex.Message);
        Assert.Empty(d.Calls);
    }

    [Fact]
    public void Click_PassesArguments()
    {
        var (t, d) = Make();
        t.Click(5, 6, "Right", true);
        Assert.Equal("Click 5,6 Right True", Assert.Single(d.Calls));
    }

    [Fact]
    public void TypeText_And_Scroll_Delegate()
    {
        var (t, d) = Make();
        t.TypeText("hi");
        t.Scroll(1, 2, -3);
        Assert.Equal(["TypeText hi", "Scroll 1,2 -3"], d.Calls);
    }

    [Fact]
    public void SendKeys_ParsesAndDelegates()
    {
        var (t, d) = Make();
        t.SendKeys("ctrl+a delete");
        Assert.Equal(2, d.LastChords!.Count);
        Assert.Equal("delete", d.LastChords[1].Key);
    }

    [Fact]
    public void SendKeys_BadKey_Error_NoCall()
    {
        var (t, d) = Make();
        Assert.Throws<McpException>(() => t.SendKeys("ctrl+nope"));
        Assert.Empty(d.Calls);
    }

    [Fact]
    public void FocusWindow_Success_And_Refused()
    {
        var (t, d) = Make();
        Assert.Equal("Focused", t.FocusWindow(77));
        d.FocusResult = false;
        var ex = Assert.Throws<McpException>(() => t.FocusWindow(77));
        Assert.Equal("Windows refused to bring 77 to the foreground", ex.Message);
    }
}
