using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Stowage.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Stowage.Bags;

/// <summary>
/// Applies F01 to loaded collectible definitions during server asset
/// finalization. Capability checks replace hardcoded mod ids,
/// and every rejected path leaves the collectible untouched (finally).
/// </summary>
internal static class BagCapacityService
{
    public static BagCapacityApplicationResult Apply(
        ICoreAPI api,
        StowageConfig config)
    {
        IReadOnlyList<CapacityOverrideRule> rules = config.ParsedOverrides;
        HashSet<int> matchedRuleIndexes = new();
        int matchedCollectibles = 0;
        int changedCollectibles = 0;
        int reducedCollectibles = 0;
        int unsupportedCollectibles = 0;

        foreach (CollectibleObject collectible in api.World.Collectibles)
        {
            if (collectible?.Code == null) continue;

            IHeldBag? heldBag = collectible.GetCollectibleInterface<IHeldBag>();
            if (heldBag == null
                || !TryFindRule(rules, collectible.Code, out CapacityOverrideRule? rule, out int ruleIndex))
            {
                continue;
            }

            if (rule == null) continue;

            matchedRuleIndexes.Add(ruleIndex);
            matchedCollectibles++;

            CollectibleBehaviorHeldBag? standardBehavior =
                collectible.GetBehavior<CollectibleBehaviorHeldBag>();
            if (standardBehavior == null)
            {
                unsupportedCollectibles++;
                api.Logger.Warning(
                    "[{0}] Pattern {1} matched {2}, but it does not use the standard held-bag behavior. Left unchanged.",
                    StowageModSystem.ModId,
                    rule.Source,
                    collectible.Code);
                continue;
            }

            ItemStack probeStack = new(collectible);
            int declaredCapacity;
            try
            {
                declaredCapacity = heldBag.GetQuantitySlots(probeStack);
            }
            catch (Exception e)
            {
                unsupportedCollectibles++;
                api.Logger.Warning(
                    "[{0}] Could not read held-bag capacity for {1}: {2}. Left unchanged.",
                    StowageModSystem.ModId,
                    collectible.Code,
                    e.Message);
                continue;
            }

            if (rule.Slots == declaredCapacity) continue;

            if (collectible.Attributes?.Token is not JObject root
                || root["backpack"] is not JObject backpack)
            {
                unsupportedCollectibles++;
                api.Logger.Warning(
                    "[{0}] {1} has no standard backpack.quantitySlots attribute. Left unchanged.",
                    StowageModSystem.ModId,
                    collectible.Code);
                continue;
            }

            JsonObject originalAttributes = collectible.Attributes.Clone();
            backpack["quantitySlots"] = rule.Slots;

            int effectiveCapacity;
            try
            {
                effectiveCapacity = heldBag.GetQuantitySlots(probeStack);
            }
            catch (Exception e)
            {
                collectible.Attributes = originalAttributes;
                unsupportedCollectibles++;
                api.Logger.Warning(
                    "[{0}] {1} rejected its configured capacity: {2}. Restored its original attributes.",
                    StowageModSystem.ModId,
                    collectible.Code,
                    e.Message);
                continue;
            }

            if (effectiveCapacity != rule.Slots)
            {
                collectible.Attributes = originalAttributes;
                unsupportedCollectibles++;
                api.Logger.Warning(
                    "[{0}] {1} uses custom capacity logic (requested {2}, reported {3}). Restored its original attributes.",
                    StowageModSystem.ModId,
                    collectible.Code,
                    rule.Slots,
                    effectiveCapacity);
                continue;
            }

            changedCollectibles++;
            if (rule.Slots < declaredCapacity)
            {
                reducedCollectibles++;
            }
        }

        for (int i = 0; i < rules.Count; i++)
        {
            if (!matchedRuleIndexes.Contains(i))
            {
                api.Logger.Warning(
                    "[{0}] BagCapacityOverrides pattern {1} matched no loaded collectible.",
                    StowageModSystem.ModId,
                    rules[i].Source);
            }
        }

        return new BagCapacityApplicationResult(
            rules.Count,
            matchedRuleIndexes.Count,
            matchedCollectibles,
            changedCollectibles,
            reducedCollectibles,
            unsupportedCollectibles);
    }

    private static bool TryFindRule(
        IReadOnlyList<CapacityOverrideRule> rules,
        AssetLocation code,
        out CapacityOverrideRule? rule,
        out int ruleIndex)
    {
        for (int i = 0; i < rules.Count; i++)
        {
            if (!WildcardUtil.Match(rules[i].Pattern, code)) continue;

            rule = rules[i];
            ruleIndex = i;
            return true;
        }

        rule = null;
        ruleIndex = -1;
        return false;
    }

}
