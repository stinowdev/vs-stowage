using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Stowage.Bags;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace Stowage.Patches;

/// <summary>
/// NOTE/TODO: Server-side interaction adapters for F01/D03. Passive bag reconstruction
/// has no authenticated player or safe drop position, so it cannot perform a reduction.
/// </summary>
internal static class BagMigrationPatches
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> ReportedFailures = new(StringComparer.Ordinal);
    private static ICoreServerAPI? sapi;

    public static void Apply(Harmony harmony, ICoreServerAPI api)
    {
        MethodInfo? playerBagModified = AccessTools.Method(
            typeof(InventoryPlayerBackpacks),
            nameof(InventoryPlayerBackpacks.OnItemSlotModified),
            new[] { typeof(ItemSlot) });

        MethodInfo? containedBagOpened = AccessTools.Method(
            typeof(CollectibleBehaviorGroundStoredHeldBag),
            nameof(CollectibleBehaviorGroundStoredHeldBag.OnContainedInteractStart),
            new[]
            {
                typeof(BlockEntityContainer),
                typeof(ItemSlot),
                typeof(IPlayer),
                typeof(BlockSelection)
            });

        MethodInfo? attachedBagPacket = AccessTools.Method(
            typeof(CollectibleBehaviorHeldBag),
            nameof(CollectibleBehaviorHeldBag.OnReceivedClientPacket),
            new[]
            {
                typeof(ItemSlot),
                typeof(int),
                typeof(Entity),
                typeof(IServerPlayer),
                typeof(int),
                typeof(byte[]),
                typeof(EnumHandling).MakeByRefType(),
                typeof(Action)
            });

        int patched = 0;
        patched += PatchSupport.TryPatch(
            harmony,
            api.Logger,
            playerBagModified,
            prefix: new HarmonyMethod(typeof(BagMigrationPatches), nameof(PlayerBagModifiedPrefix)));
        patched += PatchSupport.TryPatch(
            harmony,
            api.Logger,
            containedBagOpened,
            prefix: new HarmonyMethod(typeof(BagMigrationPatches), nameof(ContainedBagOpenedPrefix)));
        patched += PatchSupport.TryPatch(
            harmony,
            api.Logger,
            attachedBagPacket,
            prefix: new HarmonyMethod(typeof(BagMigrationPatches), nameof(AttachedBagPacketPrefix)));

        lock (Gate)
        {
            if (patched > 0)
            {
                sapi = api;
            }
        }

        api.Logger.Notification(
            "[{0}] bag reduction patches applied to {1} method(s).",
            StowageModSystem.ModId,
            patched);
    }

    private static void PlayerBagModifiedPrefix(
        InventoryPlayerBackpacks __instance,
        ItemSlot slot)
    {
        ICoreServerAPI? api = ServerApi();
        if (api == null
            || slot is ItemSlotBagContent
            || __instance.Player is not IServerPlayer player)
        {
            return;
        }

        int bagIndex = Array.IndexOf(__instance.bagSlots, slot);
        if (bagIndex < 0) return;

        TryReconcile(
            api,
            slot,
            player,
            player.Entity.Pos.XYZ.Add(0, 0.5, 0),
            bagIndex,
            markSlotDirty: false,
            onRequireSave: null);
    }

    private static void ContainedBagOpenedPrefix(
        BlockEntityContainer be,
        ItemSlot slot,
        IPlayer byPlayer,
        BlockSelection blockSel)
    {
        ICoreServerAPI? api = ServerApi();
        if (api == null
            || byPlayer is not IServerPlayer player
            || !player.Entity.Controls.CtrlKey
            || be.GetBehavior<BEBehaviorContainedBagInventory>() == null)
        {
            return;
        }

        Vec3d dropPosition = blockSel?.Position?.ToVec3d().Add(0.5, 0.5, 0.5)
            ?? player.Entity.Pos.XYZ.Add(0, 0.5, 0);

        TryReconcile(
            api,
            slot,
            player,
            dropPosition,
            sourceBagIndex: -1,
            markSlotDirty: true,
            onRequireSave: () => be.MarkDirty(true));
    }

    private static void AttachedBagPacketPrefix(
        ItemSlot bagSlot,
        int slotIndex,
        Entity onEntity,
        IServerPlayer player,
        int packetid,
        Action onRequireSave)
    {
        ICoreServerAPI? api = ServerApi();

        // CollectibleBehaviorHeldBag.OnReceivedClientPacket uses the upper bits
        // for the attached slot index and packet 1001 for the open request. Slot
        // clicks use lower packet ids and must not trigger migration (D04).
        int targetSlotIndex = packetid >> 11;
        int workspacePacketId = packetid & 2047;
        if (api == null || targetSlotIndex != slotIndex || workspacePacketId != 1001)
        {
            return;
        }

        TryReconcile(
            api,
            bagSlot,
            player,
            onEntity.Pos.XYZ.Add(0, 0.5, 0),
            sourceBagIndex: -1,
            markSlotDirty: true,
            onRequireSave);
    }

    private static void TryReconcile(
        ICoreServerAPI api,
        ItemSlot bagSlot,
        IServerPlayer player,
        Vec3d dropPosition,
        int sourceBagIndex,
        bool markSlotDirty,
        Action? onRequireSave)
    {
        try
        {
            BagCapacityMigrationResult result = BagCapacityMigrationService.Reconcile(
                bagSlot,
                player,
                dropPosition,
                sourceBagIndex);
            if (!result.Changed) return;

            if (markSlotDirty)
            {
                bagSlot.MarkDirty();
            }

            onRequireSave?.Invoke();
            NotifyDroppedItems(player, result.DroppedItems);
        }
        catch (Exception e)
        {
            ReportOnce(api, bagSlot.Itemstack, e);
        }
    }

    public static void NotifyDroppedItems(IServerPlayer player, int droppedItems)
    {
        if (droppedItems <= 0) return;

        player.SendMessage(
            GlobalConstants.GeneralChatGroup,
            Vintagestory.API.Config.Lang.Get(
                "stowage:bag-overflow-dropped",
                droppedItems),
            EnumChatType.Notification);
    }

    private static ICoreServerAPI? ServerApi()
    {
        lock (Gate) return sapi;
    }

    private static void ReportOnce(ICoreServerAPI api, ItemStack? bagStack, Exception exception)
    {
        string code = bagStack?.Collectible?.Code?.ToShortString() ?? "unknown";
        bool shouldReport;
        lock (Gate)
        {
            shouldReport = ReportedFailures.Add(code);
        }

        if (shouldReport)
        {
            api.Logger.Warning(
                "[{0}] Could not safely reduce {1}: {2}. Its remaining slots were preserved.",
                StowageModSystem.ModId,
                code,
                exception.Message);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            sapi = null;
            ReportedFailures.Clear();
        }
    }
}
