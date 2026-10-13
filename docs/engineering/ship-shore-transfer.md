# R1 ship ↔ shore persistent crew transfer — Core-first handoff (#22)

**Status:** Pure-C# canonical crew transition contract implemented. **Unity scene switching, retained session lifetime and exact shore spawn are not yet implemented or validated.**

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

**NOT yet persisted or proved:** which landing scene/site is physically active, each actor's position and path, ship vs shore active camera, transition animation or spawn-point selection. The existing save schema has no canonical `current physical theater + landing scene ID + spawn location`. `IsAway` alone is *not enough* to recover where a character is. Therefore **do not claim reliable mid-shore scene resume in the UI yet**: the front-end must either withhold a resume-to-physical-scene option for unfinished shore excursions or implement a separately specified persisted location checkpoint. Do **not** auto-return the crew when loading; that would fabricate a new domain event and erase the duty penalty.

## Unity handoff (remaining #22)

1. Keep **one living authoritative WorldSession** for the run while switching between ship and shore. Scene LifetimeScope ownership and disposal must be explicit (prefer a persistent session root / thin scene adapters, never a per-scene new WorldSession). Confirm VContainer hierarchy and actual disposal with G2/G3.
2. Before scene switch, preflight the chosen expedition crew and issue the existing commands through `ShoreCrewTransfer`. On rejection, stay put and show feedback. On extraction, confirm survival/roster state, issue return commands, then unload through canonical inventory commands where requested.
3. Scenes instantiate exactly one actor proxy per relevant stable `EntityId` for the current theater, and rebind through `WorldSession` after loading; destroy/disable former proxies. No separate scene-local authoritative health, loot or crew clock. A selected set is transient: on scene changes revalidate actor IDs; never use prior GameObject references or persist UI selection into WorldState.
4. Time elapsed while on land should be fed explicitly into `WorldSession.Advance`, with winter fuel, staffing, repairs and injuries reprojected on the ship after return.
5. Define a small authoritative location checkpoint/schema before enabling **mid-shore scene resume**: at minimum scene/site ID, active theater, party membership and spawn/reentry anchor. Avoid storing transient input/path/animation; test compatible SaveCodec migration. #22 cannot close without this or an explicit owner-approved R1 save restriction.
6. Unity G2 EditMode + G3 actual scene load/unload, click-to-loot, save/reopen/rebind, docked and shore return, and duplicate proxy detection are **NOT RUN** for this Core-only PR. G4 builds/devices are likewise not claimed.

## Test evidence

- Test-first RED: new `ShoreCrewTransferTests` do not compile before the Core class exists ([CI run 38410998236](https://github.com/MohamedXIV/Frostark/actions/runs/38410998236)).
- GREEN after paired Unity source `.meta`: [CI run 38411171343](https://github.com/MohamedXIV/Frostark/actions/runs/38411171343) — **205/205 .NET NUnit G1 PASS**, G0 structural PASS.
- 5 new tests: canonical group ordering, preflight safety, invalid roster, salvage/ship repair/fuel/save-ashore/return, continuous-vs-save-resume byte identity.

**Only the tested Core-first portion is ready to merge. Keep parent Issue #22 OPEN** until a real Unity scene transition and persisted physical theater reentry are proven.
