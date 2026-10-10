# FrostArk #26 — Modular Ship Core Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a deterministic, authoritative Pure-C# module layout with paid construction, damage, crew-powered repair, thermal coupling and backward-compatible save v6.

**Architecture:** `WorldState.Layout` owns immutable modules/blueprints/compartment footprints. The existing `WorldCommandBus` changes `Layout`, `Inventory` and `CrewDuty` in one revision; the current Crew-before-Heat scheduler finishes paid repairs and updates efficiency without a second world clock. `SaveCodec` appends a validated v6 layout section after v5 Crew. Unity uses a future thin projection, not scene-owned domain truth.

**Tech Stack:** C# / netstandard2.1 FrostArk.Core source shared with Unity, NUnit + .NET 8 hosted G1, Python G0, GitHub Project #5, existing read-only `.github/workflows/core-checks.yml`.

**Spec:** `docs/superpowers/specs/2026-10-10-modular-ship-design.md` (approved 2026-10-10). **Issue:** #26. **Canonical PR:** [#57](https://github.com/MohamedXIV/Frostark/pull/57) (Draft). **Branch:** `feat/26-modular-ship-layout`.

## Global Constraints

- Re-read live `main`, PR #57 head, Project #5 and native dependencies before each exact-head write; #23/#24/#25 were closed at planning time. Use this branch, never create a competing one.
- No UnityEngine references inside Core; every new `Assets/` source file needs matching unique `.meta`. No local-PC changes, ShellWarden, secrets or privileged workflow.
- Existing immutable-on-commit `WorldState`, command `ExpectedRevision`, `WorldClock`, Crew→Heat ordering and indexed binary `FARKSAVE` header/checksum must remain authoritative.
- ShipHold stock is the *only* construction/repair material source. No fictitious spawn, no independent manager clock, no uncommitted partial mutation.
- Production shape may preserve unresolved physical layout: **G2 Unity import/EditMode, G3 PlayMode/scenes, and G4 real platform runs NOT RUN**. These are not proven by Python/NUnit. Do not close the entire #26 physical-layout issue without a separate acceptance decision.
- Existing **TDD RED already recorded**: head `ab14bba903b2d944b247c76c0f09f14ca3799245`, [Actions #38043991612](https://github.com/MohamedXIV/Frostark/actions/runs/38043991612), Python G0 7/7 PASS and G1 missing-symbol compiler failures. Current docs-only head at plan creation: `d81025cc12b2497da301688c3b78c8ff51906397`. Intermediate builds may stay RED until all existing test-referenced APIs exist: document observed failure, never label an intermediate commit GREEN.

## Review Focus

- A forged/resumed repair job using an existing module ID cannot restore integrity without evidence of an earlier **paid reservation** — Tasks 2–4 persist a module-level `RepairReserved` flag. This flag is set only by a successful parts-consuming command and cleared on completed work; a naked CrewState job never heals an unreserved module.
1. Untrusted slot origin near `int.MaxValue` cannot overflow past a room boundary — Task 1 `OutOfBoundsAndOverflowingOriginsAreRejected`.
2. Duplicate blueprint/room/module IDs or a missing blueprint/room cannot enter canonical state — Task 1 `InvalidAuthoredLayoutRejectsDuplicatesAndMissingReferences`.
3. Rejected build/repair for insufficient parts cannot mutate inventory, queue or revision — Task 2 `NoPartsAndDuplicateCommandsAreAtomic`.
4. Damaging a fully destroyed module, or dispatching its only engineer, cannot produce hidden fuel/repair work — Task 3 `ZeroIntegrityAndAwayRepairerDoNotProgressFreeWork`.
5. Crafted v6 payload with valid recalculated checksum but overlapping footprints or malformed enum fails safe; old v1–v5 saves remain loadable — Task 4 `MalformedSignedOnlyByChecksumCannotLoad` and legacy tests.

---

### Task 1: Canonical module geometry and projection

**Files:**
- Create `Assets/FrostArk/Core/Ship/ShipModule.cs` + `.meta` — value objects, enum, validation
- Create `Assets/FrostArk/Core/Ship/ShipLayoutState.cs` + `.meta` — immutable canonical geometry, fingerprint and efficiency
- Modify `Assets/FrostArk/Core/World/WorldState.cs` — add `Layout` property, optional constructor/factory parameter, snapshot-preserving copy paths, fingerprint
- Test `tests/FrostArk.Core.Tests/ShipModuleWorldTests.cs` (already RED) and create `tests/FrostArk.Core.Tests/ShipLayoutContractTests.cs`

**Interfaces:**
- `enum ShipModuleKind { Engine = 0, Boiler = 1, Auxiliary = 2 }`
- `ShipCompartmentSlot(EntityId id, int width, int height)`
- `ShipModuleBlueprint(EntityId id, ShipModuleKind kind, int width, int height, EntityId partId, long buildCost, long repairCost)`
- `ShipModule(EntityId id, EntityId blueprintId, EntityId roomId, int x, int y, int integrityPermille = 1000, bool repairReserved = false)`; `RepairReserved` is persisted and guards paid module repair completion.
- `ShipLayoutState.Create(IEnumerable<ShipCompartmentSlot> rooms, IEnumerable<ShipModuleBlueprint> blueprints, IEnumerable<ShipModule> modules)`, `Empty`, `GetModule(EntityId)`, `GetBlueprint(EntityId)`, `CanPlace(EntityId, EntityId, EntityId, int, int)`, `Place(...)`, `Damage(EntityId,int)`, `Restore(EntityId)`, `PropulsionPermille`, `BoilerOutputPermille`, `Fingerprint()`.
- `WorldState.Create(..., CrewState? crewDuty = null, ShipLayoutState? layout = null)` and `WorldState.Layout`; preserve `Layout` in every existing `new WorldState(...)` and restore/copy path, without changing existing command semantics.
- Geometry contract: room/blueprint width/height in **1–64**; placement uses `x >= 0 && x <= room.Width - type.Width` (likewise Y), *not* unchecked `x + width`. Reject duplicate IDs, null definitions, invalid enum, unknown references, and rectangle overlap; normalized snapshots sorted by ordinal ID. For no authored Engine/Boiler, efficiency is 1000. With an Engine and Boiler, `BoilerOutputPermille = min(engineIntegrity, boilerIntegrity)` (minimum integrity for duplicates of a kind, if allowed).
- `ShipLayoutState.Empty` fingerprint is stable and included in `WorldState.Fingerprint()`; legacy worlds start with the empty layout.

- [ ] **Step 1 — tests first:** extend `ShipModuleWorldTests.BuildConsumesRealShipPartsAndRejectsCollisionWithoutRevision` with footprint-edge and duplicate-ID assertions; add `ShipLayoutContractTests.InvalidAuthoredLayoutRejectsDuplicatesAndMissingReferences`, `OutOfBoundsAndOverflowingOriginsAreRejected`, `EmptyLayoutKeepsLegacyFullEfficiency`, `StableOrderGivesSameFingerprint`.
- [ ] **Step 2 — RED:** use `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --no-restore -c Release --filter "FullyQualifiedName~ShipLayoutContractTests"`; expected missing new types at first. In connector-only runtime, observe the next PR-triggered hosted G1 logs after tests commit; do not pretend local dotnet executed.
- [ ] **Step 3 — GREEN implementation:** implement declared domain types with constructors and defensive immutable collection views. Wire optional Layout through `WorldState` constructor, `CopyWithCommand`, `WithCrewTick`, `WithThermalMinute`, `AtMinute`, `RestoreSnapshot` and canonical fingerprint. Commit with matching metadata.
- [ ] **Step 4 — check:** build/test with the same command and full G1 suite; if earlier command tests are still unresolved, record precise missing symbols rather than call whole suite green.
- [ ] **Step 5 — commit:** `feat(ship): immutable validated module layout and canonical WorldState projection`.

### Task 2: Inventory-backed build/damage/repair commands

**Files:**
- Modify `Assets/FrostArk/Core/Inventory/InventoryState.cs` — `TryConsumeShip`
- Modify `Assets/FrostArk/Core/Commands/WorldCommands.cs`, `CommandFailure.cs`, `WorldCommandBus.cs`
- Modify `Assets/FrostArk/Core/Events/WorldEvent.cs`
- Modify `Assets/FrostArk/Core/Crew/CrewState.cs` — append a bounded repair job once
- Modify `Assets/FrostArk/Core/World/WorldState.cs` — atomic command-copy helper
- Test `tests/FrostArk.Core.Tests/ShipModuleWorldTests.cs` (existing 3 command scenarios), add `ShipModuleCommandTests.cs`

**Interfaces:**
- `InventoryState.TryConsumeShip(EntityId itemId, long quantity): InventoryTransactionResult` decrements only ShipHold; rejects unknown items, nonpositive quantity, insufficient stock; no mutation.
- `BuildShipModuleCommand(EntityId id, EntityId moduleId, EntityId blueprintId, EntityId roomId, int x, int y, long expectedRevision)`
- `DamageShipModuleCommand(EntityId id, EntityId moduleId, int damagePermille, long expectedRevision)`
- `StartShipModuleRepairCommand(EntityId id, EntityId moduleId, long expectedRevision)`
- Command failure reasons include `InvalidPlacement`, `UnknownBlueprint`, `UnknownModule`, `InsufficientMaterials`, `ModuleNotDamaged`, `ModuleAlreadyRepairing`; events include `ShipModuleBuilt`, `ShipModuleDamaged`, `ShipModuleRepairStarted`. Append numeric enum values; don't renumber v1–v5 reasons.
- `WorldState.WithShipLayoutChange(ShipLayoutState layout, InventoryState inventory, CrewState duty, EntityId commandId)` calls `CopyWithCommand` once; all rejected commands return **the exact original reference** with zero events.
- `CrewState.EnqueueRepair(EntityId moduleId, long remainingWork)` fails on duplicate ID; use **500 work units** per repair job as explicitly candidate initial balance, charged **once** using blueprint `RepairCost`. The same atomic command also records `ShipModule.RepairReserved = true` (with no other public path to set it); clear only on paid job completion. Reject pre-existing arbitrary/legacy jobs with the same module ID without spending again.

- [ ] **Step 1 — tests first:** add `ShipModuleCommandTests.NoPartsAndDuplicateCommandsAreAtomic`, `RejectsUnknownBlueprintAndInvalidPlacementWithoutRevision`, `ZeroOrNegativeDamageRejected`, `SecondRepairCannotChargeTwice`, `CostsConsumeOnlyShipHold`, `StaleRevisionAndDuplicateCommandIdsReject`. Preserve existing expected stock counts from `ShipModuleWorldTests`.
- [ ] **Step 2 — RED:** run `dotnet test ... --no-restore -c Release --filter "FullyQualifiedName~ShipModuleCommandTests"`, observe compile/test failures for unimplemented APIs; hosted runner alternative as above.
- [ ] **Step 3 — GREEN implementation:** implement `TryConsumeShip` then 3 commands, their state-copy path/events and repair enqueue; verify balance and state identity for every rejection.
- [ ] **Step 4 — check:** filtered tests and full G1; inspect actual output. No Unity work.
- [ ] **Step 5 — commit:** `feat(ship): validated paid build, damage and repair commands`.

### Task 3: Crew-driven module repair and thermal consequences

**Files:**
- Modify `Assets/FrostArk/Core/World/WorldState.cs` — `WithCrewTick` reconciliation
- Modify `Assets/FrostArk/Core/Crew/ShipCrewMinuteSystem.cs` only if existing callback contract requires it
- Modify `Assets/FrostArk/Core/Ship/ShipThermalMinuteSystem.cs` — apply module efficiency
- Test `tests/FrostArk.Core.Tests/ShipModuleWorldTests.cs`; add `tests/FrostArk.Core.Tests/ShipModuleSimulationTests.cs`

**Interfaces:**
- Crew-phase reconciler obtains `nextCrew = CrewDuty.Tick(minute)`; when an authorized paid module-ID repair job disappears after its last work minute, restore its existing `Layout` module to integrity 1000 **only if its persisted `RepairReserved` flag was set by a paid command** **inside the same canonical world commit**. Legacy jobs whose ID matches no existing module only close their own work ledger, not resurrect a module.
- `ShipThermalMinuteSystem.Tick(WorldMinute minute)` scales existing crew engineering permille by `Layout.BoilerOutputPermille` once, using safe integer arithmetic, before `ShipThermalModel.Step`; legacy Empty layout leaves original thermal behavior untouched. Publish propulsion projection but do not invent a navigation runtime.
- A command may not queue an unpaid module job. Queue progress obeys existing Crew shift/away and global sleep interruption, not a new timer. Preserve one `WorldState.Minute` authority and no extra revision for simulation ticks.

- [ ] **Step 1 — tests first:** add `ShipModuleSimulationTests.ZeroIntegrityAndAwayRepairerDoNotProgressFreeWork`, `ExistingLegacyRepairJobDoesNotRestoreNonModule`, `BoilerHeatUsesCrewAndDamagedModuleEfficiencyExactlyOnce`, `InterruptedSleepDoesNotApplyPostHazardRepairs`; existing `DamagedEngineReducesHeatAndPropulsionAndStaffedPaidRepairRestoresBoth` is the primary RED acceptance.
- [ ] **Step 2 — RED:** run `dotnet test ... --no-restore -c Release --filter "FullyQualifiedName~ShipModuleSimulationTests|FullyQualifiedName~ShipModuleWorldTests"`; observe expected fail before implementing repair/heat integration.
- [ ] **Step 3 — GREEN implementation:** apply completion reconciliation at Crew phase boundary and deterministic efficiency at Heat phase boundary; preserve earlier `CrewWorldIntegrationTests` 315-minute interrupted sleep semantics.
- [ ] **Step 4 — check:** filtered and full G1 tests, including existing Ship thermal/crew tests. Commit only after reading results.
- [ ] **Step 5 — commit:** `feat(ship): couple paid crew repair completion and engine/boiler heat`.

### Task 4: Canonical Save v6, hostile-input rejection and full migrations

**Files:**
- Create `Assets/FrostArk/Core/Save/SaveCodecLayout.cs` + `.meta`
- Modify `Assets/FrostArk/Core/Save/SaveCodec.cs` — version/append/restore/validation
- Modify `tests/FrostArk.Core.Tests/SaveRoundTripTests.cs` — v1–v3 offsets
- Modify `tests/FrostArk.Core.Tests/CrewWorldIntegrationTests.cs` — v4 migration fixture
- Modify `tests/FrostArk.Core.Tests/ShipModuleWorldTests.cs` — v5 migration + mid-job replay
- Create `tests/FrostArk.Core.Tests/ShipModuleSaveTests.cs` — malformed payload, checksum, stable ordering

**Interfaces:**
- `SaveCodec.CurrentVersion = 6`, `WriteLayout(BinaryWriter, ShipLayoutState)`, `ReadLayout(BinaryReader): ShipLayoutState`, read v1–v5 to `ShipLayoutState.Empty`. `WriteLayout` appends **three Int32 count fields** (rooms, blueprints, modules) even for empty layout: old v5 payload length difference = **12 bytes**. All count/UTF-8 limits use existing SaveCodec helpers.
- Encode rooms as `(id,width,height)`, blueprints as `(id,byte kind,width,height,partId,long buildCost,long repairCost)`, modules as `(id,blueprintId,roomId,x,y,integrity,byte repairReserved)` (strict 0/1) in stable ordinal order. Validate every `ShipLayoutState.Create` invariant on read, regardless of the SHA-256 checksum. Don't change earlier binary field ordering.
- Legacy fixture length deltas relative to empty-layout v6: v1 **96** bytes, v2 **92**, v3 **48**, v4 **20** (v5 8-byte empty crew plus v6 12-byte layout), v5 **12**; confirm from actual byte layout/fixtures, not blindly from comments. Existing v1–v4 tests must keep passing.
- `WorldState.RestoreSnapshot(..., ShipLayoutState? layout = null)` reconstructs validated layout as canonical truth; `Fingerprint()` and snapshot binary bytes match continuous 120-minute run versus 31+89-minute resume.

- [ ] **Step 1 — tests first:** retain existing `ShipModuleWorldTests.SaveResumeDuringPaidRepairMatchesUninterruptedBytes` and `V5SaveMigratesToEmptyLayoutWithoutInventingShipModules`; add `ShipModuleSaveTests.NonEmptyLayoutRoundTripsCanonicalBytes`, `MalformedSignedOnlyByChecksumCannotLoad`, `OverflowedOrTruncatedLayoutCannotLoad`, `UnsortedValidInputsNormalizeDeterministically`, `UnpaidLegacyJobCannotHealMatchingModule`; update v1–v4 migration test expectations only alongside explicit new v6 fixture assertions.
- [ ] **Step 2 — RED:** run `dotnet test ... --no-restore -c Release --filter "FullyQualifiedName~ShipModuleSaveTests|FullyQualifiedName~SaveRoundTripTests|FullyQualifiedName~CrewWorldIntegrationTests"` and observe expected v5-to-v6 failures/missing serializer.
- [ ] **Step 3 — GREEN implementation:** add separate layout codec file, v6 append/decode and WorldState restore; fix measured downgrade offsets and catch malformed payloads as `InvalidData` without mutating live state.
- [ ] **Step 4 — check:** run filtered and then full G1; verify v1–v5 and v6 roundtrips, duplicate and checksum-forgery cases, byte identity.
- [ ] **Step 5 — commit:** `feat(save): persist ship layout v6 and migrate v1-v5 without inventing state`.

### Task 5: Scope review, documentation and exact-head handoff

**Files:**
- Modify `docs/INDEX.md` only if a new ADR is added
- Create `docs/engineering/ship-modules-contract.md` — behavioral contract and true gate status
- Possibly modify `docs/production/decisions.md` for 500-work balance decision; do not claim global adoption beyond #26
- Modify [PR #57](https://github.com/MohamedXIV/Frostark/pull/57) description/comment and Project #5 issue #26 evidence via connected tools

**Interfaces:** No new runtime API.

- [ ] **Step 1 — evidence:** inspect exact PR head + merge base and native dependency graph; compare touched files for Unity metadata GUID uniqueness, Core-only imports and expected cross-system boundaries.
- [ ] **Step 2 — hosted checks:** for the final *code-changing* head, inspect the GitHub Actions workflow `core-checks.yml` **run + job logs**, matching exact head SHA. G0 command is `python3 tools/validate_core_structure.py && python3 -m unittest discover -s tests/validation -p 'test_*.py' -v`. G1 commands are `dotnet restore tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --nologo` and `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj --no-restore -c Release --logger "console;verbosity=normal"`. Record count and actual PASS/FAIL; don't infer from GitGuardian.
- [ ] **Step 3 — review:** request whole-branch review of correctness, supply conservation, stale commands, recurring repair and v1–v5 schema. Fix important findings with new failing tests then inspect fresh hosted G0/G1 on the resulting SHA.
- [ ] **Step 4 — docs:** write the Core ADR with precise test numbers and **G2/G3/G4 NOT RUN**, plus the unresolved Unity scene layout and platform-storage adapter boundaries; don't claim real in-scene module placement.
- [ ] **Step 5 — finalize:** use RepoPilot exact-head prepare_merge and Project #5 evidence. If scoped headless Core is independently accepted, merge via authorized SHA-guarded path after review; if full Issue #26 scene acceptance remains missing, retain Issue #26 In progress and PR Draft (or create explicitly tracked follow-up) rather than marking physical layout Done. Handoff includes PR URL, exact SHA, files, G0/G1 run/job evidence, what is NOT RUN and next dependency-ready task.

## Plan self-review

**Coverage map:** geometry/projection Task 1; paid commands and rejection Task 2; crew/thermal/sleep Task 3; canonical serialization and historical migrations Task 4; scope/Unity boundary and code review Task 5.

**Dependency ordering:** Types + WorldState → inventory/commands → crew/thermal → codec → review. Reuse existing RED fixture; when a task's production contract becomes compilable, add a new focused RED case if necessary before implementation. Do not rewrite tests just to make them green.

**Out of scope:** Unity deck/scene mesh mapping, actual engine propulsion, PlayMode physics, real device/IL2CPP proof, WebGL atomic platform storage, medical outcomes and simulated spare-parts fabrication. Remain honest about G2–G4.
