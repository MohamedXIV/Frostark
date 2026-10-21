# E11 #112 — Accepted ship crew Move persistence & reentry (first slice)

**Status:** implementation on stacked Draft [PR #120](https://github.com/MohamedXIV/Frostark/pull/120) over Draft #119. **G0/G1 source CI PASS, G2/G3 Unity on this child branch NOT RUN**. Do not merge to main.

## Already verified parent baseline

Local Unity 6000.6.0f1 QA on parent `feat/112-crew-agency-arbitration` exact SHA `d9d70d3ee61fb760b38746f29b1314cc4bae4a8c` (user-provided report): **73/73 EditMode**, **26/26 PlayMode**, **6/6 targeted ShipCrewAgency EditMode**, clean compilation; standalone Addressables smoke 1/1 twice. Root causes of previous failures: auto-fire timing assertion and frozen EditMode Addressables load delay, fixed without gameplay changes. GitHub CI on that parent SHA separately passed 25/25 G0 + 303/303 G1.

## Implemented on this child branch

- **Canonical session-level player intent:** `WorldSession.AcceptShipCrewMove`, `GetPendingCrewMove`, `PendingCrewMoves`, and `CompleteCrewMove` store exactly one accepted ship-local destination per stable `EntityId`. No Unity selection, path cache, world/local transform, work-slot claim, or traffic reservation persists. Current `WorldSession` is the only authority, independent of presentation frame clock.
- **Only physically accepted Move intents persist:** `ShipCrewAgencyCoordinator.OnOrderIssued` listens to actual accepted per-actor dispatch, then records that actor's `ShipGridCell`. Unaccepted members cannot create saved orders; subsequent accepted Move replaces earlier target. Rejected Move keeps the old route and goal intact.
- **V18 SaveCodec extension:** a typed, bounded, deterministic `PendingCrewMove` list is persisted in `SaveGameDto`. The extension is **omitted when empty**, retaining existing payload suffix for worlds with no such orders; decoder handles absent extension and strict nonempty entries. Existing V1–V17 migration tests remain green. Stable crew/scene/room IDs and available ship crew are validated; payload never contains a cached Unity scene object.
- **Reentry:** on ship-scene bootstrap after crew binding, the agency rebuilds each accepted ship route from **the authored, validated safe spawn cell**, checks currently walkable goal and portal endpoints, and lets the normal `ShipGridWalker`/traffic coordinator traverse it. Successful arrival clears the saved order; emergency safe-cell handover or invalid route can cancel it. Changing the scene ID or theater invalidates obsolete ship-local physical orders. The saved intermediate location is **not** restored: this is target continuity rather than exact-mid-edge positional resume.
- **Tests:** three new Core G1 tests for binary Save/Restore with two crew, per-actor replacement and scene switch, unknown crew/room rejection; one newly authored Unity G2 test simulates reentry from a real typed saved order, no persisted mouse selection and no duplicate world revision.

## Verified vs pending evidence

- **Source CI on code commit `20c09515ecc95da1a8cb1b181abd2419ecac2174`**: [workflow 39693414008](https://github.com/MohamedXIV/Frostark/actions/runs/39693414008) **25/25 G0 and 306/306 G1 PASS**, including existing legacy save fixtures. Branch commits after this may add Unity G2 test/docs only; check exact HEAD CI again.
- **New Unity G2/G3 still NOT RUN** on this code. Parent 73/73 and 26/26 cannot validate new source. Need Unity 6000.6.0f1 clean compile + all relevant G2/G3 tests and scene manual roundtrip before any acceptance.
- **Remaining #112:** no guaranteed exact safe grid position checkpoint mid-route (reload intentionally restarts at authored safe spawn); no shore NavMesh pending order persistence; physical authored Work/Rest/shift assignments and offscreen outcome tracking; partial batch retarget/queued mid-edge override; emergency/slot transit expiry handling; actual G4 input, HUD buttons, platform builds and real on-stage provision with restored stocks.

**Constraint:** Do not treat a GameObject/scene path or mid-animation coordinate as canonical simulation truth. Do not add a second WorldClock or old-save compatibility blocker.
