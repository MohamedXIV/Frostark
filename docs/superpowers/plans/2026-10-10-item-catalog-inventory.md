# R1 I01 — Versioned inventory/catalog integrated with canonical WorldState

Date: 2026-10-10. Issue #23. Dependency #17 closed, #18 save schema already merged.

**Outcome:** Pick up a stable-ID salvage object, transport in a constrained pack, unload into the persistent ship hold exactly once, save/reload without duplication. Do not create a throwaway inventory manager detached from the canonical WorldState.

## Boundaries and decisions
- Immutable versioned ItemDefinition (EntityId, contentVersion, unitWeightGrams, maxStackSize); immutable ItemCatalog with ordinal stable IDs and rejected duplicates; salvage world pickups have unique stable pickup IDs and item definition IDs.
- InventoryState is immutable and embedded into WorldState snapshots, not a Unity component or independent singleton. Two fixed early containers are Pack and ShipHold; world pickups are third source. A collected pickup persists as consumed to prevent replay even after save.
- Slot usage is per ceil(quantity / maxStack) for each item definition; mass equals quantity * unitWeightGrams using checked arithmetic. Both max slots and max grams must be satisfied before committing a transfer.
- New IGameCommand variants are PickUpSalvageCommand and UnloadToShipCommand, validated by WorldCommandBus. Reject invalid input, stale revision, duplicate command ID, insufficient pack stock/capacity, already-collected pickup without partial mutation.
- Version save codec from v2 to v3: append versioned inventory snapshot with content definition revisions, pickups (including collected states), capacity and pack/ship quantities. Migrate previous v1/v2 snapshots to empty inventory without fabricating items. Add byte-for-byte golden reproducibility and backwards compatibility tests. Document new schema in ADR.
- G1 .NET tests + G0 structural tests in public read-only GitHub Actions. Unity G2, in-scene pickup rendering and physical transfer animations remain later integration, NOT RUN.

## Implementation sequence / ledger
1. [ ] TDD RED: tests for catalog ID/version/weight/stack validation and deterministic lookup and slot math. GREEN: add Core/Inventory catalog types and .meta.
2. [ ] TDD RED/GREEN: immutable InventoryState, pickups, ownership, weight/slot validation and transfer errors.
3. [ ] TDD RED/GREEN: extend canonical WorldState and WorldCommandBus, command replay/atomicity/provenance.
4. [ ] TDD RED/GREEN: save codec v3 append, v1/v2 fixture migration, corrupt inventory semantic validation; verify restore and resumed pickups exactly once.
5. [ ] Review full branch, exact-head CI test totals, merge only when issue acceptance is actually met; document G2 pending and authored item presentation still deferred.

**No dependency on #19 Unity Bridge:** this branch changes Inventory, WorldState, Commands, SaveCodec only. #19 PR lives in Core/Application and Unity adapters; preserve its exact-head state, do not merge it as Unity-verified without G2.
