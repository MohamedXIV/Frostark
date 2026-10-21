# E11 / #109 — crew movement theater routing handoff

**Design authority:** [Miro D01 ship-local navigation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187) and [D09 character agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). **Engineering owner:** [Issue #109](https://github.com/MohamedXIV/Frostark/issues/109). This records technical implementation and test gates, not an alternate design specification.

## Current unmerged increment

- `CrewTheaterMoveRouter.Plan` takes a canonical `WorldState`, `PhysicalSceneCheckpoint` and existing `TacticalOrderIntent.Move`, classifying **every actor** in stable ordinal ID order. `CrewDuty.IsAway` identifies shore vs ship presence; on shore, the actor must additionally belong to the current checkpoint's `PartyIds`. Invalid/unavailable crew and inactive theater fail explicitly. A missing checkpoint (older saves/fixture worlds) never fabricates a ship.
- `CrewTheaterMoveDispatcher.Dispatch` uses a typed `CrewMoveGoal.ShipCellGoal(ShipGridCell)` or `ShorePoint(Vector3)` and one matching `ICrewTheaterMoveExecutor` per stable actor+theater. It returns an **individual outcome**: accepted dispatch, route rejected with reason, missing/duplicate/stale-scene proxy or executor rejection. It does not assume an accepted path has completed, consume resources, alter `WorldSession` or create a persistent phantom move order.
- `ShoreNavMeshCrewExecutor` delegates to the already-authored `TacticalActorProxy.MoveTo`. Every executor carries an authored `SceneId` and cannot receive commands if it belongs to an unloaded or previous scene, even if the actor ID and theater match. `ShipGridCrewExecutor` now delegates real ship movement to the `ShipGridWalker` from #110, using a room-qualified `ShipGridCell(RoomId,X,Y)`; shore does not inherit the ship grid. This contract deliberately does NOT adapt strategic sea-ship travel.
- **7 new Core NUnit cases** cover mixed ship/shore/unknown, shore manifest, inactive theater, legacy location, incapacitation, save/restore and invalid inputs. **8 new Unity EditMode cases authored** verify dispatch partitioning, typed coordinates, duplicate/missing proxy safety, executor rejection, legacy checkpoint, invalid world points, shore proxy delegation and stale proxy rejection after scene changes.

## Deliberately NOT delivered yet

- Integration into the production `TacticalCommandController` currently changed on unmerged [PR #116](https://github.com/MohamedXIV/Frostark/pull/116). Do not merge competing changes over that work; integrate after the canonical input branch is validated. Existing independent shore click commands still function as before.
- A real `ICrewTheaterMoveExecutor` for ship is now included in this stacked branch, but production click input and Unity G2/G3 validation remain open. The Galley [PR #107](https://github.com/MohamedXIV/Frostark/pull/107) fixture is only a known **single-actor 10×8** A* demonstration, not canonical layout truth.
- Scene transit / cancellation while traveling (#22), persisted player-order intent (#112), group traffic/smoothing (#113), and 2-theater PlayMode proof (#114). `CrewDuty.IsAway` discriminates shipboard vs *away*, not an exact island: the physical checkpoint/party is therefore required to avoid accepting off-scene shore actors.
- **G2/G3 Unity import, Editor tests and PlayMode NOT RUN** by this authoring pass; G0/G1 Core-only CI success does not establish Unity compilation or gameplay. No visual/gameplay readiness claim.

## Exit the Draft safely

1. Validate G0 structure + G1 Core NUnit on the actual PR head (Core tests cannot prove Unity runtime correctness).
2. In Unity 6000.6.0f1, import and run EditMode `CrewTheaterMoveDispatchEditModeTests`; inspect the compiler and NUnit results. Add G3 tests for both real movement providers and scene reentry once #110 and #116 are integrated.
3. Keep #109 open until actual same-input ship+shore movement dispatch and individual rejection feedback are exercised and verified in real Unity.


## 2026-10-21 stacked #109/#110 integration

- PR #117 is a real merge descendant of Draft PR #118 and its base is now `feat/110-layout-spatial-route`: it includes the canonical layout-derived map without duplicating it in its PR diff. **Keep PR #118 ahead of #117 when merging.** PR #116's box selection remains separately Draft and is not merged.
- Actual ship movement now routes through `ShipGridCrewExecutor` and #110's `ShipGridWalker.TryMoveTo`. The goal carries `ShipGridCell(RoomId, X, Y)` instead of ambiguous roomless XY. Missing/stale/duplicate actor proxies remain per-actor explicit failures. Two workers can receive one request across the same authored door without touching world inventory or clock.
- `TacticalTheaterSelectionCommands` reuses `TacticalSelectionController` directly, with the genuine camera-resolved clicked-world point for `TacticalOrderIntent` and a separate typed physical movement goal. Blank deselection clears selection, **not an already accepted player move**. This is a callable scene integration seam, not a substitute for the right-click/drag-box event handler on PR #116.
- Added **5** new Unity EditMode tests on real `ShipGridWalker` GameObjects: 2-room 2-actor motion, blocked-door mixed outcomes, stale/disabled proxy, no cancellation on deselection, and the same transient selection on shore. Added **1** G3 PlayMode test using real Unity Update frames under a moving ship root. **None of these new Unity tests has been run**. Core CI is distinct evidence.
- Still pending before production readiness: actual scene/prefab authoring and Unity compiler/editor execution; shared live mouse input link with PR #116; scene reentry/route cancellation and autonomous needs (#112); positioning, formations, congestion and collisions (#113); full dual-theater acceptance (#114). Avoid treating the programmatic PlayMode fixture as a shipped playable scene.
