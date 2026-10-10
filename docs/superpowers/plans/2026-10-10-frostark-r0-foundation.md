# FrostArk R0 Core Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Establish editor-independent, deterministic C# world state and clock, save/restore, and a verified Unity adapter without changing FrostArk's gameplay direction.

**Architecture:** Domain and application are C# without UnityEngine; Unity scenes present projections and submit commands. Ordered world-time updates and explicit DTO saves guarantee consistent cross-system behavior and enable cloud verification.

**Tech Stack:** Unity 6000.6.0f1 + C# + URP, standalone .NET test project using a Unity-compatible subset, Unity Test Framework, optionally VContainer after compatibility check.

**Spec:** [architecture](../../engineering/architecture.md), [time](../../engineering/time-simulation.md), [save content](../../engineering/save-content.md), and [QA gates](../../engineering/validation.md). [Issues F02–A05](../backlog.md) own delivery.

## Global Constraints

- GitHub main is canonical; inspect exact live head and current PRs before branching.
- Do not touch the owner's unpushed local changes or start GitHub Actions.
- Pure-C# domain MUST NOT depend on UnityEngine.
- Record G0/G1 evidence; do not call G2/G3 green without authorized actual Editor/PlayMode run.
- Stay compatible with Unity 6.6's supported C# language/API profile; resolve concrete target framework from verified engine project, not assumptions.
- Scripted scene import and test automation may use Unity CLI after explicit safe permission and licensing; no ShellWarden policy bypass.

## Review Focus

- Save/load half-way through a scheduled hazard → identical outcome to continuous run.
- Same command applied twice or bad ID → reject safely, no extra inventory/crew duty.
- Variable render FPS or system timezone → unchanged deterministic world results.
- Time jump across multiple duties/resources → correct ordered consumption and interruption.
- Session/scene unload/reload → no extra tick listener, event duplication or orphaned entity.

---

### Task 1 — [F02 #14] Cloud verification contract

**Files:** Create `tools/verify-core.md` (document executable commands while runner availability is measured); create `docs/engineering/unity-runner-contract.md`; optionally `tools/verify-core.ps1`/`.sh` once actual commands tested.

**Interfaces:** Inputs: repo checkout, Unity version 6000.6.0f1. Output: repeatable G0/G1 and conditional G2 commands and evidence format.

- [ ] Record actual agent environment .NET SDK and installed Unity runner/tool restrictions.
- [ ] Define exact `dotnet test` command and output shape for the upcoming test project; mark not-yet-created project as pending, not pass.
- [ ] Document authorized `Unity.exe -batchmode -nographics -quit -projectPath ...` import/compile workflow and logs, only after runner permission is granted.
- [ ] Test the available checks and report return codes; do not start GitHub Actions.
- [ ] Commit focused scope and attach verification table to issue #14.

### Task 2 — [A01 #15] Isolated C# assembly with red/green test

**Files:** Create `Assets/FrostArk/Core/FrostArk.Core.asmdef`, `Assets/FrostArk/Core/World/WorldMinute.cs`; create `tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`, `tests/FrostArk.Core.Tests/WorldMinuteTests.cs`; use linked existing Core C# sources in the standalone test project where feasible.

**Interfaces:** `readonly record struct WorldMinute(long Value)` is a *proposed shape*, adjust if Unity profile disallows record structs. Core source must compile independently.

- [ ] Write failing test asserting negative minutes rejected and ordered arithmetic; run `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj` and record failure.
- [ ] Implement minimal minute type and safe arithmetic; test again, record count and exit code.
- [ ] Inspect Unity asmdef references for UnityEngine dependency leaks.
- [ ] Commit implementation with measured G1 and separately verified/pending G2.

### Task 3 — [A03 #17] Single authoritative state and command validation

**Files:** Create `Assets/FrostArk/Core/World/WorldState.cs`, `Commands/IGameCommand.cs`, `Commands/WorldCommandBus.cs`, `Events/WorldEvent.cs`; tests in `tests/FrostArk.Core.Tests/WorldCommandTests.cs`.

**Interfaces:** Proposed `CommandResult Execute(WorldState state, IGameCommand command)`; returns validity, events and updated state via an explicit contract (decide mutable internal state vs immutable snapshots in ADR).

- [ ] Write failing tests for invalid ID, negative resource withdrawal and same crew assigned to two exclusive jobs.
- [ ] Implement validation and atomic state transition plus event provenance.
- [ ] Run isolated tests red/green; commit with reviewable state/command interfaces.

### Task 4 — [A02 #16] Ordered scheduler and sleeping

**Files:** Create `Assets/FrostArk/Core/Time/WorldClock.cs`, `SimulationScheduler.cs`, `ScheduledWorldEvent.cs`, `SleepInterruptionPolicy.cs`; tests `tests/FrostArk.Core.Tests/TimeSchedulerTests.cs`.

**Interfaces:** Proposed `Advance(long minutes)` and `AdvanceUntil(WorldMinute target, IInterruptionPolicy policy)` exposing resulting minute and interrupt reason. No `UnityEngine.Time`.

- [ ] Red tests for 60 × 1 minute vs 1 × 60 minutes when equivalence is promised.
- [ ] Red test for sleep 22:00–06:00 interrupted at 03:15, with fuel/task updates until interrupt only.
- [ ] Implement deterministic schedule ordering and seeded event queue; verify all tests green.
- [ ] Save sample state digest/trace in fixture and commit.

### Task 5 — [A04 #18] Round-trip and migration

**Files:** Create `Assets/FrostArk/Core/Save/SaveGameDto.cs`, `SaveCodec.cs`, `SaveMigrator.cs`, fixture saves in `tests/FrostArk.Core.Tests/Fixtures/`, tests `SaveRoundTripTests.cs`.

**Interfaces:** Proposed `byte[] Encode(SaveGameDto)`, `DecodeResult Decode(ReadOnlySpan<byte>)`; choose JSON/binary only after ADR and runtime compatibility review.

- [ ] Red tests: resume matches continuous digest; older version migrations or errors clearly; invalid/corrupt payload rejected without mutating existing world.
- [ ] Implement smallest explicit versioned codec and snapshot mapping.
- [ ] Run suite and commit before Unity persistence adapter work.

### Task 6 — [A05 #19] Unity runtime and scoped composition

**Files:** Create `Assets/FrostArk/Unity/FrostArk.Unity.asmdef`, `UnityWorldBootstrap.cs`, `WorldTickDriver.cs`, `WorldStatePresenter.cs`; optional VContainer `LifetimeScope`; Unity EditMode test source in `Assets/Tests/EditMode/`.

**Interfaces:** MonoBehaviour tick driver advances only one Core clock from real input, and presenter displays projections. Entry/scene lifetime, pause and script reload are explicit.

- [ ] Write red EditMode/PlayMode scenario for duplicate subscription/scene reload and world ticks.
- [ ] Wire Core application and Unity adapters without introducing a global GameManager singleton.
- [ ] Run actual authorized Unity import/compile/EditMode commands, capture full logs; if blocked, report **pending** and leave gameplay-acceptance open.
- [ ] Verify input/visual/physics remain adapters, commit with exact head and test summary.

## Stop conditions

If .NET compatibility, Unity licensing/permissions or serialization behavior is uncertain, create a bounded evidence issue/ADR rather than invent a platform guarantee. No gameplay PR should claim Unity success from static review alone.
