using System;
using HarmonyLib;
using Stowage.Bags;
using Stowage.Config;
using Stowage.Patches;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace Stowage;

public sealed class StowageModSystem : ModSystem
{
    public const string ModId = "stowage";
    private const string ConfigFile = $"{ModId}.json";

    private Harmony? harmony;
    private ICoreServerAPI? sapi;
    private StowageConfig config = new();
    private BagCapacityApplicationResult applicationResult = BagCapacityApplicationResult.Empty;

    // -------------- Shared --------------

    public override void Start(ICoreAPI api)
    {
        base.Start(api);

        harmony = new Harmony(ModId);
        BagCapacityPatches.Apply(harmony, api.Logger);
    }

    /// <summary>
    /// Server-owned asset finalization for F01/D05.
    /// ItemTypeNet serializer sends the mutated collectible attributes to the
    /// client after this stage. No custom network contract is required.
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        base.AssetsFinalize(api);
        if (api.Side != EnumAppSide.Server) return;

        try
        {
            config = LoadOrCreateConfig(api);
            applicationResult = BagCapacityService.Apply(api, config);
        }
        catch (Exception e)
        {
            applicationResult = BagCapacityApplicationResult.Empty;
            api.Logger.Error(
                "[{0}] Failed to apply bag capacity overrides. Vanilla capacities remain active. Error: {1}",
                ModId,
                e.Message);
        }
    }

    // -------------- Client --------------

    public override void StartClientSide(ICoreClientAPI api)
    {
        base.StartClientSide(api);
        api.Logger.Notification("[{0}] client side loaded.", ModId);
    }

    // -------------- Server --------------

    public override void StartServerSide(ICoreServerAPI api)
    {
        base.StartServerSide(api);

        sapi = api;
        if (harmony != null)
        {
            BagMigrationPatches.Apply(harmony, api);
        }

        api.Event.PlayerNowPlaying += OnPlayerNowPlaying;

        api.Logger.Notification(
            "[{0}] server side loaded. Config: rules={1}, matchedRules={2}, matchedCollectibles={3}, changed={4}, reductions={5}, unsupported={6}.",
            ModId,
            applicationResult.ConfiguredRules,
            applicationResult.MatchedRules,
            applicationResult.MatchedCollectibles,
            applicationResult.ChangedCollectibles,
            applicationResult.ReducedCollectibles,
            applicationResult.UnsupportedCollectibles);
    }

    // PlayerNowPlaying is the first join event where the entity and inventory
    // are ready. Equipped bags are active storage, so D04 reconciles them here
    // instead of waiting for a contextless reconstruction callback.
    private void OnPlayerNowPlaying(IServerPlayer player)
    {
        if (sapi == null
            || player.InventoryManager.GetOwnInventory(GlobalConstants.backpackInvClassName)
                is not InventoryPlayerBackpacks backpacks)
        {
            return;
        }

        int droppedItems = 0;
        bool changed = false;

        for (int bagIndex = 0; bagIndex < backpacks.bagSlots.Length; bagIndex++)
        {
            ItemSlot bagSlot = backpacks.bagSlots[bagIndex];
            try
            {
                BagCapacityMigrationResult result = BagCapacityMigrationService.Reconcile(
                    bagSlot,
                    player,
                    player.Entity.Pos.XYZ.Add(0, 0.5, 0),
                    bagIndex);
                if (!result.Changed) continue;

                changed = true;
                droppedItems += result.DroppedItems;
                bagSlot.MarkDirty();
            }
            catch (Exception e)
            {
                sapi.Logger.Warning(
                    "[{0}] Could not safely reduce equipped bag {1} for {2}: {3}. Its remaining slots were preserved.",
                    ModId,
                    bagSlot.Itemstack?.Collectible?.Code?.ToShortString() ?? "unknown",
                    player.PlayerName,
                    e.Message);
            }
        }

        if (changed)
        {
            backpacks.bagInv.ReloadBagInventory(backpacks, backpacks.bagSlots);
            player.BroadcastPlayerData();
            BagMigrationPatches.NotifyDroppedItems(player, droppedItems);
        }
    }

    private static StowageConfig LoadOrCreateConfig(ICoreAPI api)
    {
        StowageConfig? loaded = null;
        try
        {
            loaded = api.LoadModConfig<StowageConfig>(ConfigFile);
        }
        catch (Exception e)
        {
            api.Logger.Error(
                "[{0}] Failed to parse {1}, using defaults. Error: {2}",
                ModId,
                ConfigFile,
                e.Message);
        }

        StowageConfig effective = loaded ?? new StowageConfig();
        effective.Sanitize();

        foreach (string rejected in effective.RejectedOverrides)
        {
            api.Logger.Warning("[{0}] Ignoring BagCapacityOverrides entry: {1}.", ModId, rejected);
        }

        foreach (string adjusted in effective.AdjustedOverrides)
        {
            api.Logger.Warning("[{0}] Adjusted BagCapacityOverrides entry: {1}.", ModId, adjusted);
        }

        api.StoreModConfig(effective, ConfigFile);
        return effective;
    }

    // -------------- Cleanup --------------

    public override void Dispose()
    {
        if (sapi != null)
        {
            sapi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
            sapi = null;
        }

        if (harmony != null)
        {
            harmony.UnpatchAll(ModId);
            harmony = null;
        }

        BagCapacityPatches.Reset();
        BagMigrationPatches.Reset();
        config = new StowageConfig();
        applicationResult = BagCapacityApplicationResult.Empty;

        base.Dispose();
    }
}
