# E11 #112 — Crew agency vs player orders (first executable slice)

**Status:** implementation IN PROGRESS on [Draft PR #119](https://github.com/MohamedXIV/Frostark/pull/119), stacked on Draft #116, **not merged**. Unity G2/G3 newly authored tests are **NOT YET RUN**.  
**Design authority:** [Miro D09 Crew Agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097), D03 Smart Interactions; issue [#112](https://github.com/MohamedXIV/Frostark/issues/112).

## Already authoritative — not duplicated

`WorldSession`, `WorldState.CrewDuty`, `CrewInteractionState`, `WorldClock`, `CrewProvisionInteractionDiscovery`, and the existing `ClaimCrewInteractionCommand`, `UseCrewProvisionInteractionCommand` and `ReleaseCrewInteractionCommand` continue to own condition, stock, WorldRevision, serializable slots, expiry and consumption. `TacticalSelection` remains transient presentation state; `ShipGridWalker` remains ship-local movement. No imported GOAP or parallel calendar.

## New first implementation

- `CrewAgencyPolicy.Evaluate(world, crewId, selected, activeOrder)` is a read-only deterministic priority decision. Known four-person groups remain selected/held without inventing work; accepted player moves continue after deselection. While idle and unselected, a member with real Hunger/Thirst severity >=650 may seek an actual stocked, unclaimed, authored Eat/Drink slot; severity >=950 may request a **safe-boundary emergency handover** even when selected or under an accepted Move order. These two thresholds are provisional engineering tuning. **1000 = critically hungry/thirsty, not full.** A need with no provision is explicitly unsatisfied, never a fabricated item.
- `ShipGridWalker.RequestStopAtSafeCell()` stops immediately only at a cell center; when physically between cells it finishes the already-reserved incoming edge and releases only on arrival. No teleport and no interrupt through a bulkhead.
- `ShipCrewAgencyCoordinator` attaches through the existing `ShipTacticalSceneBootstrap` and listens to accepted Move intents from `TacticalCommandController.OrderIssued`. Tracks which actor has an accepted physical player move independently of selected/highlighted status; a deselect never stops the walking order. After physical completion, reevaluates needs. Real provision fulfillment is **Claim → validated ship-local Move → physically arrive at authored slot → Use** through `WorldSession.Submit`; an interrupted/failed activity releases the actual claim when safe, and stocks cannot change just because a movement order was issued.
- Slot physical address is derived only if the slot `SourceId` is a *placed canonical ship module*. Arbitrary fixture-only source IDs are not accepted as movement/arrival proof. Missing stock, invalid map, unreachable slot, lost claim and crew absence never create a successful fake consume.
- `ShipTacticalSceneBootstrap.RebuildNavigation()` updates the agency's navigation reference after door/layout changes.

## Tests authored, required verification

- G1: `CrewAgencyPolicyTests` (5 cases): four selected/unselected, move continuing after deselect, emergency stocked vs empty, severity thresholds, away/incapacitated crew. Real Core CI must be checked at exact head.
- G2: `ShipCrewAgencyEditModeTests` (4 cases): selected restraint, real provision claim/use after a completed player move, safe-cell cancellation and claim release, emergency handover and no fabricated stock.
- G3: added one `ShipRtsDemoScenePlayModeTests` check using the actual checked-in demo, verifying no autonomy theft from a selected actor and deselect-mid-move completes.
- G0 guards Unity 6 explicit `EntityId` alias, physical theater import, GUIDs, Core/Unity wiring. A green G0/G1 **never** proves Unity compilation or G2/G3.

## Remaining #112 acceptance (do not mark issue complete)

- **Persistent/restoreable player-order lifecycle and source assignments**: this first slice tracks accepted physical Move in a *scene-local* transient dictionary. It does not save consequential pending orders or reconstruct them across scene transit/reentry. Actual SaveCodec/WorldState design is a separate #112 P0 task before closing the issue.
- **Actual duty/work autonomy:** when idle and no urgent physical provision, `ReadyForDuty` marks eligibility but does not invent an authored Work/Rest position or run an unsupported GOAP engine. The existing canonical minute-level `CrewState.Staff` continues independently. Physical work/bed routes and duty/handover remain to integrate.
- **Emergency and new-command priority tuning:** emergency interrupts an accepted Move at a safe cell. New manual order may be rejected when a walker is physically mid-edge; queuing/replay of that player's new order after stop is not implemented yet. Conflict resolution with wounds/combat/shore and claim cleanup across scene destruction still needs G2/G3.
- **Unity runtime evidence:** execute newly authored Editor and PlayMode suites in Unity 6000.6.0f1 on the exact branch head, fix real failures, then prove a real provision interaction on stage with saved/restored stocks and revised room portals.
- **G4 desktop/device and actual OS mouse/HUD buttons** remain outside this batch.

## Next dependency-ready implementation

Run the actual Unity G2/G3 suite. Once green, add **canonical pending player-order persistence and reentry reconciliation**, then authored duty/Rest/Work targets. Keep parent #116 and other stack PRs in Draft until independent scene acceptance gates are complete.
