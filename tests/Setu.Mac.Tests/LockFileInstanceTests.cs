namespace Setu.Mac.Tests;

public sealed class LockFileInstanceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "setu-lock-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void LockFile_SecondAcquireFails_UntilFirstDisposed()
    {
        // The parent folder does not exist yet: the first start of a fresh install takes the lock before
        // the data folder is created.
        var path = Path.Combine(_dir, "data", "setu.lock");
        using (var first = LockFileInstance.TryAcquire(path))
        {
            Assert.NotNull(first);
            Assert.True(File.Exists(path));
            Assert.Null(LockFileInstance.TryAcquire(path));
        }

        using var again = LockFileInstance.TryAcquire(path);
        Assert.NotNull(again);
    }
}
