using System.Buffers.Text;
using System.Text;
using WindowsMcp.Core.Server;

namespace WindowsMcp.Core.Tests;

public sealed class TokenStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wmcp-token-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void TokenStore_CreatesOnceAndReuses()
    {
        var store = new TokenStore(_dir, new UnixTokenFileProtector());
        var first = store.GetOrCreate();
        var second = new TokenStore(_dir, new UnixTokenFileProtector()).GetOrCreate();

        Assert.Equal(first, second);
        Assert.Equal(32, Base64Url.DecodeFromChars(first).Length);
        var raw = File.ReadAllBytes(Path.Combine(_dir, "token"));
        Assert.Equal(Encoding.UTF8.GetBytes(first), raw); // no BOM, no trailing newline
    }

    [Fact]
    public void TokenStore_ReadsBackTrimmed()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "token"), "abc123\r\n");
        Assert.Equal("abc123", new TokenStore(_dir, new UnixTokenFileProtector()).GetOrCreate());
    }

    [Fact]
    public void TokenStore_EmptyFile_Regenerates()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "token"), "  \n");
        var token = new TokenStore(_dir, new UnixTokenFileProtector()).GetOrCreate();
        Assert.Equal(32, Base64Url.DecodeFromChars(token).Length);
        Assert.Equal(token, File.ReadAllText(Path.Combine(_dir, "token")));
    }

    [Fact]
    public void BearerAuth_EmptyToken_Refused()
    {
        var app = Microsoft.AspNetCore.Builder.WebApplication.CreateSlimBuilder().Build();
        Assert.Throws<ArgumentException>(() => app.UseBearerAuth("", RequestLog.Null));
    }

    [Fact]
    public void TokenStore_FileIsUserOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only");
            return;
        }
        new TokenStore(_dir, new UnixTokenFileProtector()).GetOrCreate();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_dir, "token")));
    }

    [Fact]
    public void TokenStore_ExistingWorldReadableFile_IsReprotected()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Unix file modes only");
            return;
        }
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "token");
        File.WriteAllText(path, "abc123");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        Assert.Equal("abc123", new TokenStore(_dir, new UnixTokenFileProtector()).GetOrCreate());
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void ServerOptions_DefaultPort47631()
    {
        var o = ServerOptions.Load(_dir);
        Assert.Equal(47631, o.Port);
        Assert.Equal(_dir, o.DataDirectory);
    }

    [Fact]
    public void ServerOptions_ConfigOverridesPort()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), """{ "port": 50123 }""");
        Assert.Equal(50123, ServerOptions.Load(_dir).Port);
    }

    [Theory]
    [InlineData("""{ "port": 0 }""")]
    [InlineData("""{ "port": 70000 }""")]
    [InlineData("""{ "port": "abc" }""")]
    [InlineData("""{ "port": 1.5 }""")]
    [InlineData("not json")]
    public void ServerOptions_InvalidConfig_NamesFile(string json)
    {
        Directory.CreateDirectory(_dir);
        var file = Path.Combine(_dir, "config.json");
        File.WriteAllText(file, json);
        var ex = Assert.ThrowsAny<Exception>(() => ServerOptions.Load(_dir));
        Assert.Contains(file, ex.Message);
    }
}
