using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stowage.Bags;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Stowage.Patches;

/// <summary>
/// F01/D06 adapter for persisted standard-bag slot trees. Asset finalization
/// changes new-bag capacity but cannot reach slots already stored on an item
/// stack, so no registered behavior or event can preserve D04.
/// Applied on both sides.
/// </summary>
internal static class BagCapacityPatches
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);
    private static bool applied;
    private static bool applying;
    private static ILogger? logger;

    public static void Apply(Harmony harmony, ILogger targetLogger)
    {
        lock (Gate)
        {
            if (applied || applying) return;
            applying = true;
        }

        MethodInfo? getOrCreateSlots = AccessTools.Method(
            typeof(CollectibleBehaviorHeldBag),
            nameof(CollectibleBehaviorHeldBag.GetOrCreateSlots),
            new[] { typeof(ItemStack), typeof(InventoryBase), typeof(int), typeof(IWorldAccessor) });

        MethodInfo? getQuantitySlots = AccessTools.Method(
            typeof(CollectibleBehaviorHeldBag),
            nameof(CollectibleBehaviorHeldBag.GetQuantitySlots),
            new[] { typeof(ItemStack) });

        int patched = 0;
        patched += PatchSupport.TryPatch(
            harmony,
            targetLogger,
            getOrCreateSlots,
            prefix: new HarmonyMethod(typeof(BagCapacityPatches), nameof(EnsureCapacityPrefix)));
        patched += PatchSupport.TryPatch(
            harmony,
            targetLogger,
            getQuantitySlots,
            postfix: new HarmonyMethod(typeof(BagCapacityPatches), nameof(EffectiveCapacityPostfix)));

        lock (Gate)
        {
            if (patched > 0)
            {
                logger = targetLogger;
                applied = true;
            }

            applying = false;
        }

        targetLogger.Notification(
            "[{0}] held-bag capacity patches applied to {1} method(s).",
            StowageModSystem.ModId,
            patched);
    }

    private static void EnsureCapacityPrefix(ItemStack bagstack)
    {
        if (!IsActive()) return;

        try
        {
            BagSlotReconciler.EnsureCapacity(bagstack);
        }
        catch (Exception e)
        {
            ReportOnce(bagstack, e);
        }
    }

    private static void EffectiveCapacityPostfix(ItemStack bagstack, ref int __result)
    {
        if (!IsActive()) return;

        try
        {
            __result = BagSlotReconciler.EffectiveCapacity(bagstack, __result);
        }
        catch (Exception e)
        {
            ReportOnce(bagstack, e);
        }
    }

    private static bool IsActive()
    {
        lock (Gate) return applied && logger != null;
    }

    private static void ReportOnce(ItemStack? bagStack, Exception exception)
    {
        string code = bagStack?.Collectible?.Code?.ToShortString() ?? "unknown";
        ILogger? targetLogger;
        bool shouldReport;

        lock (Gate)
        {
            targetLogger = logger;
            shouldReport = targetLogger != null && ReportedFailures.Add(code);
        }

        if (shouldReport)
        {
            targetLogger?.Warning(
                "[{0}] Could not reconcile persisted slots for {1}: {2}. The bag was left unchanged.",
                StowageModSystem.ModId,
                code,
                exception.Message);
        }
    }

    /// <summary>Re-arms the process-global patch gate for the next world load.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            applied = false;
            applying = false;
            logger = null;
            ReportedFailures.Clear();
        }
    }
}
