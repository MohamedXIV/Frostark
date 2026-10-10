# ADR — Save Schema v3 and canonical salvage inventory

**Date:** 2026-10-10. **Status:** Proposed implementation in PR #53, subject to GREEN G0/G1 and exact-head review.

## Single source of truth

`WorldState.Inventory` owns immutable definition revisions, world pickup identities, consumed pickup IDs, pack stacks, ship-hold stacks and both mass/slot capacity budgets. `WorldCommandBus` is the sole source of committed pickup/unload transitions; each accepted command increments `WorldState.Revision`, records its stable ID and emits provenance. A rejected command returns the identical prior `WorldState`. Unity GameObjects must present/authorship-proxy items, not own authoritative stock.

An item definition has a stable `EntityId`, content version, integer weight in grams, and maximum items/stack. Capacity calculations use integer grams and `ceil(quantity / stackLimit)` slots. A pickup is transferred atomically as its full authored quantity to the pack; unload moves an explicit positive quantity into the ship hold. Collected pickup IDs persist even when all stock is unloaded, preventing duplicate scavenging from reappearing world objects or save/load.

## Binary save migration

The canonical `FARKSAVE` little-endian payload previously ended with the version-2 procedural override records. **Version 3 appends** (after v2 data): pack mass cap int64, pack slot cap int32, hold mass cap int64, hold slot cap int32; ordered item definitions (ID, contentVersion int32, grams int64, maxStack int32); ordered world pickups (pickup ID, item ID, quantity int64); ordered collected pickup IDs; pack item stack IDs and int64 quantities; ship-hold item stack IDs and int64 quantities. Each section has a bounded int32 count.

All item IDs and pickup IDs use ordinal case-sensitive stable identity and are sorted for deterministic byte encoding. Decoder verifies checked mass/slot totals, duplicate IDs, positive quantities, and that persisted current stock does not exceed the total from collected authored pickups. A valid SHA-256 checksum does not bypass semantic validation. It is corruption detection, **not authentication**.

Readers accept v1 (no overrides or inventory) and v2 (overrides, no inventory) and migrate both to explicit `InventoryState.Empty`; v3 contains inventory. Never invent collection progress while migrating old saves. New schema fixtures downgrade actual empty-inventory v3 bytes by stripping exactly 44 trailing inventory bytes, and v1 additionally strips 4 empty-override bytes; recompute the envelope checksum and assert re-encoding to v3.

## Unresolved platform work

The pure-C# G1 migration tests are not Unity G2. Visual pickup/transport UX and actual platform `ISaveBlobStore` atomic file writes remain separate Unity/infrastructure deliverables. `WorldSession` PR #52 must be reconciled onto v3 main after this PR merges; it is currently Draft awaiting licensed Unity import/EditMode G2.
