namespace Stowage.Bags;

/// <summary>Startup log record for F01, logged once after asset finalization.</summary>
internal sealed record BagCapacityApplicationResult(
    int ConfiguredRules,
    int MatchedRules,
    int MatchedCollectibles,
    int ChangedCollectibles,
    int ReducedCollectibles,
    int UnsupportedCollectibles)
{
    public static BagCapacityApplicationResult Empty { get; } = new(0, 0, 0, 0, 0, 0);
}
