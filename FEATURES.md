# Stowage design

This file records what Stowage promises and the choices that should not be
accidentally undone later. Player instructions live in [README.md](README.md),
release history lives in [CHANGELOG.md](CHANGELOG.md), and version-sensitive
patches are listed in [docs/PATCHES.md](docs/PATCHES.md).

## Feature status

| ID | Status | Feature | Config key | Default | Authority |
|---|---|---|---|---|---|
| F01 | Implemented | Set the capacity of standard held bags by item-code pattern, with safe migration when an existing bag gets smaller | `BagCapacityOverrides` | `{}` | The server owns the setting and every inventory change |
| F02 | Planned | Support placed containers through storage-specific adapters | - | - | The server will own capacity and inventory changes |

Planned features have no runtime settings.

## Held-bag capacity

Stowage works from bag capability and item code, not from a list of mod names.
Vanilla bags and bags from other mods are eligible when they use Vintage Story's
standard `CollectibleBehaviorHeldBag` behavior. A custom inventory that only
looks like a bag is left alone.

The server reads `stowage.json` when the world opens. Each entry pairs an
item-code pattern with the number of slots that bag should have. Patterns accept
`*` wildcards. A pattern without a domain is treated as `game:`, and the first
matching entry wins.

```json
{
  "BagCapacityOverrides": {
    "game:backpack-*": 12,
    "game:linensack": 10,
    "game:basket-*": 8,
    "othermod:rucksack-*": 18
  }
}
```

An empty map keeps the capacities supplied by the game and other mods. Values
are constrained to 1 through 256.

New bags use the configured size immediately. Existing bags grow the next time
their contents are loaded. If an existing bag needs to become smaller, its old
slots stay visible while the bag is dormant. The server migrates it when the bag
becomes active:

1. Contents are merged and compacted into the slots that remain.
2. Overflow moves into valid player inventory space and other equipped bags.
3. Anything that still does not fit is dropped near the player or the opened bag.
4. Only then is the smaller slot layout saved.

Equipping a bag, joining with one already equipped, opening a ground-stored bag,
or opening a bag attached to an entity can trigger that migration. Items are
never intentionally deleted.

## Locked decisions

| ID | State | Decision |
|---|---|---|
| D01 | Active | Stowage is required on the server and every client. The server reads `stowage.json`; clients receive the resulting bag attributes through normal game synchronization. |
| D02 | Active | Compatibility is based on the standard held-bag behavior, not a hardcoded mod list. A custom `IHeldBag` implementation that ignores `backpack.quantitySlots` is reported and left unchanged. |
| D03 | Active | Capacity reductions are applied when an oversized bag becomes active or is explicitly accessed. Stowage compacts retained contents, routes overflow into valid player storage while excluding the source bag, drops any remainder, and only then saves the smaller capacity. Dormant bags keep their existing layout until that reconciliation can happen. |
| D04 | Active | A dormant bag must continue reporting its persisted size. Showing only the new size before migration would hide items even if the data still existed. Equipped bags migrate on equip or at `PlayerNowPlaying`; ground-stored and entity-attached bags migrate when opened. |
| D05 | Active | Bag definitions are changed during `AssetsFinalize`, before the server sends collectible attributes to clients. This keeps configuration server-owned without adding a custom network protocol. |
| D06 | Active | `GetOrCreateSlots` and `GetQuantitySlots` remain small universal patches. Their job is to keep every persisted slot readable until server migration finishes, because the asset layer cannot repair an existing item stack. |
| D07 | Active | Passive slot reads only touch the current item stack. Actual migration runs from verified server main-thread inventory and interaction paths. |
| D08 | Planned | F02 will use separate adapters for generic containers, typed containers, crates, and supported custom block entities. Placed storage does not share one safe capacity contract. |
| D09 | Active | Rules are checked in the order written in JSON, and the first match wins. This allows a specific rule to appear before a broad fallback. |
| D10 | Active | Overflow cannot be routed back into the bag being reduced. Slots beyond the pending capacity of other equipped bags are excluded too, so one migration cannot create new overflow in another bag. |
| D11 | Active | If migration cannot finish, completed transfers are saved and every remaining overflow slot is kept. The bag can continue on a later interaction rather than risking item loss. |

## Configuration contract

The server loads, cleans, and stores `stowage.json` during asset finalization.
Clients do not read a local copy. Restart the dedicated server or reopen the
singleplayer world after editing the file.
