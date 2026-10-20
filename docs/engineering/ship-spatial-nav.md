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
- Build placement still uses existing `ShipLayoutState.CanPlace` and does **not** yet validate interaction clearance. A further domain-command integration step must enforce clearances without regressing existing module placements/old saves.
- No dynamic fire/cold traversal weights, corridor traffic, continuous ship-local transform movement or route reservations yet. The layout-linked **pure Core** map is not plugged into the fixed 10×8 Galley fixture (#106/PR #107) or the draft theater dispatcher (#109/PR #117).
- G0/G1 exact HEAD can be checked in CI. Actual Unity G2/G3 scene import, passage through visible doors, editor authoring and physical collision proof remain **NOT RUN**.

Next: integrate authored profile/portal data and clearance enforcement into the real build command path, then feed this map into a ship `ICrewTheaterMoveExecutor` under #109; keep shore Unity NavMesh untouched.
