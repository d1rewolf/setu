using System.Text;
using Setu.Core.Shell;

namespace Setu.Core.Tests;

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

    [Fact]
    public void Zsh_CommandLine_Exact()
    {
        Assert.Equal(
            ["-e", "setpgrp(0,0); exec @ARGV or die $!", "--", "/bin/zsh", "-l", "-c", "echo 'a b'"],
            CommandLines.Zsh("/bin/zsh", "echo 'a b'"));
    }
}
