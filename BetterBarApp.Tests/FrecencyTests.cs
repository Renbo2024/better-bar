using System.IO;
using BetterBarApp.Services.Search;
using Xunit;

namespace BetterBarApp.Tests;

public class FrecencyTests : IDisposable
{
    private readonly Guid _owner = Guid.NewGuid();
    private string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BetterBar", $"frecency_{_owner:N}.json");

    private static SearchEntry Entry(string id = "n", string src = "apps") =>
        SearchEntry.Create(id, "Notepad", src, EntryKind.DesktopApp, "notepad.exe");

    [Fact]
    public void New_store_has_no_bonus_or_context()
    {
        var f = new Frecency(_owner, _ => true, 40.0);
        Assert.Equal(0, f.BonusFor(Entry()));
        Assert.Equal(0, f.ContextCount("notepad", Entry()));
    }

    [Fact]
    public void RecordLaunch_increases_bonus_and_records_query_context()
    {
        var f = new Frecency(_owner, _ => true, 40.0);
        f.RecordLaunch(Entry(), "note");

        Assert.True(f.BonusFor(Entry()) > 0);
        Assert.True(f.ContextCount("note", Entry()) >= 1);
        Assert.Equal(0, f.ContextCount("other", Entry()));   // a different query has no context
    }

    [Fact]
    public void Disabled_source_records_nothing_and_scores_zero()
    {
        var f = new Frecency(_owner, _ => false, 40.0);
        f.RecordLaunch(Entry(), "note");

        Assert.Equal(0, f.BonusFor(Entry()));
        Assert.Equal(0, f.ContextCount("note", Entry()));
    }

    public void Dispose()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { }
    }
}
