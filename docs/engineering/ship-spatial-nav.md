# E11 / #110 — canonical ship-layout spatial routing (incremental)

**Design owner:** [Miro D01 ship-local navigation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187), [D02 construction clearance](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03 Smart Interactions](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188). **Engineering:** [#110](https://github.com/MohamedXIV/Frostark/issues/110). This describes implementation, not a duplicate design database.

## What this branch adds

- An immutable derived `ShipSpatialMap` reads actual authoritative `ShipLayoutState.Rooms/Modules/Blueprints`. No copy of placed module state is introduced. A rebuild after a real module placement creates a new projection, without mutating the previous version.
- `ShipModuleSpatialProfile` is an optional per-blueprint **authoring** mask: explicitly solid local cells and separately reserved interaction clearance offsets. An absent profile conservatively blocks the whole module footprint; an authored profile may allow walking through particular footprint cells while **all** footprint cells remain build-reserved.
- Clearance outside the footprint reserves placement but need not block walking. `ModuleClearanceAvailable(id)` reports when an already built neighboring machine occludes a required standing cell. This is detection **not yet enforcement in BuildShipModuleCommand**.
- `ShipRoomPortal` connects two typed room-local cells, with open/closed state. Paths never implicitly jump between rooms. Invalid authoring is rejected; solid door endpoint, closed portal or blocked goal results in an empty path. The A* implementation uses a zero heuristic across portals (optimal under potential authored shortcuts), deterministic neighbor and tie order.
- **8 new Core NUnit tests authored** for footprint/clearance distinctions, open/closed multi-room paths, blocked endpoints, layout rebuild, authoring rejection, inaccessible interaction, and portal order stability.

## Boundaries before declaring #110 complete

- The canonical `ShipLayoutState` currently has no module rotations, deck IDs, authored door/ladder locations or world-space room offsets. This pass does **not** fabricate them or modify save bytes. More precise per-deck coordinates, editor gizmos/assets and persisted door state must be separately authored/validated.
- `WorldCommandBus.BuildShipModuleCommand` now validates **persisted, source-relative `CrewInteractionSlot` standing cells** against the proposed `ShipSpatialMap` *before* consuming materials. This protects existing stations and newly placed stations with authored slots and survives save/restore without a second authoring database. It conservatively treats module footprints as solid until the per-blueprint spatial masks become canonical persisted authoring. Non-module fixture sources are ignored; the current build validator does not yet enforce whole-room/corridor connectivity or authoring-only profile clearances.
- No dynamic fire/cold traversal weights, corridor traffic, continuous ship-local transform movement or route reservations yet. The layout-linked **pure Core** map is not plugged into the fixed 10×8 Galley fixture (#106/PR #107) or the draft theater dispatcher (#109/PR #117).
- G0/G1 exact HEAD can be checked in CI. Actual Unity G2/G3 scene import, passage through visible doors, editor authoring and physical collision proof remain **NOT RUN**.

Next: make the per-blueprint solid/clearance masks and portals canonical authored/persisted content, enforce whole-room reachability as appropriate, then connect the physical ship walker to `ICrewTheaterMoveExecutor` under #109. Keep shore Unity NavMesh untouched.


## 2026-10-21 follow-up — build integrity and Unity-local movement

- Added a SourceId-resolved interaction standing-cell guard to the **authoritative build command**. It runs before payment and command revision/event emission. The guard is deliberately narrow (occupied slot footprints), not a claim that all corridors/stairs stay navigable. `CrewInteractionSlot.LocalX/LocalY` are interpreted as offsets from a placed module origin **only if SourceId resolves to a canonical ShipModule**. Temporary `CrewInteractionClaim` occupancy does not block construction outside the permanent station cell.
- Added six NUnit G1 tests (source collision, accepted build, own slot outside room, unknown fixture source, claimed-slot behavior, save/restore) with real inventory stock/payment checks; **do not conflate a false insufficient-material fixture with spatial rejection**.
- Added `ShipGridWalker` Unity scene-local motion adapter. It takes an already-derived `ShipSpatialMap`, explicit room-local origins, ship transform and verified initial cell; follows local routes under moving ship transforms, rejects bad destinations without cancelling a valid order, and invalidates a route when the door closes. No new persistent actor, game clock or NavMesh. Four **authored** Unity EditMode tests cover motion, root relocation, blocked doors and invalidation; Unity G2/G3 is **not executed** on this branch.
- Not yet delivered: real scene bootstrap/saved position reentry, authored room origins/gizmos and door prefab link, live actor selection integration, 4-crew corridor traffic, rotation/decks, hazard replan, or authoritative blueprint spatial-mask persistence. PR #117 remains a separate Draft; do not import `ICrewTheaterMoveExecutor` into #110 before the dependent branches are integrated.
