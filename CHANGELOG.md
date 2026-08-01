# Changelog

All notable changes to this project will be documented in this file.

Feature (F) and decision (D) numbers refer to [FEATURES.md](FEATURES.md).

## [v0.1.0]

### Added

- F01 adds JSON capacity overrides for standard held bags, matched by item-code
  pattern.
- F01 / D01 / D05 use server-owned configuration and vanilla server-asset
  synchronization.
- F01 / D03 safely reduces existing bags when they become active: retained
  slots are compacted first, overflow moves into valid player storage, and any
  remainder drops nearby.
- F01 / D04 keeps dormant oversized bags readable until a player equips or
  opens them, so no contents become hidden during migration.
- F01 reports matched rules, changed bags, configured reductions, and
  unsupported custom implementations at startup.

### Known limitations

- F02 remains planned. Version 0.1.0 does not change placed chests, vessels,
  crates, or custom block-entity inventories.
