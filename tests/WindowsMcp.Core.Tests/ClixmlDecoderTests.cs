using WindowsMcp.Core.Shell;

namespace WindowsMcp.Core.Tests;

public sealed class ClixmlDecoderTests
{
    // What powershell.exe 5.1 writes to a redirected stderr for: Write-Progress ...; Write-Error "boom"
    private const string WriteErrorWithProgress =
        "#< CLIXML\r\n" +
        "<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\">" +
        "<Obj S=\"progress\" RefId=\"0\"><TN RefId=\"0\"><T>System.Management.Automation.PSCustomObject</T><T>System.Object</T></TN>" +
        "<MS><I64 N=\"SourceId\">1</I64><PR N=\"Record\"><AV>Preparing modules for first use.</AV><AI>0</AI><Nil />" +
        "<PI>-1</PI><PC>-1</PC><T>Completed</T><SR>-1</SR><SD> </SD></PR></MS></Obj>" +
        "<S S=\"Error\">Write-Error \"boom\" : boom_x000D__x000A_</S>" +
        "<S S=\"Error\">    + CategoryInfo          : NotSpecified: (:) [Write-Error], WriteErrorException_x000D__x000A_</S>" +
        "<S S=\"Error\">    + FullyQualifiedErrorId : Microsoft.PowerShell.Commands.WriteErrorException_x000D__x000A_</S>" +
        "<S S=\"Error\"> _x000D__x000A_</S>" +
        "</Objs>";

    [Fact]
    public void Decode_WriteError_PlainTextWithoutProgress()
    {
        var text = ClixmlDecoder.Decode(WriteErrorWithProgress);
        Assert.Equal(
            "Write-Error \"boom\" : boom\r\n" +
            "    + CategoryInfo          : NotSpecified: (:) [Write-Error], WriteErrorException\r\n" +
            "    + FullyQualifiedErrorId : Microsoft.PowerShell.Commands.WriteErrorException\r\n" +
            " \r\n",
            text);
        Assert.DoesNotContain("CLIXML", text);
        Assert.DoesNotContain("Preparing modules", text);
    }

    [Fact]
    public void Decode_UnescapesEntitiesAndUnderscoreSequences()
    {
        const string input = "#< CLIXML\r\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\">" +
            "<S S=\"Error\">a &lt;b&gt; &amp; &quot;c&quot; my_x005F_var_x0009_tab_x000A_</S>" +
            "<S S=\"warning\">careful_x000A_</S></Objs>";
        Assert.Equal("a <b> & \"c\" my_var\ttab\ncareful\n", ClixmlDecoder.Decode(input));
    }

    [Fact]
    public void Decode_KeepsPlainTextAroundTheBlock()
    {
        var input = "native output\r\n" + WriteErrorWithProgress + "\r\nafter";
        var text = ClixmlDecoder.Decode(input);
        Assert.StartsWith("native output\r\nWrite-Error \"boom\" : boom\r\n", text);
        Assert.EndsWith("\r\nafter", text);
    }

    [Fact]
    public void Decode_TruncatedBlock_KeepsPartialRecord()
    {
        var cut = WriteErrorWithProgress[..WriteErrorWithProgress.IndexOf("CategoryInfo", StringComparison.Ordinal)];
        Assert.Equal("Write-Error \"boom\" : boom\r\n    + ", ClixmlDecoder.Decode(cut));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text\r\nwith <S S=\"Error\">markup-looking</S> bits\r\n")]
    public void Decode_NonClixml_Untouched(string input) => Assert.Equal(input, ClixmlDecoder.Decode(input));
}
