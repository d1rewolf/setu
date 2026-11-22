using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Setu.Core.Server;

/// <summary>Restricts the token file to the current user.</summary>
public interface ITokenFileProtector
{
    void Protect(string path);
}

public sealed class UnixTokenFileProtector : ITokenFileProtector
{
    public void Protect(string path)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("UnixTokenFileProtector cannot protect files on Windows.");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

public sealed class TokenStore(string dataDirectory, ITokenFileProtector protector)
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public string TokenPath { get; } = Path.Combine(dataDirectory, "token");

    public string GetOrCreate()
    {
        if (File.Exists(TokenPath))
        {
            var existing = File.ReadAllText(TokenPath).Trim();
            if (existing.Length > 0)
            {
                protector.Protect(TokenPath); // a file left readable by others is tightened on every start
                return existing;
            }
            // An empty token would let "Bearer " with nothing after it through; replace it.
        }

        Directory.CreateDirectory(dataDirectory);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        WriteNew(TokenPath, token);
        protector.Protect(TokenPath);
        return token;
    }

    private static void WriteNew(string path, string token)
    {
        // An empty token file is replaced; otherwise the file is new. On Unix it is created 0600 so it
        // is never readable by others, even briefly. On Windows the protector applies the ACL afterwards.
        File.Delete(path);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(path, options);
        stream.Write(Utf8NoBom.GetBytes(token));
    }
}
