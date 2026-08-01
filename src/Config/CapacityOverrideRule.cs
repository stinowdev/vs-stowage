using Vintagestory.API.Common;

namespace Stowage.Config;

internal record CapacityOverrideRule(string Source, AssetLocation Pattern, int Slots);
