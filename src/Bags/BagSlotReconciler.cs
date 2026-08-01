using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace Stowage.Bags;

/// <summary>
/// Keeps a dormant standard bag's persisted slot tree readable until a
/// context-rich server interaction can migrate a configured reduction
/// (F01/D03/D04). Passive reads never hide or remove persisted contents.
/// </summary>
internal static class BagSlotReconciler
{
    private const string BackpackKey = "backpack";
    private const string SlotsKey = "slots";
    private const string SlotPrefix = "slot-";

    public static void EnsureCapacity(ItemStack bagStack)
    {
        int declaredCapacity = DeclaredCapacity(bagStack);
        if (declaredCapacity <= 0) return;

        ITreeAttribute? backpackTree = bagStack.Attributes.GetTreeAttribute(BackpackKey);
        if (backpackTree == null)
        {
            // Creates a complete tree for a new bag.
            return;
        }

        ITreeAttribute? slotsTree = backpackTree.GetTreeAttribute(SlotsKey);
        if (slotsTree == null)
        {
            slotsTree = new TreeAttribute();
            backpackTree[SlotsKey] = slotsTree;
        }

        int targetCapacity = Math.Max(declaredCapacity, PersistedCapacity(slotsTree));
        for (int slotIndex = 0; slotIndex < targetCapacity; slotIndex++)
        {
            string key = SlotPrefix + slotIndex;
            if (!slotsTree.HasAttribute(key))
            {
                slotsTree[key] = new ItemstackAttribute(null);
            }
        }
    }

    /// <summary>
    /// Dormant higher slots remain visible until server migration completes
    /// (D04). Returning that size keeps their contents reachable and tooltips
    /// honest.
    /// </summary>
    public static int EffectiveCapacity(ItemStack bagStack, int declaredCapacity)
    {
        ITreeAttribute? slotsTree = bagStack.Attributes
            .GetTreeAttribute(BackpackKey)?
            .GetTreeAttribute(SlotsKey);

        return slotsTree == null
            ? declaredCapacity
            : Math.Max(declaredCapacity, PersistedCapacity(slotsTree));
    }

    public static int DeclaredCapacity(ItemStack bagStack)
    {
        return bagStack.ItemAttributes?[BackpackKey]["quantitySlots"].AsInt() ?? 0;
    }

    public static int PersistedCapacity(ItemStack bagStack)
    {
        ITreeAttribute? slotsTree = bagStack.Attributes
            .GetTreeAttribute(BackpackKey)?
            .GetTreeAttribute(SlotsKey);

        return slotsTree == null ? 0 : PersistedCapacity(slotsTree);
    }

    /// <summary>
    /// Removes only empty overflow entries after D03 has routed every item out
    /// of them.
    /// </summary>
    public static void CompleteShrink(ItemStack bagStack, int targetCapacity)
    {
        ITreeAttribute? slotsTree = bagStack.Attributes
            .GetTreeAttribute(BackpackKey)?
            .GetTreeAttribute(SlotsKey);
        if (slotsTree == null) return;

        List<string> overflowKeys = new();
        foreach (KeyValuePair<string, IAttribute> entry in slotsTree)
        {
            if (TryGetSlotIndex(entry.Key, out int slotIndex) && slotIndex >= targetCapacity)
            {
                overflowKeys.Add(entry.Key);
            }
        }

        foreach (string key in overflowKeys)
        {
            slotsTree.RemoveAttribute(key);
        }

        for (int slotIndex = 0; slotIndex < targetCapacity; slotIndex++)
        {
            string key = SlotPrefix + slotIndex;
            if (!slotsTree.HasAttribute(key))
            {
                slotsTree[key] = new ItemstackAttribute(null);
            }
        }
    }

    private static int PersistedCapacity(ITreeAttribute slotsTree)
    {
        int capacity = 0;
        foreach (KeyValuePair<string, IAttribute> entry in slotsTree)
        {
            if (!TryGetSlotIndex(entry.Key, out int slotIndex))
            {
                continue;
            }

            capacity = Math.Max(capacity, slotIndex + 1);
        }

        return capacity;
    }

    private static bool TryGetSlotIndex(string key, out int slotIndex)
    {
        slotIndex = -1;
        return key.StartsWith(SlotPrefix, StringComparison.Ordinal)
            && int.TryParse(key.AsSpan(SlotPrefix.Length), out slotIndex)
            && slotIndex >= 0;
    }
}
