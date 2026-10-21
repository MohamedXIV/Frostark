# E11 #111 — RTS primary selection and shore context input (incremental handoff)

Canonical design records: [Miro D01](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187), [D02](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188), [D09](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). This is engineering status, NOT an alternative game-design source.

## Shipped to this unmerged branch (authored; Unity unverified)

- The existing shore `TacticalCommandController` handles left-click actor selection, Shift toggling, drag-box selection (up to four), and empty-click/empty-box clearing. The pointer's selection hit must be the front-most physical object; the box additionally performs an occlusion ray.
- A right-click on a **first-hit, unobstructed** shore hostile / interactable / ground issues the respective existing `TacticalActorProxy` NavMesh/tactical command, without adding a new shore path planner or moving ship grid onto land.
- Clearing selection changes only `TacticalSelectionController`'s transient IDs; previously accepted NavMesh movement remains in-flight.
- Group Move keeps per-actor dispatch acceptance in `LastMoveDispatch`, and an intent lists only actors whose NavMesh executor accepted the dispatch. Acceptance is **not** verified arrival.
- EventSystem UI guards (with an explicit `UnityEngine.UI` assembly reference) prevent starting or releasing world gestures over uGUI. The mouse drag rectangle is visual-only; no new gameplay simulation state or save format.
- Existing test harness gains 1 EditMode partial-dispatch test and 4 PlayMode selection/move/deselect/occlusion tests. They are **authored only** until run in Unity 6000.6.0f1.

## Pending gates / intentionally not claimed

- Real Unity CLI package compile, EditMode and PlayMode runs; currently NOT RUN for this branch. Also verify keyboard/mouse, camera pan conflict and the known unfocused Input System test #8 before approval.
- The ship Galley fixture in Draft PR #107 remains separate and still has its prior per-character `Resume Auto` behavior. It needs a separate pass to adopt transient selection and outstanding-order/autonomy arbitration (#112). Do **not** merge this shore input PR assuming ship parity.
- Real ship layout-based pathfinding #110, theater router #109, production locomotion #113 and final two-domain proof #114 still OPEN. No new saved crew orders or ship+shore synchronization implemented here.
- Further improvements: constrained context-ground raycasting, individual interaction rejection feedback, visual input accessibility, group NavMesh formations/avoidance (#32) and cross-theater identity.

## Test commands for an authorized local Unity 6000.6.0f1 environment

Use the official Unity CLI installed with the project; confirm its exact supported invocation via `unity --help` before relying on commands:

```powershell
unity --version
unity pipeline list
unity test . --mode EditMode --filter "TacticalSelectionBridgeTests"
unity test . --mode PlayMode --filter "TacticalPlayModeTests"
```

Capture actual NUnit XML/logs and record tested branch HEAD. The full PlayMode suite previously had an unrelated reported Input System timing failure and is not green on this branch by inference.

Issue #111 is a **partial implementation**, not complete until ship/shore input parity is proven.


## 2026-10-21 — stacked physical ship + shore pointer implementation

- This Draft PR #116 now targets Draft PR #117 (which targets #118), preserving one dependency path instead of competing copies. A two-parent merge commit combined the already-reviewed canonical #109/#110 source with this branch's own tactical input changes.
- The existing `TacticalCommandController` now selects NavMesh-free `ShipCrewSelectionProxy` components by visible left-click/rectangle and handles explicit right-click on `ShipRoomClickSurface`. The floor resolves a camera ray hit to a **room-typed** `ShipGridCell(RoomId,X,Y)` under the ship transform; if a room/scene map is missing, ship clicks fail closed. Context clicks on a character do not teleport/issue an implicit movement command.
- `IssueShipMoveOrder` and authoritative shore checkpoint movement call `TacticalTheaterSelectionCommands` / `CrewTheaterMoveDispatcher`. Per-actor acceptance and detailed rejection are exposed via `LastMoveDispatch` and `LastTheaterMoveDispatch`. Only accepted crew appear in `OrderIssued`, and deselection does not revoke accepted movement. Shore scenes without a physical checkpoint keep the previous fixture behavior; shore scenes *with* a checkpoint use canonical away/party checks.
- `ShipTacticalSceneBootstrap` is opt-in scene authoring with explicit root, room origins, floor colliders, portal endpoints and already spawned crew. It derives the actual `ShipSpatialMap` from the scoped `WorldSession.Layout` rather than static blocked-cell arrays, configures the walkers, and automatically rebuilds after a committed layout fingerprint change. An authored fresh-run ship checkpoint may be enabled, but `WorldSessionScope` applies it **only when no save is being restored**. Null legacy checkpoint is never replaced during restore.
- **6 authored Unity EditMode tests** now cover physical box/right-click commands, closed-door per-crew acceptance, missing bootstrap fail-closed, immediate invalidation on door change, a committed module build invalidating an accepted route, and explicit new-run checkpoint authoring. These tests were **NOT RUN** in Unity. This does not substitute for runtime scene authoring or verified PlayMode.
- Remaining: actual Unity G2 import/test execution + G3 player-input/session scene run, physically authored prefab/scene with crew selection markers, restored re-entry positions, ongoing autonomy and claim arbitration, realistic group traffic/formation, runtime shore scene identity authoring and final multi-theater acceptance (#112–#114). Do not claim production readiness or merge solely on Core CI.
