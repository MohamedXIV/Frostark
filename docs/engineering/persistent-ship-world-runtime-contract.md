# E12 Persistent Ship World Runtime — Gate A Engineering Contract

- **Status:** Provisional Engineering Contract (Gate A resolved; G0/G1 verified; Unity G2/G3 pending authorized Unity CLI execution)
- **Canonical Design Authority:** [FrostArk — Design Lab (Miro)](https://miro.com/app/board/uXjVEYctyzg=/) — Card `3458764669395439972` / Document `3458764669396100181` (*Ship-Centered Persistent World Architecture*)
- **GitHub Tracking:** Epic [#127](https://github.com/MohamedXIV/Frostark/issues/127), Issue [#128](https://github.com/MohamedXIV/Frostark/issues/128), Issue [#129](https://github.com/MohamedXIV/Frostark/issues/129), Issue [#130](https://github.com/MohamedXIV/Frostark/issues/130)
- **Stacked Baseline:** PR [#126](https://github.com/MohamedXIV/Frostark/pull/126) (`integration/112-crew-agency-save-and-duty`)

---

## 1. Purpose & Gate A Resolution

Before E12, `ShoreSceneTransitCoordinator`, `WorldSession`, `CrewTheaterMoveRouter`, `ShipCrewAgencyCoordinator`, `ShipCrewDutyAutonomy`, and `TacticalCommandController` coupled **ship physical residency** and **shipboard simulation/routing** to a single global scene checkpoint (`SceneCheckpoint.Theater == PhysicalTheater.Ship`). Departing to shore unloaded the ship scene, stopped shipboard work observation, cleared onboard pending ship moves, and disabled shipboard crew autonomy.

This contract resolves **Architecture Gate A** by defining the exact ownership, lifetime, teardown, and per-actor theater rules for the persistent ship runtime without introducing a speculative open-world streaming framework.

---

## 2. Core & Unity Ownership Matrix

| Component | Authoritative Ownership | Lifetime & Teardown Boundary |
|---|---|---|
| `WorldSessionScope` | Owns the single `WorldSession` (`WorldClock`, `WorldState`, `WorldCommandBus`) and run-scoped Unity root (`DontDestroyOnLoad`). | Created once at run start or save restore; disposed via `EndRun()` / `OnDestroy()` when quitting to menu or replacing the run with a loaded save. |
| `WorldSession.ResidentShipSceneId` | Tracks the canonical ship `EntityId` (`scene:ship:*`) across `PhysicalTheater.Ship` and `PhysicalTheater.Shore` checkpoints. | Set when a `PhysicalTheater.Ship` checkpoint is established, when `BeginShipWorkObservation` binds a resident ship, or when restoring pending ship crew moves from a save. Cleared only when the checkpoint is reset to `null` or replaced by a *different* ship scene. |
| `PhysicalSceneCheckpoint` (Save v18) | Persists whether an expedition party is currently ashore (`PhysicalTheater.Shore` with `SceneId`, `SiteId`, `AnchorId`, `PartyIds`) or all crew are aboard (`PhysicalTheater.Ship` with `SceneId`, `AnchorId`). | Does **not** require a binary save-schema bump beyond v18: when `Theater == PhysicalTheater.Shore`, the ship remains resident at `ResidentShipSceneId`, away crew are identified by `PartyIds` / `CrewMember.IsAway`, and onboard crew (`!IsAway`) retain their `PendingCrewMoves` and `CrewInteractionSlot` claims. |
| `ShipTacticalSceneBootstrap` | Owns the resident physical ship root (`Transform`), compartment origins, `ShipRoomClickSurface` colliders, `ShipSpatialMap`, and shipboard `ShipCrewSelectionProxy` / `ShipGridWalker` bindings. | Instantiated **once** per run/load. Never destroyed or re-instantiated during ordinary `Ship -> Shore -> Ship` or `Shore A -> Shore B` transitions. |
| `ShoreSceneTransitCoordinator` | Owns additive coastal environment loading/unloading, pre-commit anchor/proxy/NavMesh validation, atomic `ShoreCrewTransfer` + `SetSceneCheckpoint` commit, and per-actor ship/shore proxy activation sync. | Preserves the resident ship scene/root during `TryDepart` (loads shore scene additively without unloading the resident ship scene) and reuses the resident ship scene/root during `TryReturn` (unloads only the coastal environment scene without reloading the ship scene). |
| `ShipCrewAgencyCoordinator` & `ShipCrewDutyAutonomy` | Own frame-to-frame shipboard Needs (`Eat`/`Drink`), `Work`/`Rest` slot choreography, and minute-bound `ReportShipDutyPresence` / `ReportShipWorkPresence` attestations for onboard crew (`!member.IsAway`). | Remain active and ticking whenever the resident ship is initialized—including while `SceneCheckpoint.Theater == PhysicalTheater.Shore`. Immediately release any shipboard claims/walks for crew members who transition to `IsAway == true`. |
| `TacticalCommandController` & `CrewTheaterMoveRouter` | Own player selection and order dispatch across both theaters. | Route per actor (`member.IsAway` + `checkpoint.PartyIds`): `PhysicalTheater.Ship` orders (`ShipRoomClickSurface` / `IssueShipMoveOrder`) route to onboard crew (`!IsAway`) even while a shore scene is loaded; `PhysicalTheater.Shore` orders (`IssueShoreMoveOrder` / shore ground, salvage, combat targets) route only to away shore-party crew (`IsAway` in `PartyIds`). |

---

## 3. Core Invariants

1. **Single Authoritative Session & Clock:**
   - Exactly one `WorldSession` and `WorldClock` govern both shipboard and shore actors.
   - No duplicate clock or offscreen fallback runs for onboard crew while the physical ship is resident and observed (`BeginShipWorkObservation` remains active across `Ship -> Shore -> Ship`).
2. **No Ship Destruction or Duplicate Instantiation on Ordinary Transitions:**
   - `TryDepart` loads the coastal scene additively and keeps the resident ship scene and its `shipRoot` hierarchy alive with identical Unity `GetInstanceID()` values.
   - `TryReturn` reuses the already-loaded resident ship scene/root and unloads only the coastal environment scene.
3. **Per-Actor Physical Presence & Proxy Exclusivity:**
   - Each crew member has at most **one** active physical proxy at any time:
     - Onboard crew (`!member.IsAway`): active `ShipCrewSelectionProxy` + `ShipGridWalker` on the resident ship root; any shore-scene proxy for that `EntityId` is inactive.
     - Away expedition crew (`member.IsAway`): active `TacticalActorProxy` + `NavMeshAgent` in the loaded coastal scene; their shipboard proxy on the resident ship is suspended (`ShipGridWalker.SuspendForShoreDeparture()` + `gameObject.SetActive(false)`) so they never block ship corridors or create duplicate active proxies.
   - On `TryReturn`, returning crew resume on the existing ship root (`ResumeFromShoreAt`) at a valid non-colliding walkable reentry cell without resetting or interrupting crew who remained aboard.
4. **Save/Load & Pending Order Continuity:**
   - Saving while aboard or while a landing party is ashore preserves canonical `WorldState`, `SceneCheckpoint`, `CrewInteractionState.Claims`, and onboard `PendingCrewMoves`.
   - Restoring a save instantiates the world once and never creates duplicate ship roots or resurrects collected shore salvage.
