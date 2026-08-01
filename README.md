# Stowage

<img
  width="450"
  alt="Burdened"
  src="https://i.imgur.com/js9t8yI.png"
/>

Stowage is a universal code mod for Vintage Story that changes held-bag
capacity through item-code patterns. The server owns the configuration and
sends the effective bag definitions to every client.

## Features

| Feature | Available since |
|---|---|
| **F01**: Held-bag capacity overrides with safe resizing of existing bags | 0.1.0 |

F01 supports vanilla bags and third-party bags built on Vintage Story's
standard held-bag behavior. Compatibility is detected from that behavior, not
from a hardcoded list of mod ids.

<img
  width="880"
  src="https://i.imgur.com/4tlhxDT.png"
/>

## Configuration

The server creates `ModConfig/stowage.json` when the mod first loads:

```json
{
  "BagCapacityOverrides": {}
}
```

Add item-code patterns and the number of slots each matching bag should have:

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

- Patterns accept `*` wildcards.
- A pattern without a domain is treated as `game:`.
- Rules are checked in file order, and the first match wins.
- Values are constrained to 1 through 256.
- An empty map preserves the capacities supplied by the game and other mods.
- Restart the dedicated server or reopen the singleplayer world after editing
  the file.

## Safe resizing

Existing bags grow automatically when their contents are next loaded. Existing
items keep their slots and the newly added slots start empty.

When a configured capacity becomes smaller, a dormant bag keeps its old layout
until it is used. On equip or open, the server compacts its contents into the
remaining slots, moves overflow into valid player inventory space and other
equipped bags, then drops anything that still does not fit. The smaller layout
is saved only after every overflow item has somewhere to go.

An already-equipped bag is resized when its player finishes joining the world.
A ground-stored or entity-attached bag is resized when opened. Until then, its
old slots remain visible so no contents become hidden.

> NOTE: Back up important worlds before changing any inventory-related mod setup.

## Planned direction

Placed chests, vessels, crates, and other block inventories need separate
adapters because they do not share one capacity contract. F02 tracks that work
in [FEATURES.md](FEATURES.md). Stowage 0.1.0 does not resize those inventories
yet.

## Compatibility

- Built for Vintage Story **1.22.3** and .NET 10.
- Required on both the client and server.
- Standalone, with no required mod dependencies.
- Supports bags using the standard Vintage Story held-bag behavior.
- Custom bag implementations that ignore the standard capacity attribute are
  left unchanged and reported in the server log.

Stowage patches standard bag loading and interaction methods. Other Vintage
Story patch versions should be treated as unverified until their interaction
paths are checked and tested.

## Installation

1. Download the latest `stowage_*.zip` from
   [GitHub Releases](https://github.com/stinowdev/vs-stowage/releases/latest).
2. Place the zip in the Vintage Story `Mods` directory.
3. Restart the game, or restart the server and reconnect.

Install the same Stowage version on the server and every connecting client.
Start the world once to create `stowage.json`, then edit the capacity map and
reopen the world.

## Building

`resources/modinfo.json` is the source of truth for release metadata.

```powershell
dotnet build
./build.ps1
./build.ps1 -Deploy
```

The build script creates `Releases/stowage_<version>.zip`. `-Deploy` also copies
that package into the active Vintage Story `Mods` directory.

## Documentation

- [FEATURES.md](FEATURES.md) tracks implementation status and design decisions.
- [CHANGELOG.md](CHANGELOG.md) records release changes and known limitations.
- [docs/MODDB.html](docs/MODDB.html) is the maintained Mod DB page copy.

## License

See [LICENSE](LICENSE). Personal non-commercial use and pull requests back to
this repository are allowed. Redistribution and modpacks require prior written
permission.

## Support

You can support Stowage and other projects on
[Ko-fi](https://ko-fi.com/stinow).
