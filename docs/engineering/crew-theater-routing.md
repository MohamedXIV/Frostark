# E11 / #109 — crew movement theater routing handoff

**Design authority:** [Miro D01 ship-local navigation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187) and [D09 character agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). **Engineering owner:** [Issue #109](https://github.com/MohamedXIV/Frostark/issues/109). This records technical implementation and test gates, not an alternate design specification.

## Current unmerged increment

- `CrewTheaterMoveRouter.Plan` takes a canonical `WorldState`, `PhysicalSceneCheckpoint` and existing `TacticalOrderIntent.Move`, classifying **every actor** in stable ordinal ID order. `CrewDuty.IsAway` identifies shore vs ship presence; on shore, the actor must additionally belong to the current checkpoint's `PartyIds`. Invalid/unavailable crew and inactive theater fail explicitly. A missing checkpoint (older saves/fixture worlds) never fabricates a ship.
- `CrewTheaterMoveDispatcher.Dispatch` uses a typed `CrewMoveGoal.ShipCellGoal(Vector2Int)` or `ShorePoint(Vector3)` and one matching `ICrewTheaterMoveExecutor` per stable actor+theater. It returns an **individual outcome**: accepted dispatch, route rejected with reason, missing/duplicate scene proxy or executor rejection. It does not assume an accepted path has completed, consume resources, alter `WorldSession` or create a persistent phantom move order.
- `ShoreNavMeshCrewExecutor` delegates to the already-authored `TacticalActorProxy.MoveTo`. The *future* real ship executor must accept a **ship-local grid cell** and use layout-derived A* (#110); shore does not inherit the ship grid. This contract deliberately does NOT adapt strategic sea-ship travel.
- **7 new Core NUnit cases** cover mixed ship/shore/unknown, shore manifest, inactive theater, legacy location, incapacitation, save/restore and invalid inputs. **7 new Unity EditMode cases authored** verify dispatch partitioning, typed coordinates, duplicate/missing proxy safety, executor rejection, legacy checkpoint, invalid world points and shore proxy delegation.

## Deliberately NOT delivered yet

- Integration into the production `TacticalCommandController` currently changed on unmerged [PR #116](https://github.com/MohamedXIV/Frostark/pull/116). Do not merge competing changes over that work; integrate after the canonical input branch is validated. Existing independent shore click commands still function as before.
- A real `ICrewTheaterMoveExecutor` for ship is blocked on #110's **layout-derived** walkability, and the Galley [PR #107](https://github.com/MohamedXIV/Frostark/pull/107) fixture is only a known **single-actor 10×8** A* demonstration, not canonical layout truth.
- Scene transit / cancellation while traveling (#22), persisted player-order intent (#112), group traffic/smoothing (#113), and 2-theater PlayMode proof (#114). `CrewDuty.IsAway` discriminates shipboard vs *away*, not an exact island: the physical checkpoint/party is therefore required to avoid accepting off-scene shore actors.
- **G2/G3 Unity import, Editor tests and PlayMode NOT RUN** by this authoring pass; G1 source tests need exact-head CI evidence. No visual/gameplay readiness claim.

## Exit the Draft safely

1. Validate G0 structure + G1 Core NUnit on the actual PR head (Core tests cannot prove Unity runtime correctness).
2. In Unity 6000.6.0f1, import and run EditMode `CrewTheaterMoveDispatchEditModeTests`; inspect the compiler and NUnit results. Add G3 tests for both real movement providers and scene reentry once #110 and #116 are integrated.
3. Keep #109 open until actual same-input ship+shore movement dispatch and individual rejection feedback are exercised and verified in real Unity.

