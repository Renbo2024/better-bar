namespace BetterBarApp.Services.Search;

/// <summary>
/// Tunable scorer weights (spec §8.9). Persisted in app.json so they can be adjusted
/// after testing without recompiling; defaults mirror the spec's starting values.
/// Tier scores set the dominant band; the others reorder within a tier.
/// </summary>
public sealed class SearchWeights
{
    // Match tiers.
    public int Exact            { get; set; } = 100;
    public int Prefix           { get; set; } = 80;
    public int TokenPrefix      { get; set; } = 70;
    public int Acronym          { get; set; } = 65;
    public int Substring        { get; set; } = 50;
    public int SubsequenceMin   { get; set; } = 10;   // floor of the fuzzy tier
    public int SubsequenceRange { get; set; } = 25;   // SubsequenceMin..(Min+Range)

    // Within-tier modifiers.
    public double FrecencyMaxBonus     { get; set; } = 40;
    public double PositionBonusMax     { get; set; } = 10;
    public double LengthPenaltyPerChar { get; set; } = 0.1;
    public double LengthPenaltyMax     { get; set; } = 8;

    // "Popular" section: high-frecency results (across all sources) promoted to a leading section so
    // the most-used matches sit on top and take the default selection. Only shown when some qualify.
    public double PopularMinStrength { get; set; } = 0.4;   // Frecency.Strength threshold (0..1); ~3-4 recent launches
    public int    PopularMaxItems    { get; set; } = 4;     // cap the section size; 0 disables the feature
}
