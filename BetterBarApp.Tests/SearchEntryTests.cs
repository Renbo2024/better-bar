using BetterBarApp.Services.Search;
using Xunit;

namespace BetterBarApp.Tests;

public class SearchEntryTests
{
    [Fact]
    public void Create_normalizes_name_and_keywords_and_tokenizes()
    {
        var e = SearchEntry.Create("id1", "Visual Studio Code", "apps", EntryKind.DesktopApp,
            "code.exe", keywords: new[] { "VSCode", "" });

        Assert.Equal("visual studio code", e.NormalizedName);
        Assert.Equal(new[] { "visual", "studio", "code" }, e.Tokens);
        Assert.Equal(new[] { "vscode" }, e.Keywords);   // normalized; blank dropped
        Assert.Equal(EntryKind.DesktopApp, e.Kind);
        Assert.Equal("code.exe", e.LaunchTarget);
    }

    [Fact]
    public void Equality_is_by_id_and_source()
    {
        var a  = SearchEntry.Create("id", "Name A", "apps", EntryKind.DesktopApp, "a");
        var a2 = SearchEntry.Create("id", "Different display", "apps", EntryKind.Document, "b");
        var b  = SearchEntry.Create("id", "Name A", "files", EntryKind.DesktopApp, "a");

        Assert.Equal(a, a2);                              // same id+source
        Assert.Equal(a.GetHashCode(), a2.GetHashCode());
        Assert.NotEqual(a, b);                            // different source
    }
}

public class EntryGlyphTests
{
    [Theory]
    [InlineData(EntryKind.DesktopApp, "▣")]
    [InlineData(EntryKind.SettingModern, "⚙")]
    [InlineData(EntryKind.Document, "📄")]
    [InlineData(EntryKind.Folder, "📁")]
    public void For_returns_expected_glyph(EntryKind kind, string glyph)
        => Assert.Equal(glyph, EntryGlyph.For(kind));
}
