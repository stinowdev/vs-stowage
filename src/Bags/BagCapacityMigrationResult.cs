namespace Stowage.Bags;

/// <summary>Result of one completed F01/D03 bag reduction.</summary>
internal sealed record BagCapacityMigrationResult(
    bool Changed,
    int MovedItems,
    int DroppedItems)
{
    public static BagCapacityMigrationResult Unchanged { get; } = new(false, 0, 0);
}
