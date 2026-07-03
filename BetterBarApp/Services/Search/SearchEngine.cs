namespace BetterBarApp.Services.Search;

/// <summary>
/// Scores entries against a query and groups the top results into sections.
/// Single-word queries use the six match tiers (spec §8.3). Multi-word queries also
/// match when ALL typed words appear (token-prefix or substring, in any order), so
/// "remove add programs" still finds "Add or remove programs" without curated aliases.
/// Tier dominates ranking; frecency / position / length only reorder within a tier,
/// and contextual frecency pins a repeatedly-chosen result to the very top (§8.6).
/// </summary>
public sealed class SearchEngine
{
    private readonly Frecency      _frecency;
    private readonly SearchWeights _w;

    public SearchEngine(Frecency frecency, SearchWeights weights)
    {
        _frecency = frecency;
        _w        = weights;
    }

    // Lifts an alias-expansion match above any literal-alias match (but below a contextually-pinned
    // result, whose bonus is 1,000,000+). Larger than the whole native band (max tier*1000 + within).
    private const long AliasBand = 500_000L;

    public IReadOnlyList<SearchSection> Search(
        string rawQuery,
        IndexSnapshot snapshot,
        IReadOnlyCollection<string> enabledSourceIds,
        int maxPerSection,
        int maxGlobal,
        IReadOnlyDictionary<string, string>? aliases = null)
    {
        var q = SearchText.Normalize(rawQuery);
        if (q.Length == 0) return [];
        bool oneChar  = q.Length == 1;
        var qTokens   = SearchText.Tokenize(q);

        // When the whole typed query equals an alias, ALSO score entries against its expansion
        // ("ps" → "powershell"). Expansion matches are flagged so they outrank literal-alias matches.
        var expansions = BuildExpansions(q, aliases);

        var scored = new List<(SearchEntry Entry, long Final)>();
        foreach (var e in snapshot.Entries)
        {
            if (!enabledSourceIds.Contains(e.SourceId)) continue;

            var (tier, matchStart) = ScoreOne(e, q, qTokens, oneChar);   // literal/native match
            bool isAlias = false;
            foreach (var ex in expansions)
            {
                var (et, es) = ScoreOne(e, ex.Norm, ex.Tokens, ex.OneChar);
                if (et < 0) continue;
                // Any expansion match beats a native-only match (alias band, applied below). Among
                // alias matches, the stronger expansion tier wins.
                if (!isAlias || et > tier) { tier = et; matchStart = es; isAlias = true; }
            }
            if (tier < 0) continue;

            double within = _frecency.BonusFor(e)
                          + PositionBonus(matchStart, e.NormalizedName.Length)
                          - LengthPenalty(e.DisplayName.Length);
            long final = tier * 1000L + (long)Math.Round(within * 4);
            if (isAlias) final += AliasBand;

            int ctx = _frecency.ContextCount(q, e);
            if (ctx > 0) final += 1_000_000L * Math.Min(ctx, 100);

            scored.Add((e, final));
        }

        scored.Sort((a, b) =>
        {
            int c = b.Final.CompareTo(a.Final);
            if (c != 0) return c;
            c = a.Entry.DisplayName.Length.CompareTo(b.Entry.DisplayName.Length);
            if (c != 0) return c;
            return string.Compare(a.Entry.DisplayName, b.Entry.DisplayName, StringComparison.OrdinalIgnoreCase);
        });

        var perSection = new Dictionary<string, List<SearchEntry>>();
        int total = 0;
        foreach (var (entry, _) in scored)
        {
            if (total >= maxGlobal) break;
            if (!perSection.TryGetValue(entry.SourceId, out var bucket))
                perSection[entry.SourceId] = bucket = [];
            if (bucket.Count >= maxPerSection) continue;
            bucket.Add(entry);
            total++;
        }

        // "Popular": promote the highest-frecency results (across ALL sources) into a leading section
        // so the most-used matches sit on top and one of them takes the default selection. Drawn from
        // results already chosen for display, in scored order (best match+frecency first). The entries
        // ALSO remain in their own source sections (shown in both places). Shown only when some qualify.
        var popular = new List<SearchEntry>();
        if (_w.PopularMaxItems > 0)
        {
            foreach (var (entry, _) in scored)
            {
                if (popular.Count >= _w.PopularMaxItems) break;
                if (!perSection.TryGetValue(entry.SourceId, out var bucket) || !bucket.Contains(entry)) continue;
                if (_frecency.Strength(entry) < _w.PopularMinStrength) continue;
                popular.Add(entry);
            }
        }

        var sections = new List<SearchSection>();
        if (popular.Count > 0)
            sections.Add(new SearchSection("popular", "Popular", popular));
        foreach (var src in snapshot.Sources)
            if (perSection.TryGetValue(src.SourceId, out var items) && items.Count > 0)
                sections.Add(new SearchSection(src.SourceId, src.DisplayName, items));
        return sections;
    }

    // Expansions to ALSO search for the given (normalized) query: currently the whole query matching
    // an alias key exactly. Each carries its tokens + one-char flag so scoring matches the native path.
    private static List<(string Norm, string[] Tokens, bool OneChar)> BuildExpansions(
        string normalizedQuery, IReadOnlyDictionary<string, string>? aliases)
    {
        var list = new List<(string, string[], bool)>();
        if (aliases != null && aliases.TryGetValue(normalizedQuery, out var raw))
        {
            var en = SearchText.Normalize(raw);
            if (en.Length > 0 && en != normalizedQuery)
                list.Add((en, SearchText.Tokenize(en), en.Length == 1));
        }
        return list;
    }

    // Tier (+ match start) of an entry against ONE normalized query, folding in the multi-word rule.
    private (int Tier, int MatchStart) ScoreOne(SearchEntry e, string query, string[] qTokens, bool oneChar)
    {
        int tier = MatchTier(e, query, oneChar, out int matchStart);
        if (qTokens.Length > 1)
        {
            int multi = AllWordsTier(e, qTokens);
            if (multi > tier) { tier = multi; matchStart = 0; }
        }
        return (tier, matchStart);
    }

    // Highest single-query tier the entry qualifies for, or -1.
    private int MatchTier(SearchEntry e, string q, bool oneChar, out int matchStart)
    {
        matchStart = 0;
        int best = -1;

        foreach (var name in Names(e))
        {
            if (name == q) { matchStart = 0; return _w.Exact; }
            if (name.StartsWith(q, StringComparison.Ordinal)) best = Math.Max(best, _w.Prefix);
        }

        foreach (var t in e.Tokens)
            if (t.StartsWith(q, StringComparison.Ordinal)) { best = Math.Max(best, _w.TokenPrefix); break; }

        if (best < _w.TokenPrefix && AcronymMatch(e.Tokens, q)) best = Math.Max(best, _w.Acronym);

        if (best >= _w.Prefix) { matchStart = 0; return best; }
        if (oneChar) return best;   // 1-char safety: no substring/subsequence (spec §8.7)

        if (best < 0)
        {
            foreach (var name in Names(e))
            {
                int idx = name.IndexOf(q, StringComparison.Ordinal);
                if (idx > 0) { matchStart = idx; return _w.Substring; }
            }
            int sub = Subsequence(e.NormalizedName, q);
            if (sub > 0) { matchStart = 0; return sub; }
        }
        return best;
    }

    // Multi-word: every query token must appear. All as word-prefixes → TokenPrefix
    // tier; otherwise all as substrings → Substring tier; else no match.
    private int AllWordsTier(SearchEntry e, string[] qTokens)
    {
        bool allPrefix = true, allSubstring = true;
        foreach (var qt in qTokens)
        {
            if (!WordPrefix(e, qt))  allPrefix = false;
            if (!Contains(e, qt))    allSubstring = false;
            if (!allSubstring) return -1;   // a missing word disqualifies the entry
        }
        return allPrefix ? _w.TokenPrefix : _w.Substring;
    }

    private static bool WordPrefix(SearchEntry e, string qt)
    {
        foreach (var t in e.Tokens)
            if (t.StartsWith(qt, StringComparison.Ordinal)) return true;
        foreach (var k in e.Keywords)
            foreach (var w in k.Split(' '))
                if (w.StartsWith(qt, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Contains(SearchEntry e, string qt)
    {
        if (e.NormalizedName.Contains(qt, StringComparison.Ordinal)) return true;
        foreach (var k in e.Keywords)
            if (k.Contains(qt, StringComparison.Ordinal)) return true;
        return false;
    }

    private static IEnumerable<string> Names(SearchEntry e)
    {
        yield return e.NormalizedName;
        foreach (var k in e.Keywords) yield return k;
    }

    private static bool AcronymMatch(string[] tokens, string q)
    {
        if (tokens.Length < q.Length || q.Length < 2) return false;
        for (int s = 0; s + q.Length <= tokens.Length; s++)
        {
            bool ok = true;
            for (int k = 0; k < q.Length; k++)
                if (tokens[s + k].Length == 0 || tokens[s + k][0] != q[k]) { ok = false; break; }
            if (ok) return true;
        }
        return false;
    }

    private int Subsequence(string name, string q)
    {
        int qi = 0, run = 0, score = 0;
        for (int i = 0; i < name.Length && qi < q.Length; i++)
        {
            if (name[i] == q[qi]) { qi++; run++; score += run; }
            else run = 0;
        }
        if (qi < q.Length) return 0;
        double max = q.Length * (q.Length + 1) / 2.0;
        return _w.SubsequenceMin + (int)Math.Round(_w.SubsequenceRange * Math.Min(1.0, score / max));
    }

    private double PositionBonus(int matchStart, int nameLength) =>
        nameLength <= 0 ? 0 : _w.PositionBonusMax * (1.0 - (double)matchStart / nameLength);

    private double LengthPenalty(int nameLength) =>
        Math.Min(_w.LengthPenaltyMax, nameLength * _w.LengthPenaltyPerChar);
}
