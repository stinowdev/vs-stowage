using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Stowage.Config;

/// <summary>
/// Loaded only by the server during asset finalization (F01/D01/D05). Clients
/// receive the resulting collectible attributes through vanilla asset sync.
/// </summary>
public sealed class StowageConfig
{
    public const int MinCapacity = 1;
    public const int MaxCapacity = 256;

    // F01: item-code pattern to requested capacity.
    public Dictionary<string, int>? BagCapacityOverrides { get; set; } = new();

    private readonly List<CapacityOverrideRule> parsedOverrides = new();
    private readonly List<string> rejectedOverrides = new();
    private readonly List<string> adjustedOverrides = new();

    [JsonIgnore]
    internal IReadOnlyList<CapacityOverrideRule> ParsedOverrides => parsedOverrides;

    [JsonIgnore]
    public IReadOnlyList<string> RejectedOverrides => rejectedOverrides;

    [JsonIgnore]
    public IReadOnlyList<string> AdjustedOverrides => adjustedOverrides;

    /// <summary>
    /// Normalizes hand-edited JSON once, preserving declaration order because
    /// D09 makes the first matching wildcard authoritative.
    /// </summary>
    public void Sanitize()
    {
        parsedOverrides.Clear();
        rejectedOverrides.Clear();
        adjustedOverrides.Clear();

        BagCapacityOverrides ??= new Dictionary<string, int>();
        Dictionary<string, int> sanitized = new();

        foreach (KeyValuePair<string, int> entry in BagCapacityOverrides)
        {
            string source = entry.Key?.Trim().ToLowerInvariant() ?? string.Empty;
            if (source.Length == 0)
            {
                rejectedOverrides.Add("an entry with a blank item code");
                continue;
            }

            AssetLocation? pattern;
            try
            {
                pattern = AssetLocation.Create(source);
            }
            catch (Exception)
            {
                pattern = null;
            }

            if (pattern == null)
            {
                rejectedOverrides.Add($"\"{entry.Key}\" is not a usable item-code pattern");
                continue;
            }

            int slots = GameMath.Clamp(entry.Value, MinCapacity, MaxCapacity);
            if (slots != entry.Value)
            {
                adjustedOverrides.Add(
                    $"\"{entry.Key}\" was clamped from {entry.Value} to {slots}");
            }

            if (!sanitized.TryAdd(source, slots))
            {
                rejectedOverrides.Add($"\"{entry.Key}\" duplicates an earlier normalized pattern");
                continue;
            }

            parsedOverrides.Add(new CapacityOverrideRule(source, pattern, slots));
        }

        BagCapacityOverrides = sanitized;
    }
}
