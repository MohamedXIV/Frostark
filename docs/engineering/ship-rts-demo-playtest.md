# FrostArk — Ship RTS Demo scene playtest (E11 / #114)

**Status: authored on Draft PR #116; Unity Editor G2/G3 NOT RUN here.** This is a dedicated throwaway test scene, **not** the final ship game scene. It is stacked over PR #117 and PR #118.

## Exact entry point

- Unity project version from `ProjectSettings/ProjectVersion.txt`: **6000.6.0f1**.
- Branch: `feat/111-rts-selection-input` (contains ancestor merge commits for #117/#118).
- Scene: **`Assets/FrostArk/Scenes/ShipRtsDemo.unity`**, explicitly registered in `ProjectSettings/EditorBuildSettings.asset`.
- Open the scene in Unity Editor and press **Play**. Its single serialized `ShipRtsDemoRuntime` entry generates the small visible two-room interior **when Play starts**. It appears mostly empty in Edit Scene view on purpose; this avoids editing unrelated production scenes or hand-assigning object references.
- Four colored capsule crew proxies spawn in the bridge room. The adjacent engine room is separated by one bulkhead door. Ship root, room local origins, floor colliders, door portal, crew roster, canonical `WorldSession` and camera are wired by `ShipRtsDemoRuntime.BuildDemo()` with `ShipTacticalSceneBootstrap.Initialize(session)`. No NavMesh is needed onboard.

## Controls to exercise

- **Left-click** crew to select; **Shift+left-click** another crew to extend selection.
- **Left-click drag rectangle** around several crew to select up to **4** in stable order.
- **Right-click** the **floor** in the engine room to issue an A* ship-local move from the bridge via the open door.
- **Left-click blank floor** to deselect without cancelling already accepted Move.
- **C / O** closes/opens the bulkhead. Closing while moving invalidates any now-unreachable path; reopening makes new orders possible.
- **M** toggles small moving-ship root offsets so actors must keep local room-relative coordinates while the ship moves. **H** toggles the help overlay.
- The top-left HUD also has clickable **Open door**, **Close door**, **Move ship** controls.

## Technical truth / why this fixture is distinct

- The ship build/navigation source is real `WorldState.Layout`: two canonical 6x6 compartments and two 1x1 machine modules. The map derives from `ShipSpatialMap` with authored `ShipRoomPortal` endpoints at bridge (5,2) and engine (0,2).
- Each actor identity is stable in `WorldSession.CrewDuty`, not inferred from a new visual GameObject. The room-qualified move goal is `ShipGridCell(RoomId,X,Y)`; `ShipGridWalker` animates in local coordinates of a parent ship transform.
- `TacticalCommandController` handles real mouse raycasts, selection and dispatch. The route checks the canonical ship checkpoint and correct scene ID, rather than silently falling back to shore NavMesh.
- This **fixture** constructs one `WorldSession` and disposes it when destroyed. It is deliberately separate from `WorldSessionScope` and from existing `TacticalVerticalSlice`, `ShipShoreTransitLauncher` and Galley draft. It does not touch saves.
- Visually the demo intentionally uses basic primitives and in-Play creation; it is a first **behavioral** slice, not final art/prefabs.

## Evidence / tests

- `tests/validation/test_ship_rts_scene_contract.py` checks the actual checked-in `.unity` → `.cs.meta` GUID and `EditorBuildSettings` scene path. It is **static evidence only**.
- Authored Unity G2 `ShipRtsDemoSceneEditModeTests`: import the *actual scene asset*, inspect serialized entry script, instantiate runtime scene content with real crew+rooms, test door open/closed Core route visibility.
- Authored Unity G3 `ShipRtsDemoScenePlayModeTests`: load the *actual registered scene* in PlayMode, let Unity run `Start()`, use the actual `TacticalCommandController.ProcessContextClick` and exercise selected crew, door close/reopen and move acceptance.
- Earlier `ShipRtsPointerBridgeEditModeTests` and `ShipTheaterMovePlayModeTests` cover box selection, mixed per-actor rejections, committed build invalidation and moving ship root. They also require Unity execution.
- No G2/G3 result is known until somebody opens the Unity Editor and records Test Runner output. A green Core G0/G1 GitHub workflow does **not** imply the Unity scene imports, compiles, or animates successfully.

## Important outstanding limitations

- No crew-collision avoidance, formations, or reserved destination cells: ordering four crew onto the same cell may visually overlap them.
- No actor-coordinate persistence or physical save/re-entry; no integrated GOAP/Needs/player-order lifetime contract yet (#112), and no multi-agent congestion (#113).
- No persistent authored door states; door status in this fixture is transient and resets on Play.
- This demo is ship-only. Real two-theater transfer and full #114 end-to-end acceptance still need an actual G3 Unity run.
- Do not merge Draft PR #116 before parent #118 → #117 and actual Unity import/PlayMode evidence.

## 2026-10-21 — real Unity G2/G3 smoke on prior head + lifecycle repair

First **actual local Unity 6000.6.0f1 run** on `34ad9940dd1db00f7d698d8d22a20b23c95632fb` established:

- **Unity compilation PASS** (`compilationFailed=false`, no errors, fresh branch-111 assemblies).
- **EditMode 24/25 PASS; PlayMode 2/2 PASS** (separate runs after domain reload). The sole failing EditMode case was `ShipRtsPointerBridgeEditModeTests.BoxSelectAndRoomRightClickDispatchTwoRealShipWalkers`: selection controller returned 2 IDs, but `FirstActor.IsSelected` remained false. Root cause: `TacticalCommandController` only subscribed to `TacticalSelectionController.SelectionChanged` in `Awake`, which the manually constructed EditMode fixture did not invoke.
- Manual PlayMode camera/selection/drag/blank-click/room-aware right-click/movement-after-deselect/door C and O/ship M/help H/HUD click-through isolation all **PASS** in the agent's synthetic input tests. Cross-room Move completed; physical scene and selected-four visual markers rendered. Real OS-level pointer input and IMGUI **button activation** remain unverified.
- Back-to-back async PlayMode test runs destabilized Unity's Test Runner; perform a domain reload or separate test run between suites, not a claims-based product regression. An old `InitTestScene` temporary cleanup caused runner state complaints, not a demonstrated gameplay failure.
- Source fix on **this later branch head** binds selection notifications idempotently via `EnsureSelectionBinding()` when direct `Selection`, actor registration and box-selection APIs are used, not only in `Awake`. Added Unity G2 `EditModeSelectionNotificationsUpdateRegisteredShipVisualsWithoutAwake` to cover direct selection, exclusion, solo replacement, and clear, plus a G0 static assertion.
- **Unity retest of the lifecycle fix still required.** First run's 26/27 PASS is evidence for the old head only; do not claim 28/28 until actual editor reports. With the new authored EditMode test there are **28 expected targeted tests** (26 EditMode, 2 PlayMode), not 27. Specifically rerun the prior failure, then all G2 and both independent G3 fixtures.
