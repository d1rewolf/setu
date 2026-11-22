using Setu.Core.Files;

namespace Setu.Core.Tests;

public class PathResolverTests
{
    [Fact]
    public void Resolve_ExpandsEnvVar()
    {
        var dir = Path.GetFullPath(Path.GetTempPath());
        var old = Environment.GetEnvironmentVariable("WMCP_T");
        Environment.SetEnvironmentVariable("WMCP_T", dir);
        try
        {
            var result = PathResolver.Resolve("%WMCP_T%/a.txt");
            Assert.EndsWith("a.txt", result);
            Assert.StartsWith(dir.TrimEnd('/', '\\'), result);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WMCP_T", old);
        }
    }

    [Fact]
    public void Resolve_Relative_Throws()
    {
        Assert.Throws<ToolInputException>(() => PathResolver.Resolve("a.txt"));
    }
}
