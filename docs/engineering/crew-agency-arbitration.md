# E11 #112 — Crew agency vs player orders (first executable slice)

**Status:** implementation IN PROGRESS on [Draft PR #119](https://github.com/MohamedXIV/Frostark/pull/119), stacked on Draft #116, **not merged**. Local G1/G2/G3 verification ran on 2026-10-22 (G2/G3 on Unity 6000.6.0f1); targeted agency and demo suites pass, with full-suite isolation failures documented below.
**Design authority:** [Miro D09 Crew Agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097), D03 Smart Interactions; issue [#112](https://github.com/MohamedXIV/Frostark/issues/112).

## Already authoritative — not duplicated

`WorldSession`, `WorldState.CrewDuty`, `CrewInteractionState`, `WorldClock`, `CrewProvisionInteractionDiscovery`, and the existing `ClaimCrewInteractionCommand`, `UseCrewProvisionInteractionCommand` and `ReleaseCrewInteractionCommand` continue to own condition, stock, WorldRevision, serializable slots, expiry and consumption. `TacticalSelection` remains transient presentation state; `ShipGridWalker` remains ship-local movement. No imported GOAP or parallel calendar.

## New first implementation

- `CrewAgencyPolicy.Evaluate(world, crewId, selected, activeOrder)` is a read-only deterministic priority decision. Known four-person groups remain selected/held without inventing work; accepted player moves continue after deselection. While idle and unselected, a member with real Hunger/Thirst severity >=650 may seek an actual stocked, unclaimed, authored Eat/Drink slot; severity >=950 may request a **safe-boundary emergency handover** even when selected or under an accepted Move order. These two thresholds are provisional engineering tuning. **1000 = critically hungry/thirsty, not full.** A need with no provision is explicitly unsatisfied, never a fabricated item.
- `ShipGridWalker.RequestStopAtSafeCell()` stops immediately only at a cell center; when physically between cells it finishes the already-reserved incoming edge and releases only on arrival. No teleport and no interrupt through a bulkhead.
- `ShipCrewAgencyCoordinator` attaches through the existing `ShipTacticalSceneBootstrap` and listens to accepted Move intents from `TacticalCommandController.OrderIssued`. Tracks which actor has an accepted physical player move independently of selected/highlighted status; a deselect never stops the walking order. An accepted player order supersedes an autonomous provision and releases its claim without stopping the newly accepted route. After physical completion, reevaluates needs. Real provision fulfillment is **Claim → validated ship-local Move → physically arrive at authored slot → Use** through `WorldSession.Submit`; an interrupted/failed activity releases the actual claim when safe, and stocks cannot change just because a movement order was issued.
- Slot physical address is derived only if the slot `SourceId` is a *placed canonical ship module*. Arbitrary fixture-only source IDs are not accepted as movement/arrival proof. Missing stock, invalid map, unreachable slot, lost claim and crew absence never create a successful fake consume.
- `ShipTacticalSceneBootstrap.RebuildNavigation()` updates the agency's navigation reference after door/layout changes. The duplicate early `agency` local in `Initialize` was removed; the coordinator `SetMap` call now lives solely in the rebuild path, preserving the prior rebind point without the CS0128 compile failure.

## Test evidence and remaining verification

Local verification on 2026-10-22, Unity 6000.6.0f1:

- G1: `CrewAgencyPolicyTests` (5 cases) passed; full `FrostArk.Core.Tests` passed 303/303.
- G2: `ShipCrewAgencyEditModeTests` (6 cases) passed 6/6. Coverage includes selected restraint, real provision claim/use after a completed player move, safe-cell cancellation and claim release, emergency stocked vs empty, accepted player move superseding a claim, and empty-stock critical need.
- G3: `ShipRtsDemoScenePlayModeTests` passed 2/2 against the checked-in demo, including selecting and moving across rooms and deselection mid-move.
- Manual `ShipRtsDemo` QA: selected-move accepted and advancing; deselect did not cancel the accepted walk; closing a bulkhead mid-edge stopped at a safe boundary with `RouteInvalidated`, no canonical revision change, and no fake consumption. Screenshot captured at `Temp/pipeline-screenshots/crew-agency-manual.png`.
- Full EditMode: 72/73 passed. `PackageSmokeTests.AddressablesLoadsAndReleasesConfiguredTextAsset` timed out at 180 seconds; this is unrelated to the agency slice and remains unisolated.
- Full PlayMode: 25/26 passed. `CombatPlayModeTests.Test05_ArmorMitigation_MitigatesBallisticDamageInRealPhysics` took double damage (expected 875, got 750) in the full suite but passed alone after a forced reload; order-dependent test isolation remains unresolved.
- No claim is made that the full suite is green or that desktop/device, real OS mouse/HUD activation, or on-stage provision interaction with saved stock has been proven.

## Remaining #112 acceptance (do not mark issue complete)

- **Persistent/restoreable player-order lifecycle and source assignments**: this first slice tracks accepted physical Move in a *scene-local* transient dictionary. It does not save consequential pending orders or reconstruct them across scene transit/reentry. Actual SaveCodec/WorldState design is a separate #112 P0 task before closing the issue.
- **Actual duty/work autonomy:** when idle and no urgent physical provision, `ReadyForDuty` marks eligibility but does not invent an authored Work/Rest position or run an unsupported GOAP engine. The existing canonical minute-level `CrewState.Staff` continues independently. Physical work/bed routes and duty/handover remain to integrate.
- **Emergency and new-command priority tuning:** emergency interrupts an accepted Move at a safe cell. New manual order may be rejected when a walker is physically mid-edge; queuing/replay of that player's new order after stop is not implemented yet. Conflict resolution with wounds/combat/shore and claim cleanup across scene destruction still needs G2/G3.
- **On-stage provision proof:** the demo scene has no authored need, stocked interaction, or provision route, so real claim/use was covered by the G2 fixture; a scene-level provision with saved/restored stocks and revised room portals is still outstanding.
- **G4 desktop/device and actual OS mouse/HUD buttons** remain outside this batch. Synthetic pointer injection did not select a crew member in manual QA; direct controller evaluation was used for the movement checks.

## Next dependency-ready implementation

Resolve the unrelated full-suite isolation gates (Addressables smoke timeout and Combat Test05 order dependence), repeat G2/G3 at the exact committed head, then add **canonical pending player-order persistence and reentry reconciliation**, followed by authored duty/Rest/Work targets. Keep parent #116 and other stack PRs in Draft until independent scene acceptance gates are complete.


## 2026-10-22 — full-suite armor test timing follow-up (new Unity result pending)

On exact source `57a811423bd8fa164fb732a00a2ab8a4a0cf6497`, the Unity local agent reported **6/6** agency G2 tests, **2/2** checked-in demo G3 tests, **72/73** full EditMode (Addressables editor build smoke timed out at 180 seconds), and **25/26** full PlayMode (`CombatPlayModeTests.Test05`: expected 875 health but saw 750, passed separately). The agent reported Unity compilation with no errors and no main/merge changes.

Inspected source of `CombatPlayModeTests.Test05`, `TacticalActorProxy.AttackTarget`, `TacticalActorProxy.UpdateCombat` and `CombatTickRunner.FixedUpdate`: a direct clear-range attack executes its FIRST ballistic shot synchronously. The old test then waited 0.25 seconds, during which the independent 10 Hz combat-tick runner and auto-fire logic may legitimately fire a SECOND (also armor-mitigated) shot. Health 750 means two 125-damage shots; the test conflated first-shot armor mitigation with subsequent weapon cadence.

The next code commit changes **only the PlayMode test** to assert immediately after the synchronous accepted attack before FixedUpdate advances the cooldown; it retains exact 125 damage and 875 health assertions, uses the same real physics raycast and does NOT weaken combat/game logic or the assertion. **This specific follow-up has NOT yet been run in Unity**. Run `CombatPlayModeTests.Test05` alone, then full 26 PlayMode tests after a proper domain reload; verify full suite genuinely reaches 26/26. If it still fails, collect actual output rather than modifying gameplay without evidence.

`PackageSmokeTests.AddressablesLoadsAndReleasesConfiguredTextAsset` uses a `BuildScriptFastMode.BuildData` call and awaits Addressables initialization/load. Its reported **180s timeout is an unresolved test-runner/environment/content build failure**; no cause has yet been established and it has NOT been fixed. Capture which stage hangs and address independently after a clean Editor/domain reload. Do not count either full suite as PASS until confirmed.

#112 still requires saved pending orders/reentry, real staged provision and duty/work autonomy. The code-level original QA is valid but not a full release gate.
