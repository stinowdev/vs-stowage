using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Stowage.Bags;

/// <summary>
/// Applies a configured capacity reduction while an authenticated player and a
/// safe world position are available (F01/D03/D04).
/// </summary>
internal static class BagCapacityMigrationService
{
    public static BagCapacityMigrationResult Reconcile(
        ItemSlot bagSlot,
        IServerPlayer player,
        Vec3d dropPosition,
        int sourceBagIndex = -1)
    {
        ItemStack? bagStack = bagSlot.Itemstack;
        if (bagStack == null
            || bagStack.Collectible.GetBehavior<CollectibleBehaviorHeldBag>() is not { } heldBag)
        {
            return BagCapacityMigrationResult.Unchanged;
        }

        int targetCapacity = BagSlotReconciler.DeclaredCapacity(bagStack);
        int persistedCapacity = BagSlotReconciler.PersistedCapacity(bagStack);
        if (targetCapacity <= 0 || persistedCapacity <= targetCapacity)
        {
            return BagCapacityMigrationResult.Unchanged;
        }

        // D04: the universal read patches preserve every persisted slot until
        // this server-owned interaction has somewhere safe to route overflow.
        BagSlotReconciler.EnsureCapacity(bagStack);

        // The behavior creates detached ItemSlotBagContent wrappers. They must
        // belong to an inventory that actually contains those same instances,
        // otherwise ItemSlot.TryPutInto rejects their dirty notification.
        InventoryGeneric workspace = new(
            persistedCapacity,
            "stowage-migration",
            Guid.NewGuid().ToString("N"),
            player.Entity.Api);
        List<ItemSlotBagContent> slots = heldBag.GetOrCreateSlots(
            bagStack,
            workspace,
            bagIndex: 0,
            player.Entity.World);
        for (int slotIndex = 0; slotIndex < slots.Count; slotIndex++)
        {
            workspace[slotIndex] = slots[slotIndex];
        }

        // Persist each successful engine transfer immediately. This mirrors
        // BagInventory.SaveSlotIntoBag and keeps the source tree aligned.
        workspace.SlotModified += slotIndex =>
            heldBag.Store(bagStack, (ItemSlotBagContent)workspace[slotIndex]);

        int movedItems = 0;
        int droppedItems = 0;

        try
        {
            for (int slotIndex = targetCapacity; slotIndex < slots.Count; slotIndex++)
            {
                ItemSlotBagContent? source = slots[slotIndex];
                if (source?.Empty != false) continue;

                movedItems += CompactIntoRetainedSlots(
                    source,
                    slots,
                    targetCapacity,
                    player.Entity.World);

                if (!source.Empty)
                {
                    movedItems += MoveIntoPlayerInventory(
                        source,
                        player,
                        bagStack,
                        sourceBagIndex);
                }

                if (!source.Empty)
                {
                    ItemStack sourceStack = source.Itemstack!;
                    ItemStack dropStack = sourceStack.Clone();

                    // Clear and persist the source before spawning the clone.
                    // If spawning fails, restore the original stack and slot.
                    source.Itemstack = null;
                    heldBag.Store(bagStack, source);
                    try
                    {
                        if (player.Entity.World.SpawnItemEntity(dropStack, dropPosition) == null)
                        {
                            throw new InvalidOperationException("The world rejected an overflow item drop.");
                        }
                    }
                    catch
                    {
                        source.Itemstack = sourceStack;
                        heldBag.Store(bagStack, source);
                        throw;
                    }

                    droppedItems += sourceStack.StackSize;
                }
            }

            StoreAll(heldBag, bagStack, slots);
            BagSlotReconciler.CompleteShrink(bagStack, targetCapacity);
        }
        catch
        {
            // Persist any transfers that completed before the failure, but keep
            // every remaining overflow slot. Partial progress does not duplicate
            // an item, and a later interaction can safely continue (D03).
            StoreAll(heldBag, bagStack, slots);
            throw;
        }

        return new BagCapacityMigrationResult(true, movedItems, droppedItems);
    }

    private static int CompactIntoRetainedSlots(
        ItemSlotBagContent source,
        IReadOnlyList<ItemSlotBagContent> slots,
        int targetCapacity,
        IWorldAccessor world)
    {
        int moved = 0;

        // Merge first so empty retained slots remain available for distinct
        // stacks. Stable slot order makes repeated migrations predictable.
        moved += MoveIntoRange(source, slots, targetCapacity, world, emptyTargets: false);
        if (!source.Empty)
        {
            moved += MoveIntoRange(source, slots, targetCapacity, world, emptyTargets: true);
        }

        return moved;
    }

    private static int MoveIntoRange(
        ItemSlot source,
        IReadOnlyList<ItemSlotBagContent> slots,
        int targetCapacity,
        IWorldAccessor world,
        bool emptyTargets)
    {
        int moved = 0;
        for (int slotIndex = 0; slotIndex < targetCapacity && !source.Empty; slotIndex++)
        {
            ItemSlotBagContent? target = slots[slotIndex];
            if (target == null || target.Empty != emptyTargets) continue;

            moved += source.TryPutInto(world, target, source.StackSize);
        }

        return moved;
    }

    private static int MoveIntoPlayerInventory(
        ItemSlot source,
        IServerPlayer player,
        ItemStack sourceBag,
        int sourceBagIndex)
    {
        int before = source.StackSize;
        List<ItemSlot> skipSlots = BuildSkipSlots(player, sourceBag, sourceBagIndex);
        int attempts = 0;

        while (!source.Empty && attempts++ < 5000)
        {
            ItemStackMoveOperation op = new(
                player.Entity.World,
                EnumMouseButton.Left,
                (EnumModifierKey)0,
                EnumMergePriority.AutoMerge,
                source.StackSize);

            ItemSlot? target = player.InventoryManager.GetBestSuitedSlot(
                source,
                onlyPlayerInventory: true,
                op,
                skipSlots);
            if (target == null) break;

            skipSlots.Add(target);
            source.TryPutInto(target, ref op);
        }

        return before - source.StackSize;
    }

    /// <summary>
    /// D10: overflow cannot return to the source bag or enter an invalid slot
    /// of another equipped bag that is also waiting for a reduction.
    /// </summary>
    private static List<ItemSlot> BuildSkipSlots(
        IServerPlayer player,
        ItemStack sourceBag,
        int sourceBagIndex)
    {
        List<ItemSlot> skipSlots = new();
        if (player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)
            is not InventoryPlayerBackpacks backpacks)
        {
            return skipSlots;
        }

        foreach (ItemSlot slot in backpacks.bagInv)
        {
            if (slot is not ItemSlotBagContent content
                || content.BagIndex < 0
                || content.BagIndex >= backpacks.bagSlots.Length)
            {
                continue;
            }

            ItemStack? ownerBag = backpacks.bagSlots[content.BagIndex].Itemstack;
            bool belongsToSource = content.BagIndex == sourceBagIndex
                || ReferenceEquals(ownerBag, sourceBag);
            int ownerTarget = ownerBag == null
                ? 0
                : BagSlotReconciler.DeclaredCapacity(ownerBag);

            if (belongsToSource || content.SlotIndex >= ownerTarget)
            {
                skipSlots.Add(content);
            }
        }

        return skipSlots;
    }

    private static void StoreAll(
        CollectibleBehaviorHeldBag heldBag,
        ItemStack bagStack,
        IEnumerable<ItemSlotBagContent> slots)
    {
        foreach (ItemSlotBagContent? slot in slots)
        {
            if (slot != null)
            {
                heldBag.Store(bagStack, slot);
            }
        }
    }
}
