# E11 #111 — RTS primary selection and shore context input (incremental handoff)

Canonical design records: [Miro D01](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187), [D02](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188), [D09](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). This is engineering status, NOT an alternative game-design source.

## Shipped to this unmerged branch (authored; Unity unverified)

- The existing shore `TacticalCommandController` handles left-click actor selection, Shift toggling, drag-box selection (up to four), and empty-click/empty-box clearing. The pointer's selection hit must be the front-most physical object; the box additionally performs an occlusion ray.
- A right-click on a shore hostile / interactable / ground issues the respective existing `TacticalActorProxy` NavMesh/tactical command, without adding a new shore path planner or moving ship grid onto land.
- Clearing selection changes only `TacticalSelectionController`'s transient IDs; previously accepted NavMesh movement remains in-flight.
- Group Move keeps per-actor dispatch acceptance in `LastMoveDispatch`, and an intent lists only actors whose NavMesh executor accepted the dispatch. Acceptance is **not** verified arrival.
- EventSystem UI guards prevent starting world gestures over uGUI. The mouse drag rectangle is visual-only; no new gameplay simulation state or save format.
- Existing test harness gains 1 EditMode partial-dispatch test and 3 PlayMode selection/move/deselect tests. They are **authored only** until run in Unity 6000.6.0f1.

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
