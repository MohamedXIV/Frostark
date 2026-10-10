# FrostArk #33 — Sea Navigation Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship deterministic headless polar route choices, real fuel and hull consequences, command provenance and save v7, ready for later Unity helm/ice integration.

**Architecture:** Authored graph of stable ID waypoints/legs and a persistent `ShipNavigationState` owned solely by `WorldState.Navigation`. Revision-guarded planning commands use the existing `WorldCommandBus`. `ShipTravelMinuteSystem` ticks in the existing `SimulationPhase.Travel=50` after `Heat=30`, spending only remaining canonical fuel without advancing its own clock.

**Tech Stack:** Unity-compatible pure C# (netstandard2.1), existing .NET 8 NUnit suite, Python G0 and approved read-only `.github/workflows/core-checks.yml`.

**Spec:** `docs/superpowers/specs/2026-10-10-sea-navigation-core-design.md` — accepted for planning on 2026-10-10. **Issue:** #33 (P1). **Branch:** `feat/33-navigation-core`. **Main baseline:** `38fa1e18ce72cd6f69b43b1da0bba6e65ae8b575`.

## Global Constraints

- Follow `AGENTS.md`: this is a full-game subsystem, not a disposable sample. Do not install frameworks, touch the user's local Unity files or introduce another authoritatively mutable state.
- Use `WorldState.GetSupply(EntityId.Parse("supply:fuel"))`, `WorldState.Layout.PropulsionPermille`, `SimulationPhase.Crew=10`, `Heat=30`, `Travel=50` and revisioned commands. Heat commits the authoritative minute before Travel, so Travel requires `world.Minute == minute`; no second WorldClock or fuel stock.
- D019 world-time rate and D022 world/map scale remain OPEN. Coordinate bound ±1,000,000 *authoring units*, leg length 1..1,000,000, condition permille 0..1000 and movement rates in the plan are tuning data for deterministic fixtures—not canon.
- Empty nav for v1–v6 saves is **inactive and unlocated** (no fabricated reinforced hull). Authored maps must explicitly establish start location and ship hull. Save v7 append cannot reinterpret older fields or event IDs.
- One new `.meta` with unique GUID per C# file in `Assets/`. No UnityEngine in Core. Re-read exact GitHub main, dependency graph, existing PRs and branch head before writing. No parallel competing PRs.
- G0 Python and actual .NET 8 NUnit G1 only. Unity G2 Editor, G3 PlayMode/real-space ice/helm, G4 platform/device builds remain **NOT RUN** until separate evidence; create/reuse a scoped Unity issue before Core-only closure.

## Review Focus — failure classes

1. **Heat burns the last fuel unit:** `TravelStopsIfHeatConsumedRequiredFuel` must prevent partial navigation debit, displacement or free hull wear (Task 3).
2. **Engine power 1/1000 rounds effective speed to zero:** `TinyEnginePowerNeverCreatesFreeTravel` must not force minimum displacement (Task 3).
3. **Disconnected, repeated or mid-leg replacement route:** `InvalidRouteRejectsAtomically` preserves exact prior state reference, no event/revision (Task 2).
4. **Forged payload with recomputed checksum and impossible progress/hull:** `ChecksummedInvalidNavigationRejected` fails InvalidData (Task 4).
5. **Travel registered without clock-committing Heat system:** `TravelCannotAdvanceWorldClock` rejects miswiring rather than stepping time itself (Task 3).

---

### Task 1 — Canonical graph and immutable navigation world state

**Files:** Create `Assets/FrostArk/Core/Ship/SeaRouteMap.cs`, `ShipNavigationState.cs` and matching `.meta`. Modify `Assets/FrostArk/Core/World/WorldState.cs`. Test `tests/FrostArk.Core.Tests/SeaRouteModelTests.cs`.

**Interfaces:**
- `SeaWaypoint(EntityId id,int x,int y)`; x/y integer ±1,000,000.
- `SeaLeg(EntityId id,EntityId fromId,EntityId toId,int distanceUnits,int icePermille,int stormPermille,int visibilityPermille)`; distinct valid endpoint IDs; distance 1..1,000,000; environment 0..1000.
- `NavigationTuning(int speedUnitsPerMinute,int fuelPerUnit,int iceDragPermille,int stormDragPermille,int safeIcePermille,int iceDamagePerMinute,int stormDamagePerMinute)`. Positive bounded speed/fuel; other params 0..1000.
- `ShipNavigationState.Empty` inactive and unlocated; `ShipNavigationState.Create(IEnumerable<SeaWaypoint> waypoints,IEnumerable<SeaLeg> legs,EntityId currentWaypoint,NavigationTuning tuning,int hullIntegrityPermille)` requires valid current waypoint and hull 1..1000, sorted graph definitions, no duplicates, no dangling leg refs; immutable arrays and read-only projections.
- `ShipNavigationState`: `CurrentWaypoint`, `HullIntegrityPermille`, `ActiveLegIds`, `ActiveLegIndex`, `ProgressUnits`, `IsActive`, `Fingerprint()`. `WorldState.Navigation` optional last parameter `navigation = null` in `Create` and `RestoreSnapshot`; propagate through every `new WorldState`, command-copy, Crew/Heat ticks, `AtMinute` and fingerprint.

- [ ] **Step 1: Add RED tests** `SeaRouteModelTests.RejectsDuplicateWaypointsAndDanglingLegs`, `RejectsOutOfRangeCoordinatesAndWeather`, `EmptyMigrationDoesNotInventHull`, `CanonicalOrderHasEqualFingerprint`, `WorldCopyPathsRetainNavigation`. Assert exact values and original state unchanged.
- [ ] **Step 2: Run RED:** `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj -c Release --filter FullyQualifiedName~SeaRouteModelTests`. Without local dotnet, commit test-first to canonical branch/PR and inspect actual hosted G1 error; distinguish missing-symbol compile RED from behavior RED.
- [ ] **Step 3: Implement** interfaces above with defensive immutable collections and bounded arithmetic (checked long for coordinates).
- [ ] **Step 4: Run GREEN** same filter plus entire NUnit G1 and Python G0 at current head; report any still-unimplemented later symbols without claiming overall GREEN.
- [ ] **Step 5: Commit** `feat(nav): canonical route map and WorldState identity`.

### Task 2 — Revisioned route planning and cancellation

**Files:** Modify `Assets/FrostArk/Core/Commands/WorldCommands.cs`, `WorldCommandBus.cs`, `CommandFailure.cs`, `Assets/FrostArk/Core/Events/WorldEvent.cs`, `Ship/ShipNavigationState.cs` and `World/WorldState.cs`. Test `tests/FrostArk.Core.Tests/ShipRouteCommandTests.cs`.

**Interfaces:**
- `PlanShipRouteCommand(EntityId id,IReadOnlyList<EntityId> legIds,long expectedRevision)` defensive-copies route IDs. Requires nonempty chain, matching current-waypoint origin, connected directed edges, no repeats, no unknown IDs; replacement only when currently at a waypoint, never while mid-leg.
- `CancelShipRouteCommand(EntityId id,long expectedRevision)` allows cancellation only at a waypoint and when a route exists. `ShipNavigationState.PlanRoute(...)` / `CancelRoute()` return immutable state. `WorldState.WithNavigationCommand(ShipNavigationState next,EntityId commandId)` commits exactly one revision and stores provenance.
- Append failures `InvalidRoute`, `NavigationUnavailable`, `CannotRerouteMidLeg` and events `ShipRoutePlanned`, `ShipRouteCancelled` without renumbering prior enum values. Every rejection uses identical source state and 0 events.

- [ ] **Step 1: Tests-first** `ShipRouteCommandTests.InvalidRouteRejectsAtomically`, `StaleAndDuplicateReject`, `RouteReplacementAtWaypointWorks`, `CannotCancelMidLegOrTeleport`.
- [ ] **Step 2: Observe RED** `dotnet test ... -c Release --filter FullyQualifiedName~ShipRouteCommandTests` (real filtered test or hosted run).
- [ ] **Step 3: Implement** guarded commands, route validation and immutable single-revision transitions.
- [ ] **Step 4: Verify GREEN** focused filter then full G1, check inventory/module regressions.
- [ ] **Step 5: Commit** `feat(nav): connected route commands with provenance`.

### Task 3 — Fuel-conserving Travel tick with ice/storm consequences

**Files:** Create `Assets/FrostArk/Core/Ship/ShipTravelMinuteSystem.cs` and `.meta`. Modify `ShipNavigationState.cs` and `WorldState.cs`. Test `tests/FrostArk.Core.Tests/ShipTravelIntegrationTests.cs`.

**Interfaces:**
- `ShipTravelMinuteSystem(Func<WorldState> read,Action<WorldState> commit) : IWorldMinuteSystem` has `Phase=Travel`, `StableKey="ship:travel"` and `LastTick`. It requires `world.Minute == tickMinute`, because Heat already committed this minute.
- `ShipNavigationState.Step(int propulsionPermille,long availableFuel): NavigationTickResult` pure calculation. Effective progress = floor(`speedUnitsPerMinute * propulsion /1000`), then ice multiplier `(1000 - iceDragPermille * icePermille /1000)/1000`, then storm multiplier likewise; clamp to remaining leg. Use checked long intermediates, never force 1 unit if rounded 0.
- For nonzero committed movement, required fuel = `movedUnits * fuelPerUnit + ceil(movedUnits * fuelPerUnit * icePermille /1000)`; require all available fuel **after Heat**, else no progress, fuel or damage. Hull damage on a moving minute = `ceil(max(0,icePermille-safeIcePermille)*iceDamagePerMinute/1000) + ceil(stormPermille*stormDamagePerMinute/1000)`; clamp integrity 0..1000 and halt future movement at hull 0. No damage while stationary and no fake event ID.
- `WorldState.WithTravelTick(WorldMinute minute,NavigationTickResult result)` commits both nav state and remaining fuel in one immutable commit without stepping minute or incrementing command revision. Idle nav/zero engine/no fuel preserves route state. Only one headless WorldState pointer shared with the scheduler.

- [ ] **Step 1: Tests-first** `ShipTravelIntegrationTests.ShortIceCutVsDetourDifferentTimeFuelAndHull`, `TravelStopsIfHeatConsumedRequiredFuel`, `TinyEnginePowerNeverCreatesFreeTravel`, `EngineDamageAndPaidRepairChangesSpeed`, `TenOneMinuteStepsMatchSingleTenMinuteAdvance`, `SleepInterruptionStopsTravelAtHazardMinute`, `TravelCannotAdvanceWorldClock`.
- [ ] **Step 2: Observe RED** filtered G1 before implementation.
- [ ] **Step 3: Implement** pure `Step`, Heat-before-Travel adapter, and atomic fuel/nav commit. Test harness always registers existing `ShipThermalMinuteSystem` even for an empty thermal graph.
- [ ] **Step 4: Verify GREEN** targeted and full G1, checking no post-interruption progress.
- [ ] **Step 5: Commit** `feat(nav): heat-first fuel accounting and deterministic ice transit`.

### Task 4 — Append-only Save v7 and migration validation

**Files:** Create `Assets/FrostArk/Core/Save/SaveCodecNavigation.cs` plus `.meta`. Modify `Assets/FrostArk/Core/Save/SaveCodec.cs` (current v6), `World/WorldState.cs`; update `tests/FrostArk.Core.Tests/SaveRoundTripTests.cs`, `CrewWorldIntegrationTests.cs`, `ShipModuleWorldTests.cs` as needed. Test `tests/FrostArk.Core.Tests/NavigationSaveTests.cs`.

**Interfaces:**
- `SaveCodec.CurrentVersion=7`, `WriteNavigation(BinaryWriter writer,ShipNavigationState nav)` and `ReadNavigation(BinaryReader reader): ShipNavigationState` after the existing v6 `WriteLayout/ReadLayout`. Include presence flag, bounded node/leg definitions, tuning, hull, current waypoint, route sequence, progress and index; order graph catalogs by ordinal stable ID, retain *ordered* route IDs.
- `version < 7` loads `ShipNavigationState.Empty` without fabrication. Do not reorder legacy payloads. Update legacy empty-nav byte offsets by measurement, not guessed constants.
- Strictly reject duplicate IDs, invalid references/ranges/direction/route index/progress, corrupted/truncated bytes, forged checksum with bad semantic state; checksum only proves incidental corruption.

- [ ] **Step 1: RED tests** `NavigationSaveTests.ActiveVoyageRoundTripByteIdentical`, `InterruptedSaveResumeMatchesContinuousWorld`, `LegacyV1ThroughV6StayInactiveUnlocated`, `ChecksummedInvalidNavigationRejected`, `BadLengthAndTruncationReject`.
- [ ] **Step 2: Observe RED** filtered NUnit and record expected actual failures.
- [ ] **Step 3: Implement** bounded v7 serializer/hydrator and measured v1–v6 migration fixtures.
- [ ] **Step 4: GREEN** full G1 + G0 from *exact final code head*; no Unity assertions.
- [ ] **Step 5: Commit** `feat(save): v7 navigation replay and strict legacy migrations`.

### Task 5 — Docs, review, downstream Unity acceptance and integration

**Files:** Create `docs/engineering/navigation-core-contract.md`; update `docs/INDEX.md`, Issue #33 and Project #5 through connectors; create/reuse downstream issue for Unity helm, ice visuals and platform validation when Core is ready.

- [ ] **Step 1: Self-review** full spec vs code, actual main+PR head and dependency graph. Confirm no second clock, duplicate fuel ownership, UnityEngine imports, duplicate `.meta` GUIDs, or invented D019/D022 decisions.
- [ ] **Step 2: Verify exact-head CI logs**: `python3 tools/validate_core_structure.py` and `python3 -m unittest discover -s tests/validation -p 'test_*.py' -v` (G0); `dotnet restore tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --nologo` and `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --no-restore -c Release --logger "console;verbosity=normal"` (G1). Record PASS/FAIL counts and run/job IDs; never reuse stale GREEN.
- [ ] **Step 3: Review** command atomicity and engine/hull/fuel invariants, forged saves, 10x1 equivalence, interrupted sleep, true platform boundary; use new failing tests to fix important findings.
- [ ] **Step 4: Document** authored tuning, limitation that navigation is strategic domain (not Unity ship physics); track actual Unity helm/input, visible ice/weather, G2/G3/G4 in a separate clearly-open issue.
- [ ] **Step 5: Integrate** only after exact-head RepoPilot `prepare_merge` readiness and applicable code review. Core-only completion of #33 must link still-open Unity evidence issue; otherwise keep #33 OPEN. Include full test evidence and remaining scope in handoff.

## Self-review / approval

The spec's graph, command, time/ice, Save and Unity handoff requirements map to Tasks 1–5 respectively. Existing `SimulationPhase.Travel` can be reused; no speculative new world clock. Review Focus lists five explicit adverse inputs and owning tests. Type names and method signatures are consistent across tasks. The planned fixtures do **not** settle world-time scale D019 or map scale D022.

**Status:** Plan proposal, not code execution. Next stage: user's review of this written plan, then Native execution (already preferred in prior FrostArk task) with test-first commits and real hosted verification.
