# E11 #112 — Authored physical Work / Rest autonomy, first executable slice

**Status:** implemented in source on **stacked Draft [PR #121](https://github.com/MohamedXIV/Frostark/pull/121)**, sibling of pending-order persistence Draft #120. Based on exact **Unity-tested parent Draft #119 HEAD `d9d70d3ee61fb760b38746f29b1314cc4bae4a8c`**. No merge, no changes to the parent #119, #120 or main. **New Unity G2/G3: NOT RUN on #121.**

## Authoritative ownership

The existing `WorldSession` and `CrewState.Staff(WorldMinute)` already own eligible staff, fixed duty priority, shift windows, fatigue/recovery, backups and minute-by-minute progress. `WorldState.GetCrewStation` records a player's persisted station assignment. `CrewInteractionState` records authored Work/Rest slots and their exclusive short-lived claims. **No parallel GOAP, new staffing scheduler, work progress clock, invented duty, fake stock or duplicate fatigue effects are introduced.** This slice is physical choreography toward existing work and rest anchors, not authorization to earn effects simply by standing beside a machine.

## Delivered

1. **Canonical staffed-role projection:** `CrewState.StaffedRoleFor(id, minute)` reads **the same private Staff(...) decision** used by `CrewState.Tick`. A nominally trained but out-of-shift, wounded, hungry/recovering, absent or displaced backup actor never receives a phantom staffed role.
2. **Read-only duty offer planner:** `CrewDutyActivityPlanner.Find` uses valid ship crew, staffed role, persisted station assignment AND real placed module's `Work` slot to offer work. If the member is off-shift/recovering or unstaffed and fatigued, it may offer an authored placed `Rest` slot; fully rested off-shift crew simply idle. Competing slot/crew claims make that offer unavailable. Deterministic slot order is ordinal ID. Room and physical walkability remain the scene adapter's responsibility.
3. **One physical Unity adapter:** `ShipCrewDutyAutonomy` sits alongside existing `ShipCrewAgencyCoordinator`, only starts activity after actor becomes unselected, has no active accepted player Move, no pending provision and is idle. It validates actual `ShipSpatialMap` reachability and portal exclusion, claims an authored slot via `WorldSession.Submit(ClaimCrewInteractionCommand)`, follows normal `ShipGridWalker.TryMoveTo`, remains stationed on arrival, and releases owned claim via canonical Release command on cancellation. It never calls `WorldSession.Advance` or performs synthetic Work/Rest effects.
4. **Safe handover and Needs:** selection/authoritative new accepted player Move take priority; while traversing a reserved edge, cancellation requests `RequestStopAtSafeCell` and only releases claim after a safe stop. A newly *accepted* player Move supersedes physical duty without canceling the new route. At hunger/thirst >= `CrewAgencyPolicy.SeekProvisionAt`, physical Work/Rest yields its claim so the existing provision discovery is not masked by an owned work slot. Critical Needs/danger decisions stay in existing policy. No imaginary `Resume Auto` switch.
5. **Integration:** the existing `ShipTacticalSceneBootstrap` attaches/configures one duty adapter and rebinds navigation after a door/layout change.

## Tests and gates

- Added **4 NUnit Core G1 tests**: only genuinely staffed, assigned crew get Work; offshift/recovering fatigue and Rest stay on canonical minute clock; staffing backup and unplaced station; exclusive claim prevention. These tests never mutate crew hours or inventory during planning.
- Added **4 authored Unity G2 EditMode** tests: selected Hold, deselected player-move completion → work slot, mid-edge selected worker safe stop/claim release, accepted override preservation, offshift physical Rest and canonical-only fatigue. Tests have NOT been run in Unity on #121.
- Added G0 static wiring regression and source `.meta` GUIDs.
- **Exact code HEAD `953d83ae884ea30320ccbadee45f71566fa45fb9` source CI [#39698874714](https://github.com/MohamedXIV/Frostark/actions/runs/39698874714) PASS:** **26/26 G0**, **307/307 G1** (job logs inspected). Future docs-only commit may move head; verify exact source before Unity runs.
- Prior parent #119 Unity report: **73/73 EditMode, 26/26 PlayMode**, G0 25/25 and G1 303/303. **Do not attribute those Unity results to this child branch.**

## Honest unfinished boundaries

- **Physical presence vs productive tick:** Core `CrewState.Staff` still advances canonical staffed work and fatigue regardless of whether the scene-local walker has reached a station. Making work-effect authorization depend on both near-scene and offscreen physical presence requires an explicit model of offscreen/scene handover—not a parallel simulation.
- **#120 integration:** sibling Draft #120 implements typed pending Move save/reentry, separately. The duty adapter has not yet been rebased/merged over #120; do not combine both branches without deliberate reconciliation and Unity retesting.
- **Player override mid-edge:** accepted commands supersede old duty correctly, but *issuing* a new player Move mid-edge may still be rejected by the current walker until a safe stop; a queued replay/priority layer remains a #112 deliverable.
- **Claim scene transfer:** claims are canonical WorldClock-expiring. This slice does not issue commands from `OnDestroy` because teardown ordering is unreliable; explicit scene-transfer release/reentry reconciliation remains.
- **Door deadlocks, shore assignments/NavMesh parity, save of standing Work/Rest physical action, work outcome authority, real on-stage work/bed content, G3 and G4 hardware/UI builds** remain open in #112/#113/#114. No claim that #112 as a whole is complete.

## Required Unity agent acceptance on this sibling branch

On `feat/112-physical-duty-autonomy`, verify Unity 6000.6.0f1 compilation and full EditMode + PlayMode suites, especially `ShipCrewDutyAutonomyEditModeTests`. Add a PlayMode/manual fixture with authored placed Work/Rest slots and stationed crew, test Work → select → accepted Move → deselect → Work return, plus hunger interruption, claim count and zero phantom resource changes. Fix in this child branch only; don't change sibling #120.


## 2026-10-22 — continued independent execution while #120 is under local Unity QA

The user authorized continued GPT-6 engineering **without waiting for the parallel #120 testing agent**. All changes here remain exclusive to **#121**, and #120's agent-owned branch is untouched.

1. **Nonfatal traffic congestion:** `ShipCrewDutyAutonomy` no longer treats `ShipGridWalker.LastError == "WaitingForTraffic"` as terminal. The worker retains its genuine Work/Rest claim while the traffic system waits/replans, and resumes after the obstruction clears. A new G2 fixture blocks three next-step candidates with stationary peers, verifies no phantom claim release, then removes them and checks eventual physical arrival.
2. **Save/reentry physical claim reconciliation:** after the Unity presentation coordinator is reconstructed, existing canonical Work/Rest claims need not vanish. `TryStart` now *adopts* a still-valid matching claim instead of issuing a duplicate world command. If the actor is selected, has an active order, or the authored duty/slot no longer matches, the stale Work/Rest claim is released at a safe cell. Eat/Drink claims remain owned by the existing Needs/provision coordinator, not this one. Two new G2 fixtures cover reentry from an authored safe spawn (exactly one claim and no revision increase) and selected-actor orphan cleanup. This is **not** a full #120 save codec, shore-scene transfer, or complete physical-action persistence contract.
3. **Actual Unity-frame G3 test authored:** `ShipCrewDutyAutonomyPlayModeTests.EngineerReturnsToAuthoredStationAfterRealFramePlayerMove` creates a physical ship root, placed Work module, actual crew/collider-free ship Grid walker, persisted assignment and registered RTS input. It waits on real `yield return null` frames to prove Work arrival, player Move supersession, deselection and autonomous return to Work without a duplicate WorldClock or manufactured work progress. It does **not** claim OS-level pointer/HUD input or an authored production demo containing a work module.
4. G0 now guards the new G3 source and Unity metadata, plus transient traffic waiting logic. The G3 fixture observes the player's intermediate arrival **before** automatic same-frame Work resumption to avoid timing-dependent false failures.

**Latest substantive source HEAD `23f64dd8c55c9e42181f6768a3e58ea0f2f43390`:** [GitHub CI #39704681675](https://github.com/MohamedXIV/Frostark/actions/runs/39704681675) **PASS**, 26/26 G0 and 308/308 G1; full logs checked. This is **source/static and Core validation only**. The updated Unity fixture count is **7 new duty G2 EditMode tests + 1 duty G3 PlayMode test, all NOT RUN on #121**. The full Unity suite must be rerun before merging or claiming runtime correctness.

**Known remaining risks:** a new player order issued halfway across an edge is not yet queued; scene-transit claim handover is still not an explicit before-unload operation; the current canonical staffed WorldClock can still count productive minutes while a crew proxy is physically travelling in-scene; and real staged Work/Rest/Need loops plus shore parity and G4 are not yet accepted. Do not infer those features from a green Core CI.
