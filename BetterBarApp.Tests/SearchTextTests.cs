using BetterBarApp.Services.Search;
using Xunit;

namespace BetterBarApp.Tests;

public class SearchTextTests
{
    [Theory]
    [InlineData("  Notepad  ", "notepad")]
    [InlineData("CALCULATOR", "calculator")]
    [InlineData("Café", "cafe")]              // diacritics stripped
    [InlineData("naïve", "naive")]
    public void Normalize_lowercases_trims_and_strips_diacritics(string input, string expected)
        => Assert.Equal(expected, SearchText.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_blank_yields_empty(string? input)
        => Assert.Equal(string.Empty, SearchText.Normalize(input));

    [Fact]
    public void Tokenize_splits_on_separators_and_letter_digit_boundaries()
    {
        Assert.Equal(new[] { "win", "32", "app" }, SearchText.Tokenize("win32 app"));
        Assert.Equal(new[] { "visual", "studio", "code" }, SearchText.Tokenize("visual-studio.code"));
        Assert.Equal(new[] { "7", "zip" }, SearchText.Tokenize("7zip"));
    }

    [Fact]
    public void Tokenize_empty_yields_no_tokens()
        => Assert.Empty(SearchText.Tokenize(""));
}
