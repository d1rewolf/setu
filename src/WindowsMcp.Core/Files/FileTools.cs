using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace WindowsMcp.Core.Files;

public sealed record FileToolsOptions(int AnsiCodePage);

[McpServerToolType]
public sealed class FileTools(RequestLog log, FileToolsOptions options)
{
    private const int MaxReadBytes = 256 * 1024;

    [McpServerTool(Name = "read_file")]
    [Description("Reads a text file and returns it with numbered lines (like cat -n). The first line is a header with the file's encoding, line endings and total line count.")]
    public string ReadFile(
        [Description("Absolute Windows path. %VARS% are expanded.")] string path,
        [Description("1-based line number to start at.")] int offset = 1,
        [Description("Maximum number of lines to return.")] int limit = 2000)
        => ToolGuard.Run(log, "read_file", () =>
        {
            if (offset < 1) throw new ToolInputException("offset must be 1 or greater");
            if (limit < 1) throw new ToolInputException("limit must be 1 or greater");
            var full = PathResolver.Resolve(path);
            var bytes = ReadAllShared(full);
            if (TextFormat.LooksBinary(bytes))
                throw new ToolInputException($"Binary file; read_file only reads text: {path}");

            var format = TextFormat.Detect(bytes, options.AnsiCodePage);
            var text = Encoding.GetEncoding(format.Encoding.CodePage).GetString(bytes.AsSpan(format.PreambleLength));
            var lines = text.Split('\n').ToList();
            if (lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

            var sb = new StringBuilder();
            sb.Append($"encoding={format.Kind} line_endings={format.LineEndings} total_lines={lines.Count}\n");
            var bodyBytes = 0;
            var end = (int)Math.Min((long)offset - 1 + limit, lines.Count);
            for (var i = offset - 1; i < end; i++)
            {
                var row = $"{i + 1,6}\t{lines[i].TrimEnd('\r')}\n";
                bodyBytes += Encoding.UTF8.GetByteCount(row);
                if (bodyBytes > MaxReadBytes)
                {
                    sb.Append("[truncated at 256 KiB; use offset to continue]\n");
                    break;
                }
                sb.Append(row);
            }
            return sb.ToString();
        });

    [McpServerTool(Name = "write_file")]
    [Description("Writes a text file, replacing it if it exists. An existing file keeps its encoding and line endings unless overridden; a new file is UTF-8 without BOM with CRLF line endings.")]
    public string WriteFile(
        [Description("Absolute Windows path. %VARS% are expanded. The parent directory must exist.")] string path,
        [Description("Full file content. Newlines are converted to the chosen line endings.")] string content,
        [Description("Optional: utf8, utf8bom, utf16le, utf16be or ansi.")] string? encoding = null,
        [Description("Optional: crlf or lf.")] string? line_endings = null)
        => ToolGuard.Run(log, "write_file", () =>
        {
            var full = PathResolver.Resolve(path);
            var dir = Path.GetDirectoryName(full)!;
            if (!Directory.Exists(dir)) throw new ToolInputException($"Parent directory does not exist: {dir}");

            TextFormat? existing = null;
            if (File.Exists(full))
            {
                var current = ReadAllShared(full);
                if (encoding is null && TextFormat.LooksBinary(current))
                    throw new ToolInputException($"Existing file looks binary; pass encoding explicitly to overwrite it: {path}");
                existing = TextFormat.Detect(current, options.AnsiCodePage);
            }

            var kind = encoding is null
                ? existing?.Kind ?? FileEncodingKind.Utf8
                : ParseEncoding(encoding);
            var crlf = line_endings is null
                ? existing is null || existing.LineEndings is not LineEndings.Lf
                : ParseLineEndings(line_endings);

            var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
            if (crlf) normalized = normalized.Replace("\n", "\r\n");

            var enc = StrictEncoding(kind);
            byte[] body;
            try
            {
                body = enc.GetBytes(normalized);
            }
            catch (EncoderFallbackException)
            {
                throw new ToolInputException($"Text cannot be represented in {kind}");
            }
            byte[] bytes = [.. enc.GetPreamble(), .. body];
            AtomicWriter.Write(full, bytes);
            return $"Wrote {bytes.Length} bytes to {path} ({kind}, {(crlf ? LineEndings.Crlf : LineEndings.Lf)})";
        });

    [McpServerTool(Name = "edit_file")]
    [Description("Replaces an exact string in a text file. The file keeps its encoding, BOM and line endings, and bytes outside the replaced text are unchanged. old_string must match exactly once unless replace_all is true.")]
    public string EditFile(
        [Description("Absolute Windows path. %VARS% are expanded.")] string path,
        [Description("Exact text to find. Must not be empty. On a CRLF file, \\n is treated as \\r\\n.")] string old_string,
        [Description("Replacement text. On a CRLF file, \\n is written as \\r\\n.")] string new_string,
        [Description("Replace every occurrence instead of requiring exactly one.")] bool replace_all = false)
        => ToolGuard.Run(log, "edit_file", () =>
        {
            if (old_string.Length == 0) throw new ToolInputException("old_string must not be empty");
            var full = PathResolver.Resolve(path);
            var bytes = ReadAllShared(full);
            if (TextFormat.LooksBinary(bytes))
                throw new ToolInputException($"Binary file; edit_file only edits text: {path}");

            var format = TextFormat.Detect(bytes, options.AnsiCodePage);
            if (format.LineEndings == LineEndings.Crlf)
            {
                old_string = LoneLf.Replace(old_string, "\r\n");
                new_string = LoneLf.Replace(new_string, "\r\n");
            }

            var enc = StrictEncoding(format.Kind);
            byte[] oldBytes, newBytes;
            try
            {
                oldBytes = enc.GetBytes(old_string);
                newBytes = enc.GetBytes(new_string);
            }
            catch (EncoderFallbackException)
            {
                throw new ToolInputException($"Text cannot be represented in {format.Kind}");
            }

            var alignment = format.Kind is FileEncodingKind.Utf16Le or FileEncodingKind.Utf16Be ? 2 : 1;
            var result = ByteSplice.Replace(bytes, oldBytes, newBytes, format.PreambleLength, alignment, replace_all, out var count);
            if (count == 0) throw new ToolInputException($"old_string not found in {path}");
            if (count > 1 && !replace_all)
                throw new ToolInputException($"old_string found {count} times; pass more context or replace_all=true");

            AtomicWriter.Write(full, result);
            return $"Replaced {count} occurrence(s) in {path}";
        });

    private static readonly Regex LoneLf = new(@"(?<!\r)\n", RegexOptions.CultureInvariant);

    private Encoding StrictEncoding(FileEncodingKind kind)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // idempotent; needed for ANSI code pages
        return kind switch
        {
            FileEncodingKind.Utf8 => new UTF8Encoding(false, true),
            FileEncodingKind.Utf8Bom => new UTF8Encoding(true, true),
            FileEncodingKind.Utf16Le => new UnicodeEncoding(false, true, true),
            FileEncodingKind.Utf16Be => new UnicodeEncoding(true, true, true),
            _ => Encoding.GetEncoding(options.AnsiCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
        };
    }

    private static FileEncodingKind ParseEncoding(string value) => value.ToLowerInvariant() switch
    {
        "utf8" => FileEncodingKind.Utf8,
        "utf8bom" => FileEncodingKind.Utf8Bom,
        "utf16le" => FileEncodingKind.Utf16Le,
        "utf16be" => FileEncodingKind.Utf16Be,
        "ansi" => FileEncodingKind.Ansi,
        _ => throw new ToolInputException($"Unknown encoding '{value}'; use utf8, utf8bom, utf16le, utf16be or ansi"),
    };

    private static bool ParseLineEndings(string value) => value.ToLowerInvariant() switch
    {
        "crlf" => true,
        "lf" => false,
        _ => throw new ToolInputException($"Unknown line_endings '{value}'; use crlf or lf"),
    };

    private static byte[] ReadAllShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }
}
