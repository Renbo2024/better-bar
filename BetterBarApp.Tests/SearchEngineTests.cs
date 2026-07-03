using BetterBarApp.Services.Search;
using Xunit;

namespace BetterBarApp.Tests;

public class SearchEngineTests
{
    private sealed class FakeSource : ISearchSource
    {
        public string SourceId { get; }
        public string DisplayName { get; }
        public FakeSource(string id, string name) { SourceId = id; DisplayName = name; }
        public IReadOnlyList<string> WatchRoots => [];
        public Task<IReadOnlyList<SearchEntry>> EnumerateAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<SearchEntry>>([]);
    }

    private static SearchEngine NewEngine() =>
        new(new Frecency(Guid.NewGuid(), _ => true, 40.0), new SearchWeights());

    private static IndexSnapshot Snapshot(params (string id, string name)[] apps)
    {
        var source = new FakeSource("apps", "Apps");
        var entries = apps
            .Select(a => SearchEntry.Create(a.id, a.name, "apps", EntryKind.DesktopApp, a.name + ".exe"))
            .ToList();
        return new IndexSnapshot(entries, [source], 1);
    }

    private static readonly string[] Enabled = ["apps"];

    private static List<string> Names(IReadOnlyList<SearchSection> sections) =>
        sections.SelectMany(s => s.Items).Select(i => i.DisplayName).ToList();

    [Fact]
    public void Exact_match_ranks_first()
    {
        var snap = Snapshot(("1", "Notepad"), ("2", "Notion"), ("3", "Note Taker"));
        var result = NewEngine().Search("notepad", snap, Enabled, 10, 50);
        Assert.Equal("Notepad", Names(result).First());
    }

    [Fact]
    public void Prefix_query_returns_all_prefix_matches()
    {
        var snap = Snapshot(("1", "Notepad"), ("2", "Notion"), ("3", "Calculator"));
        var names = Names(NewEngine().Search("no", snap, Enabled, 10, 50));
        Assert.Contains("Notepad", names);
        Assert.Contains("Notion", names);
        Assert.DoesNotContain("Calculator", names);
    }

    [Fact]
    public void Acronym_query_matches_token_initials()
    {
        var snap = Snapshot(("1", "Visual Studio Code"), ("2", "Notepad"));
        var names = Names(NewEngine().Search("vsc", snap, Enabled, 10, 50));
        Assert.Contains("Visual Studio Code", names);
    }

    [Fact]
    public void No_match_returns_no_sections()
    {
        var snap = Snapshot(("1", "Notepad"), ("2", "Calculator"));
        Assert.Empty(NewEngine().Search("zzzzzz", snap, Enabled, 10, 50));
    }

    [Fact]
    public void Empty_query_returns_no_sections()
    {
        var snap = Snapshot(("1", "Notepad"));
        Assert.Empty(NewEngine().Search("   ", snap, Enabled, 10, 50));
    }

    [Fact]
    public void Disabled_source_is_excluded()
    {
        var snap = Snapshot(("1", "Notepad"));
        Assert.Empty(NewEngine().Search("notepad", snap, new[] { "other" }, 10, 50));
    }

    [Fact]
    public void MaxGlobal_caps_total_results()
    {
        var snap = Snapshot(("1", "Test One"), ("2", "Test Two"), ("3", "Test Three"));
        var result = NewEngine().Search("test", snap, Enabled, 10, 2);
        Assert.Equal(2, Names(result).Count);
    }

    [Fact]
    public void High_frecency_match_is_promoted_to_a_leading_Popular_section()
    {
        var frecency = new Frecency(Guid.NewGuid(), _ => true, 40.0);
        var engine   = new SearchEngine(frecency, new SearchWeights());
        var snap     = Snapshot(("1", "Notepad"), ("2", "Notion"), ("3", "Note Taker"));

        // Make "Notion" popular via repeated (non-contextual) launches.
        var notion = snap.Entries.First(e => e.DisplayName == "Notion");
        for (int i = 0; i < 6; i++) frecency.RecordLaunch(notion, queryContext: null);

        var result = engine.Search("no", snap, Enabled, 10, 50);

        // The Popular section is first, so its first item is the default selection.
        Assert.Equal("Popular", result[0].DisplayName);
        Assert.Equal("popular", result[0].SourceId);
        Assert.Equal("Notion", result[0].Items[0].DisplayName);

        // Shown in BOTH places: also still listed in its own Apps section.
        var apps = result.First(s => s.SourceId == "apps");
        Assert.Contains(apps.Items, i => i.DisplayName == "Notion");
    }

    [Fact]
    public void No_Popular_section_when_nothing_is_high_frecency()
    {
        var snap   = Snapshot(("1", "Notepad"), ("2", "Notion"));
        var result = NewEngine().Search("no", snap, Enabled, 10, 50);
        Assert.DoesNotContain(result, s => s.SourceId == "popular");
    }

    [Fact]
    public void Alias_expansion_outranks_literal_alias_matches()
    {
        // "Paint Shop" matches the literal "ps" as an acronym (a strong native tier). With the alias
        // ps → powershell, PowerShell (an exact expansion match) should jump above it.
        var snap    = Snapshot(("1", "Paint Shop"), ("2", "PowerShell"));
        var aliases = new Dictionary<string, string> { ["ps"] = "powershell" };

        var withAlias = Names(NewEngine().Search("ps", snap, Enabled, 10, 50, aliases));
        Assert.Equal("PowerShell", withAlias.First());      // expansion match promoted to the top
        Assert.Contains("Paint Shop", withAlias);           // literal match still present, below

        // Without the alias, the literal acronym match leads.
        var noAlias = Names(NewEngine().Search("ps", snap, Enabled, 10, 50));
        Assert.Equal("Paint Shop", noAlias.First());
    }
}
