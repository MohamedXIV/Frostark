# World Clock and Deterministic Scheduler — Issue #16

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans or subagent-driven-development. Preserve test-first red/green evidence and exact CI SHA.

**Goal:** Build a pure-C# authoritative minute scheduler with deterministic phase ordering, scheduled hazards and interruptible sleeping, without simulated physics frames.

**Architecture:** Keep `WorldMinute` in existing `FrostArk.Core.World`. Add `FrostArk.Core.Time` types under `Assets/FrostArk/Core/Time` (compiled by existing Unity asmdef and the exact same standalone csproj source glob). Only `SimulationScheduler` may advance `WorldClock`; system callbacks are ordered by phase then ordinal stable key. Scheduled event order on ties is stable/seeded by `StableHash32`, then ID. `Advance` and `AdvanceUntil` both traverse reference minute steps; batching optimizations are deferred until proved equivalent.

**Tech stack:** Unity-compatible C# 8 / netstandard2.1 source, NUnit 3 standalone net8 test project, existing GitHub Actions G0/G1 workflow.

**Spec:** `docs/engineering/time-simulation.md`, `docs/engineering/architecture.md`, Issue #16.

## Global constraints
- Current Unity Editor version 6000.6.0f1; no engine references in Core.
- Preserve `WorldMinute`, `EntityId` and `StableHash32` contracts; no new external packages.
- No accelerated Unity physics, no time input from system clock or frame count.
- Serialize/save design belongs #18; integrated WorldState command mutation belongs #17.
- GitHub Actions authorized for public repo; restrict workflow to existing read-only G0/G1.
- G2 Unity/PlayMode/build results remain NOT RUN until actually executed.

## Review focus
- Sleep from world minute 1320 (22:00) to 1800 (06:00 next day): hazard at 1635 (03:15) interrupts exactly at 1635, no further simulation/fuel.
- Same phase and stable key ordering on every run regardless of registration order, reject duplicates.
- Same-minute events should have reproducible seeded tie ordering regardless of scheduling insertion sequence.
- Negative, zero, backward and overflow-minute inputs should not corrupt clock state.
- Per-minute stepping 60×1 must equal a 1×60 batch under identical seed and systems; isolated from presentation FPS.

### Task 1: RED — specify the public API through tests

**Files:** Create `tests/FrostArk.Core.Tests/TimeSchedulerTests.cs`.

**Interfaces under test:** `WorldClock(WorldMinute start)` with `Current`; `SimulationScheduler(WorldClock, IEnumerable<IWorldMinuteSystem>, uint seed=0)` with `Schedule(ScheduledWorldEvent)`, `Advance(long)`, `AdvanceUntil(WorldMinute, SleepInterruptionPolicy)`; `IWorldMinuteSystem.Phase/StableKey/Tick`; `ScheduledWorldEvent(EntityId, WorldMinute, string kind, bool interruptsSleep=false)`; `WorldAdvanceResult.WasInterrupted/EndedAt/Interruption/FiredEvents`.

- [ ] Write tests for minute-step equivalence, phase order, seeded event tie order, interrupt at 03:15, invalid scheduling and time arithmetic.
- [ ] Create Draft PR and observe expected GitHub Actions compile FAIL due to missing scheduler types; capture run URL and error.
- [ ] Preserve RED proof in PR and checkpoint.

### Task 2: GREEN — implement minimal scheduler

**Files:** Create `Assets/FrostArk/Core/Time.meta`, `Assets/FrostArk/Core/Time/*.cs` and their `.meta` pairs.

**Interfaces:** Implement exactly the tested API; no speculative WorldState or physics clocks.

- [ ] Add minute clock with checked internal stepping.
- [ ] Sort systems by ordered simulation phase and `StringComparer.Ordinal` key.
- [ ] Process each minute's systems, then due scheduled events ordered by seeded stable hash and ordinal ID.
- [ ] Stop at first flagged hazard event under SleepInterruptionPolicy, after this minute's systems.
- [ ] Run workflow and obtain G0 7/7 and all NUnit tests PASS.
- [ ] Confirm G2 Unity remains NOT RUN; code review against issue and intended persistent data contracts.

### Task 3: Review and integrate

- [ ] Check actual GitHub head, jobs, logs and test totals; review patches and boundaries; re-run if amended.
- [ ] Only merge on exact-head with green checks. Update #16 evidence and Project status; next issue #17.
