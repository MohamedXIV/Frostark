# E11 — Cooperative crew movement and adaptive group destinations (execution contract)

**Status:** implementation **IN PROGRESS** on Draft PR #116; source changes present, **Unity G2/G3 retest NOT RUN on the new source**.  
**Last reviewed:** 2026-10-21.  
**Design authority:** [Miro D01 Ship Navigation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187), [D03 Smart Interactions](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188), [D09 Crew Agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). This is a scoped implementation agreement, not a replacement for Miro.

**Owners / issue boundaries:**
- [#113](https://github.com/MohamedXIV/Frostark/issues/113) owns ship-local routing, smoothing, moving occupancy, corridor/portal traffic.
- [#32](https://github.com/MohamedXIV/Frostark/issues/32) owns **shared group destination/formation** and shoreline NavMesh parity. Do not create a second shore-specific formation system.
- [#112](https://github.com/MohamedXIV/Frostark/issues/112) owns order-vs-autonomy arbitration, interruptions and consequential order/claim persistence.
- [#109](https://github.com/MohamedXIV/Frostark/issues/109), [#110](https://github.com/MohamedXIV/Frostark/issues/110), and [#111](https://github.com/MohamedXIV/Frostark/issues/111) provide existing theater-aware dispatch, layout-derived ship navigation, and up-to-four selection.
- [#114](https://github.com/MohamedXIV/Frostark/issues/114) is the final cross-domain ship/shore Unity acceptance gate.

## Observed limitations of current baseline

Read from the actual #116 stack on 2026-10-21:

1. `ShipSpatialMap.Neighbors` uses only four orthogonal edges, uniform unit cost and room portals, leading to Manhattan/L-shaped paths. Its A* heuristic is zero whenever the derived map has portals, intentionally preserving correctness but often producing tie-biased routes.
2. `TacticalCommandController.IssueShipMoveOrder` sends the **same** `ShipGridCell` to every selected crew executor. The current shore physical Move also shares one world destination; legacy shore unscoped Move offsets companions in a simple circle. Neither is a unified, reachable, claim-aware destination allocator.
3. `ShipGridWalker` moves independently along exact cell centers at constant speed, with bounded visual turning but no speed ramp, collision/occupancy, dynamic agent coordination or corner smoothing.
4. Layout/build footprint and permanent interaction clearance are already modeled. Do **not** confuse them with mobile actor occupancy, future arrival slots or temporary work-slot claims.
5. Unity 6000.6.0f1 targeted G2 **26/26** and G3 **2/2** passed on source `0c775bed7402e5294f22bb3bc0176a49d58bfb1f`. These prove the baseline can navigate a two-room door, **not** formation/traffic correctness.

## Behavioral contracts

### Shared order and group goal allocation (ship AND shore)

- A clicked point is a **group target / center**, not an unconditional exact destination for each selected actor. One selected actor can still target the nearest reachable, unclaimed point to the click.
- Produce deterministic **actor-specific** goals, stable by `EntityId` for tie breaks. Prefer nearby reachable free space, then fit a formation to the actual available geometry; account for current actor positions, travel cost and obstacles. Keep group spatial coherence without demanding rigid military formations inside cramped cabins.
- A formation is a **desired arrival arrangement**, not a requirement to maintain that shape through a narrow passage. Collapse to single file at a one-cell door and reform where there is room. If no legal arrival slot exists for an actor, it receives an **explicit rejection / waiting reason**; never fake acceptance or stack at the click.
- The planner returns individual status, assigned goal and reason. **Order accepted != physically arrived**. A partially accepted group order reports per-actor outcomes and only accepted actor IDs in the emitted intent.
- Model common semantic entities (illustrative names, not a commitment to duplicate Core records): `GroupMoveRequest`, `GroupGoalAssignment`, `MovementStatus`, `ITacticalNavigationAdapter`. The theater adapter projects/validates goals while the shared coordinator handles goal allocation and status. **Keep the existing** `CrewMoveGoal`, `CrewTheaterMoveDispatcher` and `TacticalSelectionController` as integration seams; do not build a new authoritative world state.
- Current UI selection cap is **4** for the playable slice; planning APIs should not hardcode a cap. Future #32 UX/performance decides larger squads.

### Ship: static path vs moving traffic

- **Static navigation:** `ShipSpatialMap` remains a derived view of canonical `WorldState.Layout`, module footprints, portals, permanently walkable/blocked cells, and interaction clearance. The ship root can move in world space without changing ship-local goals.
- **Path shape:** permit diagonal (8-neighbor) movement **only** when both side-adjacent cardinal cells are valid; prevent corner cutting and walking through module/hull geometry. Use cardinal/diagonal costs and an admissible heuristic that respects portals; if uncertain about cross-room bounds, retain a safe zero heuristic rather than an incorrect estimate. Treat a portal as explicit room-local graph connectivity.
- **Smoothing:** line-of-sight shortcutting, steering, acceleration/deceleration and turn smoothing may change the rendered line **only within verified passable space**. Grid path clearance/occupancy remains authoritative; do not let an interpolated diagonal clip a bulkhead. Keep a safe stop/replan position on interruption.
- **Actual occupancy:** one actor owns its current occupied cell/footprint until a safe transition. Static or stopped actors are barriers; other crew never pass through them or use the same stop position.
- **Near-future reservations:** coordinate next-cell/edge/one-cell-door use over a **bounded rolling lookahead**. Two agents cannot enter the same cell at the same time, swap opposite directions across the same edge at the same time, or pass one another through a one-cell corridor. A stopped/mid-step actor invalidates conflicting future reservations; the other agents wait or replan from safe positions.
- **Replanning triggers, not per-frame A*:** changed obstruction/portal, new or cancelled order, significant lost progress, disabled/wounded actor, changed reserved route, or bounded traffic decision interval. Rendering can update each frame, but global pathfinding should not.
- **Yield/deadlock:** define deterministic right-of-way with starvation prevention. At narrow choke points use a door token/queue with safe wait cells; when head-on peers cannot pass, one yields/backs up only into verified free space. Never infinite oscillation, collision-through, teleport, or unconditional blocking forever. Explicit `WaitingForTraffic`, `Rerouting`, `Blocked` states.
- Three different concepts must stay separate: `PhysicalOccupancy` (currently physically here), `DestinationReservation` (future stop), and `InteractionSlotClaim` (exclusive work entitlement). Align claim release with #112 on cancel, interruption, wound, invalid destination, scene transfer and timeout; no leaked/stale claims.

### Shore: same contract, separate navigation backend

- Keep Unity `NavMeshAgent` and world-space NavMesh. Candidate group destinations must be **sampled AND checked for reachable path** with suitable spacing / agent radius; separate arrival slots, not an identical `SetDestination` call for all.
- Use built-in NavMeshAgent steering / avoidance / radius / acceleration / angular speed before introducing a third-party crowd library. If avoidance alone cannot solve a choke/door, the common coordinator provides a bounded queue/priority policy rather than pretending local velocity avoidance solves global deadlocks.
- Do not port ship-cell reservations or ship Grid A* onto the shore. Implement common results/goal assignment with backend-specific occupancy, route feasibility and collision techniques.
- Selection, stable crew IDs, partial dispatch, order priority and autonomous resumption should mean the **same** on both theaters.

### State, correctness and lifecycle

- `WorldSession` / canonical world clock remain authority for consequential orders, duties, Needs, injury, inventory, work-slot claims and transit; **no second gameplay simulation clock** or shadow save-authority from locomotion.
- Local traffic reservations are transient execution state; current positions and consequential, already-accepted orders must be recoverable/revalidated for scene reload/reentry as owned by #112/#114. No saving ephemeral mouse selection.
- Selected actors do not start unrelated idle autonomous goals. Deselect never cancels an already accepted Move. On completion, unselected idle actors may choose Needs/duties again; emergencies can interrupt with an explicit reason. No per-character `Resume Auto`.

## Execution order and strict evidence gates

| Gate | Scope | Minimum automated evidence |
| --- | --- | --- |
| **A — Path/locomotion** (#113) | Ship safe 8-neighbor A*, corner rules, stable deterministic ties, route smoothing and visible acceleration/turning; maintain prior door and moving-ship behavior | G1 route tests (diagonal, adjacent blocked corner, portal); G2 movement tests; G3 moving ship + bulkhead visual path |
| **B — Group target assignment** (#32 + #113) | One shared actor-to-slot allocator with backend-specific reachable candidates; ship and shore assign separate arrival positions | G1 stable 1–4 actor assignments and insufficient space; G2 ship 4-unit group; G3 shore NavMesh 4-unit target |
| **C — Occupancy / parking** (#113) | Ship occupied current cells, assigned destination reservations; no stationary actor pass-through | G1 blocked static actor, no duplicate goal; G2+G3 collision/parking |
| **D — Moving traffic** (#113 + #112) | Bounded rolling reservations, door token, yielding, edge swap prevention, cancel/replan, Need interruption | G1 two agents opposite directions / no cell or edge swap; G3 stop-while-moving and door lock mid-crossing, no starvation |
| **E — Dual-domain proof** (#114) | Ship-to-shore-to-ship same actor IDs, actual orders, no clones, save/reentry and interaction slot consistency | Unity G2/G3 both theaters + replay/save tests + G4 desktop acceptance |

Implementation can add small tests/commits at each gate, but **do not declare a gate PASS on static code or Core CI alone**. Preserve existing 28/28 Unity targeted RTS tests, plus Core reproducibility. Prefer simple bounded reservation + yield over full CBS/WHCA* until actual traffic stress evidence justifies extra complexity.

## Out of scope for this iteration

- Full optimal multi-agent MAPF/CBS, heavy crowd simulation, a replacement NavMesh system or a new Unity navigation asset.
- Final art, animation assets, squad combat formations, camera UX or complex cover behavior; these are separate visual/tactical tasks.
- New persistent saved selection, phantom work-slot claims or a complete GOAP rewrite.

## First code slice after this contract

Begin with **Gate A**, keeping the public ship/shore command semantics unchanged; then deliver **Gate B** before treating movement as a completed RTS user experience. Work on the existing stacked PR path or a clean dependency-aware branch; avoid overlapping changes with the currently open #116/#117/#118 drafts and do not merge without Unity evidence.

## 2026-10-21 — first executable slice shipped to draft branch (NOT Unity-verified)

The following changes were implemented on the existing `feat/111-rts-selection-input` stacked PR branch, without touching `main` or merging the draft:

- **Gate A / #113, implementation:** `ShipSpatialMap` now generates eight neighbors, with cardinal cost 10, diagonal cost 14, prohibited diagonal corner cutting, and safe octile A* heuristic when no arbitrary portal shortcuts exist. A room-local supercover line check verifies shortcuts cannot tunnel through machines or across rooms. `ShipGridWalker` uses the farthest clear room-local waypoint when alone, ramps speed and turns smoothly, with existing ship-root-local transforms preserved. Core G1 diagonal/corner tests and a Unity G2 accelerated diagonal test were added.
- **Gate B / #32, first implementation:** `TacticalGroupGoalPlanner` is a shared deterministic, geometry-agnostic actor→arrival allocator. It scores each actor's feasible candidates by travel cost plus distance from group click center, resolves ties by stable EntityId and candidate order, and prevents overlapping arrival slots. `TacticalTheaterSelectionCommands.MoveSelectedAssigned` uses existing `CrewTheaterMoveDispatcher` per actor and emits `DestinationUnavailable` when no legal stop exists; the RTS controller reuses it in both domains. Ship candidates are derived from walkable local cells and per-crew A* paths; shore candidates are sampled from NavMesh with complete-path checks and native NavMeshAgent movement/avoidance. The interaction/attack contracts remain separate. Unity G2 suite now checks distinct ship stops and deterministic/group-capacity assignment; existing moving-ship G3 assertion updated to expect separate goals.
- **Gate C / #113, conservative first implementation:** new per-ship-root `ShipTrafficCoordinator` registers physical walkers and prevents stopping on another actor or taking its active final goal. Each walker retains its current occupancy while reserving its next step; entering an occupied next cell or squeezing diagonally between two occupied side cells causes explicit `WaitingForTraffic` instead of ghosting. With multiple walkers, route traversal uses discrete next cells to make reservations meaningful; single-walker safe long-line smoothing remains available. EditMode test added for a stationary blocker physically vacating a cell. An interrupted midway step remains conservatively occupied until a safe restart, not teleported or silently unlocked.

**Important limits / not yet done:** This is NOT the full planned Gate D or overall Gate E. Static module A* still has no dynamic occupancy-based reroute; agents may wait indefinitely when a route needs replanning or head-on yielding/backtracking. No bounded multi-step time-window reservations, starvation-prevention, slot-claim ownership integration, #112 Needs/autonomy arbitration, complete #114 ship↔shore scene transit, or OS-level mouse/IMGUI button validation. The new Unity G2/G3 source code must actually compile and run on Unity 6000.6.0f1 before calling G2/G3 PASS. The previously proven 28/28 Unity tests apply **only** to the older source HEAD `0c775bed7402e5294f22bb3bc0176a49d58bfb1f`.

Next: run full Unity EditMode and PlayMode suites, fix any actual compiler/test regressions, then implement bounded door-queue yielding/replanning with a reproducible two-way corridor stress fixture before claiming completion of #113.

### Incremental occupied-cell detour follow-up (same unverified Unity branch)

`ShipSpatialMap.FindPath(start, goal, temporarilyBlocked)` now accepts transient blocked crew cells without mutating layout geometry or persistent claims. The pathfinder applies those blockers to cardinal moves, diagonal corner checks, portals and destination feasibility. A waiting walker asks `ShipTrafficCoordinator.BlockedCellsExcept(this)` for current/next cells and attempts a **bounded 0.75s** detour; no A* per frame. Added G1 dynamic blocker / corner / occupied destination tests and G2 stationary-crew detour test. This does **not** implement full reverse-direction door arbitration, priority aging, backing out or deadlock recovery; those stay open in Gate D.

Source validation after the first G1 path changes: automatic Core suite 297/297; after the new dynamic occupancy path test the exact-head run must be checked again. None of these GitHub checks compile or run Unity-specific tests.
