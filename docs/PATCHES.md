# Harmony patch inventory

Stowage changes declared bag capacity through the asset/property layer.
Harmony is limited to existing item-stack data and the server interactions that
provide a player and safe drop position. Targets were verified against game
assets decompilation.

| Target | Kind | Side | Feature | Thread | Reason |
|---|---|---|---|---|---|
| `CollectibleBehaviorHeldBag.GetOrCreateSlots` | Prefix | Universal | F01 | Main thread in installed call sites | Keep every persisted slot readable until a server migration can safely finish |
| `CollectibleBehaviorHeldBag.GetQuantitySlots` | Postfix | Universal | F01 | Main thread in installed call sites | Report dormant persisted capacity so occupied slots are not hidden |
| `InventoryPlayerBackpacks.OnItemSlotModified` | Prefix | Server | F01 | Server main thread | Resize a bag after it enters an equip slot and before vanilla reloads its contents |
| `CollectibleBehaviorGroundStoredHeldBag.OnContainedInteractStart` | Prefix | Server | F01 | Server main thread | Resize a ground-stored bag before its inventory opens, with the interacting player and block position available |
| `CollectibleBehaviorHeldBag.OnReceivedClientPacket` | Prefix | Server | F01 | Server main thread | Resize an entity-attached bag on the authenticated open packet before its workspace loads |

The migration patches are server-authoritative and preserve remaining overflow slots 
if a reduction cannot finish. A missing or failed target disables only that interaction 
path and is reported in the log.
