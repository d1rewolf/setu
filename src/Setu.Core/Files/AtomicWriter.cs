namespace Setu.Core.Files;

public static class AtomicWriter
{
    /// <summary>
    /// Writes to a temp file beside the target, then swaps it in. File.Replace keeps the target's
    /// ACL and attributes. The temp file is removed if anything fails.
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        var tmp = $"{path}.wmcp-{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch
        {
            try { File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
