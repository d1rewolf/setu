using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace WindowsMcp.Core.Server;

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
            if (existing.Length > 0) return existing;
            // An empty token would let "Bearer " with nothing after it through; replace it.
        }

        Directory.CreateDirectory(dataDirectory);
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(TokenPath, token, Utf8NoBom);
        protector.Protect(TokenPath);
        return token;
    }
}
