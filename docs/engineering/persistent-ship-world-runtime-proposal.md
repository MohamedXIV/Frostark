# E12: Persistent Ship and Unified World Runtime

**Status:** Engineering proposal, **NOT implemented or ratified**. Recorded 2026-10-23.
**Game-design authority:** [Miro D11](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764688262429940).
**Tracking:** [Epic #127](https://github.com/MohamedXIV/Frostark/issues/127), [#128 ship residency](https://github.com/MohamedXIV/Frostark/issues/128), [#129 actor presence](https://github.com/MohamedXIV/Frostark/issues/129), [#130 coastal proof](https://github.com/MohamedXIV/Frostark/issues/130).
**Baseline:** [Draft PR #126](https://github.com/MohamedXIV/Frostark/pull/126) at `df7802f3d6699f3c5d2efe51aecb684e6b00774b`; no code changes in this document.

## Requirement and reason

The ship is the physical heart of FrostArk and should stay resident throughout ordinary ship-to-shore travel. Disembarking changes crew location and player focus, **not the lifetime of the physical ship**. Island, ocean and ruin environments can load/unload as needed. We are **not** committing now to infinite streaming, procedural world generation, any specific chunk size, ECS/DOTS or a third-party scene framework. See Miro D11 for the owner-approved fantasy, history, alternatives and rationale. This file captures the **engineering hypothesis and its verification criteria**, not a second game-design source.

## Current implementation versus required behavior

- `ShoreSceneTransitCoordinator` uses `LoadSceneAsync(destination, Additive)`, then unloads the previous environment scene. The WorldSession root survives via `DontDestroyOnLoad`, but the ship's own scene GameObjects **do not** survive if they belonged to that unloaded environment.
- `WorldSessionScope` owns one VContainer-scoped `WorldSession`. Preserve this: no scene-specific additional WorldSession or WorldClock.
- `PhysicalSceneCheckpoint` represents a single global Ship/Shore active theater. `WorldSession.SetSceneCheckpoint` stops ship Work observation on Shore; `ShipCrewDutyAutonomy` and `ShipCrewAgencyCoordinator` gate execution on global Ship; `TacticalCommandController` routes by global theater. That assumption fails when the ship and shore party are physical simultaneously.
- Core ship systems can continue abstractly offscene today. That **does not prove physical ship residency or valid physical Work/Rest attendance**.
- Existing #126 Core and Unity tests validate existing behavior, not this proposed architectural correction.

## Candidate ownership boundary

| Lifetime | Candidate authority | During coastal environment unload |
| --- | --- | --- |
| Run: domain, world time, save, command bus | Existing `WorldSessionScope` and `WorldSession` | Retain |
| Physical ship: hull/modules/interior, onboard actors, local grid | Persistent ship root or persistent ship scene (choose after focused inspection) | **Retain same GameObject instances within a run** |
| Shore/ocean/ruins chunk geometry and NavMesh | Additive environment loader, separate from run/ship | May unload independently |
| Actor theater/location, crew commands, duty/Needs, cargo | Canonical per-entity Core state with stable EntityIds | Retain; never infer from camera focus |
| Camera, selected actors, Unity active scene | Transient view/input adapter | Can change without mutating ship presence |
| Actual Work/Rest slot attestation | Physical observed occupancy translated into canonical reports | Never manufacture work when actor/slot is absent |

**Proposed seam:** a persistent run root plus one persistent ship physical runtime, and additive *surrounding* environment. This is a candidate, not a committed implementation style. A ship root that survives all scene transitions and is correctly destroyed on EndRun may be simpler than a new global streaming service.

## Transition invariants to implement

1. Preflight reachable gangway/landing anchor, destination environment and expedition crew before mutating canonical roster. Shore NavMesh must not be used for onboard navigation; ship Grid/A* stays local to the moving ship transform.
2. Load or retain the required shore environment **without unloading the ship**. Onboard crew, modules, ship-root transform, GridWalkers and workstations stay alive.
3. Commit departure/return as valid WorldSession commands, keeping one actor identity. A landing party is not also an active onboard worker; release departing actor's physical Work lease and claims. Do not assume `ShoreCrewTransfer` sequential commands form an atomic batch.
4. Route orders by validated actor location/theater and hit target, not the session's global scene focus. Shore agents use existing NavMesh; ship actors use ship-local grid. Ship work and onboard Needs continue when player camera moves shore-side.
5. Return through a real authored connection. Preserve same ship instances, eliminate duplicate proxies, and unload shore content only when it is no longer needed.
6. Save the canonical run plus enough per-actor location, site/chunk and reentry metadata; preserve V18 accepted ship Move goals and canonical work/claim evidence. A **new run or loaded save** may instantiate one ship; ordinary disembark must not reconstruct it.
7. Shutdown or replace a run by disposing exactly one persistent ship and WorldSession root. Stale input observers, event handlers and Unity objects must not survive into another run. Failed post-commit transitions must report reconciliation, not pretend rollback.

## Gate A: architecture reasoning, before implementation

On the actual current #126 integration head, map the ownership/teardown of `WorldSessionScope`, `ShoreSceneTransitCoordinator`, `ShipTacticalSceneBootstrap`, `ShipCrewAgencyCoordinator`, `ShipCrewDutyAutonomy`, `PhysicalSceneCheckpoint` and `TacticalCommandController`. Decide between a resident ship Scene and a resident ship root by inspecting actual lifetime and local-coordinate requirements. Describe exact fields/interfaces and save compatibility, lifecycle transitions, error recovery and who owns teardown. Keep this ADR **Proposed**.

## Implementation and falsifiable evidence

- **#128:** Unity G2/G3 shows the same physical ship root/child instance IDs and ship transform through ship -> shore -> ship; the environment streams independently; no duplicate roots or clocks.
- **#129:** Actual onboard Work/Rest/Needs continue while landing party moves on NavMesh; no fake Repair minute, double reservation/use, lost accepted Move or wrong-theater dispatch. Camera/selection cannot disable onboard crew.
- **#130:** One authored ship + one coastal island: physically disembark, issue shore order/interact, reembark, and save/restore while both aboard and ashore. Preserve crew IDs, resources, work, party location and modules. After a genuine load, construct one ship only. Check real OS pointer/HUD behavior; report G4/device evidence separately.
- **Ratification:** only after exact-head Unity evidence passes #130, update technical ADR and relevant engineering status. If it fails, keep this candidate provisional and revise. Don't mark E12 implemented based on diagrams, hosted Core tests or a scene fixture alone.

## Non-goals and related ownership

No large-scale chunk streaming yet, no second simulation, no broad offscreen AI rewrite, no DOTS/ECS, no ship NavMesh rebaking. Existing E11 [#108](https://github.com/MohamedXIV/Frostark/issues/108)/[#114](https://github.com/MohamedXIV/Frostark/issues/114) retains RTS/movement UX ownership; [#60](https://github.com/MohamedXIV/Frostark/issues/60) owns marine physics/helm. D01/D09 on Miro continue to own ship navigation/crew agency design. #126 remains a verified integration baseline, not a permanent-scene-lifetime proof.
