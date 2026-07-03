using BetterBarApp.Services;
using Xunit;

namespace BetterBarApp.Tests;

public class PathUtilTests
{
    [Theory]
    [InlineData(@"C:\Windows\System32")]     // local drive — not a network mapping
    [InlineData(@"\\server\share\folder")]   // already UNC
    [InlineData(@"relative\path")]           // no drive root
    [InlineData("")]                         // empty
    public void ToUnc_returns_non_mapped_paths_unchanged(string path)
        => Assert.Equal(path, PathUtil.ToUnc(path));
}
