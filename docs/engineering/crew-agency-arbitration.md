# E11 #112 — Crew agency vs player orders (first executable slice)

**Status:** implementation IN PROGRESS on [Draft PR #119](https://github.com/MohamedXIV/Frostark/pull/119), stacked on Draft #116, **not merged**. Local G1/G2/G3 verification ran on 2026-10-22 (G2/G3 on Unity 6000.6.0f1); targeted agency/demo suites and both full local suites pass at this working tree. G4 desktop/device and real OS input remain unverified.
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
- Full EditMode: 73/73 passed, including the Addressables smoke test twice in the same Editor session. Full PlayMode: 26/26 passed after a clean domain reload, including `CombatPlayModeTests.Test05_ArmorMitigation_MitigatesBallisticDamageInRealPhysics` and `TacticalPlayModeTests.Test08_RealInputSystem_PointerClickInjected_DispatchesThroughUpdate`.
- No claim is made that desktop/device, real OS mouse/HUD activation, or on-stage provision interaction with saved stock has been proven.

## Remaining #112 acceptance (do not mark issue complete)

- **Persistent/restoreable player-order lifecycle and source assignments**: this first slice tracks accepted physical Move in a *scene-local* transient dictionary. It does not save consequential pending orders or reconstruct them across scene transit/reentry. Actual SaveCodec/WorldState design is a separate #112 P0 task before closing the issue.
- **Actual duty/work autonomy:** when idle and no urgent physical provision, `ReadyForDuty` marks eligibility but does not invent an authored Work/Rest position or run an unsupported GOAP engine. The existing canonical minute-level `CrewState.Staff` continues independently. Physical work/bed routes and duty/handover remain to integrate.
- **Emergency and new-command priority tuning:** emergency interrupts an accepted Move at a safe cell. New manual order may be rejected when a walker is physically mid-edge; queuing/replay of that player's new order after stop is not implemented yet. Conflict resolution with wounds/combat/shore and claim cleanup across scene destruction still needs G2/G3.
- **On-stage provision proof:** the demo scene has no authored need, stocked interaction, or provision route, so real claim/use was covered by the G2 fixture; a scene-level provision with saved/restored stocks and revised room portals is still outstanding.
- **G4 desktop/device and actual OS mouse/HUD buttons** remain outside this batch. Synthetic pointer injection did not select a crew member in manual QA; direct controller evaluation was used for the movement checks.

## Next dependency-ready implementation

Repeat G1/G2/G3 at the exact committed head after committing the Addressables test fix, then add **canonical pending player-order persistence and reentry reconciliation**, followed by authored duty/Rest/Work targets. Keep parent #116 and other stack PRs in Draft until independent scene acceptance gates are complete.


## 2026-10-22 — full-suite isolation follow-up (resolved)

Earlier on `57a8114` the same suites reported 72/73 EditMode and 25/26 PlayMode. On `0da01cc`, after pulling commit `73d3c70`, the first post-pull `CombatPlayModeTests.Test05` failure was stale compiled test code rather than gameplay: the stack line referenced the old source, and an explicit `unity command recompile` followed by a run after compilation settled passed the test alone. The committed test asserts the first synchronous shot before FixedUpdate advances the 10 Hz auto-fire cooldown; it retains the real physics raycast, exact 125 damage and 875 health assertions. After a clean domain reload, full PlayMode passed **26/26**, including Test05 and the previously order-dependent `TacticalPlayModeTests.Test08`.

`PackageSmokeTests.AddressablesLoadsAndReleasesConfiguredTextAsset` was then isolated with stage timing. `BootstrapSetup` took 72–304ms, `FastModeBuildData` 6–57ms with no error, and `InitializeAsync` 5–44ms with `Succeeded`; the timeout was entirely in `LoadAssetAsync` (56,499ms once, then 179,981ms with `status=None`). Frame diagnostics showed `unscaled=0.01` and `delayedActive=True`. Fast mode's `AssetDatabaseProvider` schedules the read through `DelayedActionManager`, which uses `Time.unscaledTime`; EditMode test time does not advance, so the delayed action never fires.

The fix is test-only in `Assets/Tests/Editor/PackageSmokeTests.cs`: set `settings.SimulatedLoadDelay = 0f` before `BuildData` and restore the previous value in `finally`; after initialization also apply zero delay to the live `AssetDatabaseProvider` so an already-initialized provider cannot retain the frozen-time delay. No Addressables settings asset is saved; only in-memory settings/provider delay is changed for this test. The test also calls `handle.Release()` directly so the local struct is invalidated; `Addressables.Release(handle)` passes the struct by value and cannot invalidate the caller's copy, so the old `handle.IsValid() == false` assertion could never pass. The smoke test passed twice in the same Editor session (0.36s and 0.23s), full EditMode passed **73/73**, and full `FrostArk.Core.Tests` passed **303/303** on the final working tree.

#112 still requires saved pending orders/reentry, real staged provision and duty/work autonomy. The code-level original QA is valid but not a full release gate.
