namespace Setu.Mac;

/// <summary>
/// One Setu per user: an exclusive lock on a file, held until the returned handle is disposed or the process
/// ends. On macOS (and Linux) .NET takes FileShare.None as <c>flock(LOCK_EX | LOCK_NB)</c>, which the kernel
/// drops with the process, so a crash never leaves a stale lock. The file itself stays; only the lock counts.
/// </summary>
public static class LockFileInstance
{
    /// <summary>
    /// The held lock, or null when another holder has it. Creates the parent folder. Other failures (no
    /// permission, a bad path) throw, so they are reported as such and not as "already running".
    /// </summary>
    public static IDisposable? TryAcquire(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (ex.GetType() == typeof(IOException))
        {
            // The lock is held elsewhere (EWOULDBLOCK). Subclasses such as DirectoryNotFoundException are real errors.
            return null;
        }
    }
}
