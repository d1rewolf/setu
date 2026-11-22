using System.Text;
using WindowsMcp.Core.Shell;

namespace WindowsMcp.Core.Tests;

public sealed class CommandLinesTests
{
    [Fact]
    public void CommandLines_PowerShell_EncodesUtf16LeBase64()
    {
        var args = CommandLines.PowerShell("Get-ChildItem 'C:\\Users\\Jöhn'");
        const string prefix = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand ";
        Assert.StartsWith(prefix, args);
        var decoded = Encoding.Unicode.GetString(Convert.FromBase64String(args[prefix.Length..]));
        Assert.Equal(
            "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$ProgressPreference='SilentlyContinue';Get-ChildItem 'C:\\Users\\Jöhn'",
            decoded);
        Assert.Contains("OutputEncoding", decoded);
        Assert.EndsWith("Get-ChildItem 'C:\\Users\\Jöhn'", decoded);
    }

    [Fact]
    public void CommandLines_Cmd_Shape()
    {
        Assert.Equal("/d /s /c \"chcp 65001>nul & dir\"", CommandLines.Cmd("dir"));
    }
}
