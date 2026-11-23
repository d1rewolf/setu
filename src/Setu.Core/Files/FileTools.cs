using System.ComponentModel;
using System.Globalization;
using System.IO.Enumeration;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace Setu.Core.Files;

public sealed record FileToolsOptions(int FallbackCodePage, LineEndings NewFileLineEndings, ProtectedPaths Protected);

[McpServerToolType]
public sealed class FileTools(RequestLog log, FileToolsOptions options)
{
    private const int MaxReadBytes = 256 * 1024;
    private const int MaxSearchLimit = 2000;
    private const int MaxMatchTextLength = 300;
    private const long MaxContentSearchBytes = 10L * 1024 * 1024;

    [McpServerTool(Name = "read_file")]
    [Description("Reads a text file and returns it with numbered lines (like cat -n). The first line is a header with the file's encoding, line endings and total line count.")]
    public string ReadFile(
        [Description("Absolute path. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder.")] string path,
        [Description("1-based line number to start at.")] int offset = 1,
        [Description("Maximum number of lines to return.")] int limit = 2000)
        => Run("read_file", path, () =>
        {
            if (offset < 1) throw new ToolInputException("offset must be 1 or greater");
            if (limit < 1) throw new ToolInputException("limit must be 1 or greater");
            var full = PathResolver.Resolve(path, options.Protected);
            var bytes = ReadAllShared(full);
            if (TextFormat.LooksBinary(bytes))
                throw new ToolInputException($"Binary file; read_file only reads text: {path}");

            var format = TextFormat.Detect(bytes, options.FallbackCodePage);
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
    [Description("Writes a text file, replacing it if it exists. An existing file keeps its encoding and line endings unless overridden; a new file is UTF-8 without BOM, with CRLF line endings on Windows and LF on macOS.")]
    public string WriteFile(
        [Description("Absolute path. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder. The parent directory must exist.")] string path,
        [Description("Full file content. Newlines are converted to the chosen line endings.")] string content,
        [Description("Optional: utf8, utf8bom, utf16le, utf16be or ansi.")] string? encoding = null,
        [Description("Optional: crlf or lf.")] string? line_endings = null)
        => Run("write_file", path, () =>
        {
            var full = PathResolver.Resolve(path, options.Protected);
            var dir = Path.GetDirectoryName(full)!;
            if (!Directory.Exists(dir)) throw new ToolInputException($"Parent directory does not exist: {dir}");

            TextFormat? existing = null;
            if (File.Exists(full))
            {
                var current = ReadAllShared(full);
                if (encoding is null && TextFormat.LooksBinary(current))
                    throw new ToolInputException($"Existing file looks binary; pass encoding explicitly to overwrite it: {path}");
                existing = TextFormat.Detect(current, options.FallbackCodePage);
            }

            var kind = encoding is null
                ? existing?.Kind ?? FileEncodingKind.Utf8
                : ParseEncoding(encoding);
            var crlf = line_endings is null
                ? existing?.LineEndings switch
                {
                    LineEndings.Lf => false,
                    LineEndings.Mixed or LineEndings.Crlf => true,
                    _ => options.NewFileLineEndings == LineEndings.Crlf, // new file, or an existing one with no line breaks
                }
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
        [Description("Absolute path. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder.")] string path,
        [Description("Exact text to find. Must not be empty. On a CRLF file, \\n is treated as \\r\\n.")] string old_string,
        [Description("Replacement text. On a CRLF file, \\n is written as \\r\\n.")] string new_string,
        [Description("Replace every occurrence instead of requiring exactly one.")] bool replace_all = false)
        => Run("edit_file", path, () =>
        {
            if (old_string.Length == 0) throw new ToolInputException("old_string must not be empty");
            var full = PathResolver.Resolve(path, options.Protected);
            var bytes = ReadAllShared(full);
            if (TextFormat.LooksBinary(bytes))
                throw new ToolInputException($"Binary file; edit_file only edits text: {path}");

            var format = TextFormat.Detect(bytes, options.FallbackCodePage);
            if (format.LineEndings == LineEndings.Crlf)
            {
                old_string = LoneLf.Replace(old_string, "\r\n");
                new_string = LoneLf.Replace(new_string, "\r\n");
            }

            // A byte-level match in a multi-byte ANSI code page (932, 936, 949, 950...) can start on the trail
            // byte of a double-byte character and corrupt the file, so only single-byte code pages are edited.
            if (format.Kind == FileEncodingKind.Ansi && !StrictEncoding(format.Kind).IsSingleByte)
                throw new ToolInputException(
                    $"edit_file cannot safely edit this file: it is in the ANSI code page {options.FallbackCodePage}, which uses double-byte characters. " +
                    "Read it with read_file and write the whole file back with write_file instead.");

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

    [McpServerTool(Name = "list_dir")]
    [Description("Lists a directory, one entry per line: type (d or f), size in bytes, last write time (local, ISO-8601 with offset), name, separated by tabs. The last line is '<shown> of <total> entries'. Use sort=time with descending=true to find the newest file.")]
    public string ListDir(
        [Description("Absolute path of the directory. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder.")] string path,
        [Description("Optional wildcard on the entry name, such as *.mkv.")] string? pattern = null,
        [Description("Sort key: name, time or size. Directories come first only when sorting by name.")] string sort = "name",
        [Description("Reverse the sort order (newest or largest first).")] bool descending = false,
        [Description("Maximum number of entries to return.")] int limit = 500)
        => Run("list_dir", path, () =>
        {
            var key = sort.ToLowerInvariant();
            if (key is not ("name" or "time" or "size"))
                throw new ToolInputException($"Unknown sort '{sort}'; use name, time or size");
            if (limit < 1) throw new ToolInputException("limit must be 1 or greater");
            var full = PathResolver.Resolve(path, options.Protected);
            if (!Directory.Exists(full)) throw new ToolInputException($"Directory does not exist: {path}");

            var entries = new DirectoryInfo(full)
                .EnumerateFileSystemInfos("*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 })
                .Where(e => string.IsNullOrEmpty(pattern) || FileSystemName.MatchesSimpleExpression(pattern, e.Name, true))
                .Select(e => (Info: e, IsDir: e is DirectoryInfo, Size: e is FileInfo f ? f.Length : 0L, Time: e.LastWriteTime))
                .ToList();

            IOrderedEnumerable<(FileSystemInfo Info, bool IsDir, long Size, DateTime Time)> ordered = key switch
            {
                "time" => descending ? entries.OrderByDescending(e => e.Time) : entries.OrderBy(e => e.Time),
                "size" => descending ? entries.OrderByDescending(e => e.Size) : entries.OrderBy(e => e.Size),
                _ => descending
                    ? entries.OrderByDescending(e => e.IsDir).ThenByDescending(e => e.Info.Name, StringComparer.OrdinalIgnoreCase)
                    : entries.OrderByDescending(e => e.IsDir).ThenBy(e => e.Info.Name, StringComparer.OrdinalIgnoreCase),
            };
            if (key != "name") ordered = ordered.ThenBy(e => e.Info.Name, StringComparer.OrdinalIgnoreCase);

            var sb = new StringBuilder();
            var shown = 0;
            foreach (var e in ordered.Take(limit))
            {
                sb.Append(e.IsDir ? 'd' : 'f').Append('\t')
                  .Append(e.Size.ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Time.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Info.Name).Append('\n');
                shown++;
            }
            sb.Append($"{shown} of {entries.Count} entries\n");
            return sb.ToString();
        });

    [McpServerTool(Name = "search")]
    [Description("Searches a directory tree recursively by file name, by file content, or both. Content matches are returned as 'path:line: text'; name-only matches as 'path'. Binary files and files over 10 MiB are skipped for content search.")]
    public string Search(
        [Description("Absolute path of the directory to search. On Windows %VARS% are expanded; on macOS a leading ~ is your home folder.")] string path,
        [Description("Optional wildcard on the file name, such as *.log.")] string? name_pattern = null,
        [Description("Optional .NET regular expression matched against each line of text files.")] string? content_regex = null,
        [Description("Maximum number of matches to return (clamped to 2000).")] int limit = 200)
        => Run("search", path, () =>
        {
            if (string.IsNullOrEmpty(name_pattern) && string.IsNullOrEmpty(content_regex))
                throw new ToolInputException("Provide name_pattern, content_regex or both");
            if (limit < 1) throw new ToolInputException("limit must be 1 or greater");
            limit = Math.Min(limit, MaxSearchLimit);
            var full = PathResolver.Resolve(path, options.Protected);
            if (!Directory.Exists(full)) throw new ToolInputException($"Directory does not exist: {path}");

            Regex? regex = null;
            if (!string.IsNullOrEmpty(content_regex))
            {
                try
                {
                    regex = new Regex(content_regex, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                }
                catch (ArgumentException ex)
                {
                    throw new ToolInputException($"Invalid content_regex: {ex.Message}");
                }
            }

            var matches = new StringBuilder();
            var count = 0;
            var reached = false;
            var tooLarge = 0;
            var unreadable = 0;
            var timedOut = new List<string>();

            bool Add(string row)
            {
                if (count >= limit) { reached = true; return false; }
                matches.Append(row).Append('\n');
                count++;
                return true;
            }

            // AttributesToSkip = 0: Hidden and System files and directories (such as AppData) are searched too,
            // as list_dir shows them. A protected folder is never entered: even looking inside could block on a prompt.
            var skippedProtected = new List<string>();
            var options2 = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };
            var files = new FileSystemEnumerable<string>(full, static (ref entry) => entry.ToFullPath(), options2)
            {
                ShouldIncludePredicate = static (ref entry) => !entry.IsDirectory,
                ShouldRecursePredicate = (ref entry) =>
                {
                    var dir = entry.ToFullPath();
                    if (options.Protected.Match(dir) is null) return true;
                    skippedProtected.Add(dir);
                    return false;
                },
            };
            foreach (var file in files)
            {
                if (!string.IsNullOrEmpty(name_pattern)
                    && !FileSystemName.MatchesSimpleExpression(name_pattern, Path.GetFileName(file), true))
                    continue;

                if (regex is null)
                {
                    if (!Add(file)) break;
                    continue;
                }

                byte[] bytes;
                try
                {
                    if (new FileInfo(file).Length > MaxContentSearchBytes) { tooLarge++; continue; }
                    bytes = ReadAllShared(file);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                    continue;
                }
                if (TextFormat.LooksBinary(bytes)) continue;

                var format = TextFormat.Detect(bytes, options.FallbackCodePage);
                var text = LenientEncoding(format.Kind).GetString(bytes.AsSpan(format.PreambleLength));
                var lineNo = 0;
                try
                {
                    foreach (var line in text.Split('\n'))
                    {
                        lineNo++;
                        var clean = line.TrimEnd('\r');
                        if (!regex.IsMatch(clean)) continue;
                        var shownText = clean.Length > MaxMatchTextLength ? clean[..MaxMatchTextLength] : clean;
                        if (!Add($"{file}:{lineNo}: {shownText}")) break;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    timedOut.Add(file);
                }
                if (reached) break;
            }

            var sb = new StringBuilder();
            if (count == 0) sb.Append("No matches\n");
            sb.Append(matches);
            if (tooLarge > 0) sb.Append($"[skipped {tooLarge} file(s) over 10 MiB for content search]\n");
            if (unreadable > 0) sb.Append($"[skipped {unreadable} unreadable file(s)]\n");
            foreach (var f in timedOut) sb.Append($"[regex timed out after 2 s, skipped rest of file: {f}]\n");
            if (reached) sb.Append("[limit reached]\n");
            if (skippedProtected.Count > 0) sb.Append($"skipped protected folders: {string.Join(", ", skippedProtected)}\n");
            return sb.ToString();
        });

    /// <summary>
    /// ToolGuard.Run, plus on non-Windows a refused open (EPERM or EACCES, which .NET raises as
    /// UnauthorizedAccessException) becomes a plain "permission denied" for the path the agent gave.
    /// </summary>
    private T Run<T>(string tool, string path, Func<T> body)
        => ToolGuard.Run(log, tool, () =>
        {
            try
            {
                return body();
            }
            catch (UnauthorizedAccessException) when (!OperatingSystem.IsWindows())
            {
                // Resolve throws the protected-folder message when the path matches one.
                PathResolver.Resolve(path, options.Protected);
                throw new ToolInputException($"{path}: permission denied");
            }
        });

    private Encoding LenientEncoding(FileEncodingKind kind)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return kind switch
        {
            FileEncodingKind.Utf8 or FileEncodingKind.Utf8Bom => new UTF8Encoding(false),
            FileEncodingKind.Utf16Le => new UnicodeEncoding(false, false),
            FileEncodingKind.Utf16Be => new UnicodeEncoding(true, false),
            _ => Encoding.GetEncoding(options.FallbackCodePage),
        };
    }

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
            _ => Encoding.GetEncoding(options.FallbackCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
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
