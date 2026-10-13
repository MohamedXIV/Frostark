# R1 ship ↔ shore persistent crew transfer — Core-first handoff (#22)

**Status:** Pure-C# crew transfer **and v13 persistent physical scene checkpoint** implemented. **Unity scene switching, retained session lifetime, actual physical spawn and G2/G3 are not yet implemented or validated.**

**Source of truth:** `WorldSession` / `WorldState.CrewDuty` / `SetCrewAwayCommand` and existing inventory, thermal, repairs, navigation, clock and SaveCodec. The application cannot infer a character's location from the Unity object instance, and must never clone the crew record when instantiating a proxy.

## Pure-C# contract delivered

`FrostArk.Core.Application.ShoreCrewTransfer.Transfer(WorldSession session, IEnumerable<EntityId> actorIds, bool toShore, EntityId transitionId)`:

- Freezes an explicit nonempty group, rejects invalid/duplicate stable IDs, sorts actors by ordinal ID for replay stability.
- **Preflights the whole roster** against the existing world crew registry and authored CrewDuty. An unknown crew member, missing roster entry or member already in the requested `IsAway` state rejects the whole group **before any command** (no invented character, no revision, no time advance).
- Applies one existing revisioned `SetCrewAwayCommand` for each actor in canonical order with a command ID namespaced by the caller-supplied **unique** transfer ID + stable actor ID. Departure sets `CrewDuty.IsAway=true`; return sets it false, leaving each persistent identity unchanged.
- Returns `ShoreCrewTransferResult.Success`, `Failure`, `ActorIds` and the ordered read-only `CommandResults` for precise feedback. An unchanged/invalid manifest does not silently report a successful scene transition.
- This is **not** an atomic group world command. The normal preflight prevents invalid mixed rosters from partially committing, but an unexpected subscriber/reentrant revision change between per-actor commits could produce partial progress. The Unity caller **must inspect the result and keep the current scene on any failure**, then reconcile persisted `IsAway` state explicitly rather than reload/duplicate crew. Future all-or-nothing multi-crew departures require a deliberate one-revision batch domain command, not silent rollback.
- Does not assert path availability, unload inventory, spend travel time automatically, create a second scheduler, store scene GameObjects or persist pointer selection.

Example application boundary:

```csharp
var sent = ShoreCrewTransfer.Transfer(session, selectedCrewIds, toShore: true,
    transitionId: EntityId.Parse("transition:landing:unique-event-id"));
if (!sent.Success)
{
    // Show per-actor outcomes if partially applied; remain in the current scene.
    return;
}
// Unity: load the shore scene, resolve spawn proxies by EntityId, do not recreate WorldSession.
```

Returning uses a **different** transition ID and `toShore:false`. `WorldSession.Advance(minutes)` remains the only time authority while a shore excursion occurs: ship crew, fuel/heat, workshops, repairs and route systems still run normally; absent crew never staff a ship duty for that minute. Ground salvage uses `CollectSalvageCommand`; unloading at the vessel uses `UnloadToShipCommand`. These already survive SaveCodec.

## Save while docked / ashore — accurate state contract

**Verified at G1:** SaveCodec preserves `CrewDuty.IsAway` and crew identities, pending scheduler state, ship fuel/repair progress, and inventory/pickup collected status across a save **made while a crew member is away**. Resuming after loading and returning crew + unloading loot preserves item counts and prevents duplicate pickup. Continuous vs save/resumed canonical state and save bytes match for the tested sequence.

**v13 persisted re-entry metadata (Core / G1):** `PhysicalSceneCheckpoint` can now hold a typed `Ship` or `Shore` theater, stable `SceneId`, stable `SpawnAnchorId`, and for shore a `SiteId` plus a sorted, immutable set of persistent expedition crew IDs. `WorldSession.SetSceneCheckpoint(...)` checks shore party members against its canonical WorldState (`HasCrew`, `CrewDuty.IsAway`) and retains metadata through `Capture` and `Restore`. The checkpoint is part of `SaveGameDto` and a bounded trailing SaveCodec **v13** extension. Existing **v1–v12** saves decode with `SceneCheckpoint = null`, meaning **unknown physical location**, not a fabricated default ship or automatic crew return. Saves written without a checkpoint add only one absent-marker byte. Invalid flags/theater IDs/party manifests reject on decode, even when a checksum is valid.

**Still NOT implemented/proved:** actual Unity scene loading/rebinding/spawn-anchor resolution, ship/shore camera, per-actor world position/path within a location, resumed actual physical gameplay or device testing. Stable authored anchor IDs are *not* numeric transforms. Engine adapters must recognize the active checkpoint and resolve the correct authored scene and anchor, reject missing assets gracefully and preserve the existing same-session authority. If exact positions during a shore excursion become necessary, evolve an explicit bounded position checkpoint later rather than serializing NavMesh paths, camera or Unity GameObjects. No G2/G3 claim is implied by a valid v13 save. Do **not** auto-return the crew when loading; that fabricates an event and erases the duty penalty.

## Unity handoff (remaining #22)

1. Keep **one living authoritative WorldSession** for the run while switching between ship and shore. Scene LifetimeScope ownership and disposal must be explicit (prefer a persistent session root / thin scene adapters, never a per-scene new WorldSession). Confirm VContainer hierarchy and actual disposal with G2/G3.
2. Before scene switch, preflight the chosen expedition crew and issue the existing commands through `ShoreCrewTransfer`. On rejection, stay put and show feedback. On extraction, confirm survival/roster state, issue return commands, then unload through canonical inventory commands where requested.
3. Scenes instantiate exactly one actor proxy per relevant stable `EntityId` for the current theater, and rebind through `WorldSession` after loading; destroy/disable former proxies. No separate scene-local authoritative health, loot or crew clock. A selected set is transient: on scene changes revalidate actor IDs; never use prior GameObject references or persist UI selection into WorldState.
4. Time elapsed while on land should be fed explicitly into `WorldSession.Advance`, with winter fuel, staffing, repairs and injuries reprojected on the ship after return.
5. **Persisted checkpoint delivered in v13:** set `PhysicalSceneCheckpoint.Shore(sceneId, siteId, anchorId, partyIds)` after a successful shore transfer and `PhysicalSceneCheckpoint.Ship(sceneId, anchorId)` after return. Use `WorldSession.SetSceneCheckpoint`, then `Capture`/`SaveCodec.Encode`. On `WorldSession.Restore`, inspect `SceneCheckpoint` and choose the authored scene/anchor. A null legacy checkpoint has no justified auto-scene reentry. Test real scene load/rebind and that missing or invalid scene/anchor IDs fail without creating duplicate actors.
6. Unity G2 EditMode + G3 actual scene load/unload, click-to-loot, save/reopen/rebind, docked and shore return, and duplicate proxy detection are **NOT RUN** for this Core-only PR. G4 builds/devices are likewise not claimed.

## Test evidence

- Test-first RED: new `ShoreCrewTransferTests` do not compile before the Core class exists ([CI run 38410998236](https://github.com/MohamedXIV/Frostark/actions/runs/38410998236)).
- GREEN after paired Unity source `.meta`: [CI run 38411171343](https://github.com/MohamedXIV/Frostark/actions/runs/38411171343) — **205/205 .NET NUnit G1 PASS**, G0 structural PASS.
- 5 new tests: canonical group ordering, preflight safety, invalid roster, salvage/ship repair/fuel/save-ashore/return, continuous-vs-save-resume byte identity.

**Only the tested Core-first portion is ready to merge. Keep parent Issue #22 OPEN** until a real Unity scene transition and persisted physical theater reentry are proven.


## v13 checkpoint G1 evidence and migration — 2026-10-13

- `PhysicalSceneCheckpoint.Ship(sceneId, spawnAnchorId)` records a stable ship reentry point; `.Shore(sceneId, siteId, spawnAnchorId, partyIds)` requires nonempty valid IDs with deterministic ordinal ordering and immutable external snapshots.
- `WorldSession.SetSceneCheckpoint` changes **save reentry metadata only**. It does not execute a crew command, advance time, mutate WorldState, create a second simulation or change authoritative revisions. The ship/shore crew transfer still requires prior successful `SetCrewAwayCommand` commits and is validated against the persisted duty flags when saving.
- `SaveCodec.CurrentVersion = 13` appends a presence flag (0 = unspecified; 1 = typed checkpoint), followed by the bounded authored identifiers for known locations. Unknown former versions do not invent a location; legacy migrations retain their previous canonical world fingerprints.
- Engine G2/G3 remains open under #22. An anchor must resolve to an authored scene object; a saved checkpoint's validity as C# data is **not** proof that the scene exists or an actor navigates into it.
