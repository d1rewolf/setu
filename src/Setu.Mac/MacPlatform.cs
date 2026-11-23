using Setu.Core;
using Setu.Core.Files;
using Setu.Core.Screen;
using Setu.Core.Server;
using Setu.Core.Shell;

namespace Setu.Mac;

/// <summary>The macOS parts for <see cref="SetuStartup"/>. Kept here, not in Program.cs, so the tests can check them.</summary>
public static class MacPlatform
{
    /// <param name="home">The user's home folder (protected folders are relative to it).</param>
    /// <param name="dataDirectory">~/Library/Application Support/setu: the lock, token, config, log and screenshot temp files.</param>
    /// <param name="log">The one request log of the run, shared with SetuStartup.</param>
    public static PlatformParts Create(string home, string dataDirectory, RequestLog log) =>
        Create(home, dataDirectory, log, new MacDesktop(dataDirectory));

    internal static PlatformParts Create(string home, string dataDirectory, RequestLog log, IDesktop desktop)
    {
        if (!Path.IsPathFullyQualified(home)) throw new InvalidOperationException($"The home folder '{home}' is not an absolute path.");
        var protectedPaths = MacProtectedPaths.For(home);
        return new PlatformParts(
            dataDirectory,
            () => LockFileInstance.TryAcquire(Path.Combine(dataDirectory, "setu.lock")),
            new UnixTokenFileProtector(),
            MacKeepAwake.Start,
            () => new ProcessGroupContainment(),
            new MacDetachedLauncher(log, protectedPaths),
            desktop,
            // ISO-8859-1: every byte round-trips. The time limit turns a wait on an unlisted privacy prompt into an error.
            new FileToolsOptions(28591, LineEndings.Lf, protectedPaths, TimeSpan.FromSeconds(120)),
            KeyVocabulary.Mac,
            ShellFlavor.Zsh,
            null,
            MacStartupPrompts.Run);
    }
}
