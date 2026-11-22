using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Setu.Core.Shell;

/// <summary>
/// powershell.exe writes its error, warning, verbose and progress streams to a redirected pipe as CLIXML
/// ("#&lt; CLIXML" followed by an &lt;Objs&gt; document). This turns that back into the plain text a console
/// would show: the text of each &lt;S S="..."&gt; record, with XML entities and _xHHHH_ escapes undone.
/// Progress and other object records are dropped. Text without a CLIXML marker is returned unchanged, and a
/// block cut off by the output cap still yields the records read so far.
/// </summary>
public static partial class ClixmlDecoder
{
    private const string Marker = "#< CLIXML";

    public static string Decode(string text)
    {
        if (!text.Contains(Marker, StringComparison.Ordinal)) return text;

        var sb = new StringBuilder(text.Length);
        var pos = 0;
        foreach (Match block in ObjsBlock().Matches(text))
        {
            sb.Append(RemoveMarkers(text[pos..block.Index]));
            foreach (Match record in StringRecord().Matches(block.Groups["body"].Value))
                sb.Append(Unescape(record.Groups["text"].Value));
            pos = block.Index + block.Length;
        }
        sb.Append(RemoveMarkers(text[pos..]));
        return sb.ToString();
    }

    private static string RemoveMarkers(string segment) => MarkerLine().Replace(segment, "");

    private static string Unescape(string value)
    {
        var plain = WebUtility.HtmlDecode(value);
        return UnderscoreEscape().Replace(plain,
            m => ((char)int.Parse(m.Groups[1].ValueSpan, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
    }

    // An <Objs> document, or the start of one cut off at the end of the text.
    [GeneratedRegex(@"<Objs\b[^>]*>(?<body>.*?)(?:</Objs>|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ObjsBlock();

    // A top-level stream record such as <S S="Error">...</S>; strings inside objects use <S N="...">, so
    // requiring the S attribute skips them. A record cut off at the end still yields its text.
    [GeneratedRegex(@"<S S=""[^""]*"">(?<text>[^<]*)(?:</S>|\z)", RegexOptions.CultureInvariant)]
    private static partial Regex StringRecord();

    [GeneratedRegex(@"#< CLIXML\r?\n?", RegexOptions.CultureInvariant)]
    private static partial Regex MarkerLine();

    [GeneratedRegex(@"_x([0-9A-Fa-f]{4})_", RegexOptions.CultureInvariant)]
    private static partial Regex UnderscoreEscape();
}
