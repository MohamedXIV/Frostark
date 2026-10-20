# GOAP 3.1.2 / Unity 6 — opt-in import and integration spike

**Status:** EXPERIMENT SETUP ONLY; Unity package import, EditMode tests, PlayMode behavior and target builds **NOT RUN**. Do not merge this branch as proof of GOAP adoption.

**Engineering issue:** [#96](https://github.com/MohamedXIV/Frostark/issues/96).  
**Current design authority:** [D09 crew agency/needs](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097), [D10 GOAP evaluation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875098). Keep *game-design decisions in Miro*, not here.

## Changes on this experimental branch

1. `Packages/manifest.json` pins `com.crashkonijn.goap` to the upstream UPM source `https://github.com/crashkonijn/GOAP.git?path=/Package#3.1.2`.
2. The existing FrostArk Editor **test** assembly alone references the upstream `CrashKonijn.Goap.Runtime` and `CrashKonijn.Agent.Runtime` assemblies; the core world domain and production Unity assembly do not acquire new references.
3. `GoapPackageAssemblySmokeTests` statically checks that V3 still supplies *separate* `GoapActionProvider` and `AgentBehaviour` Unity components. It **does not** create a real plan or perform actions.

These assembly names and the pin were verified against the upstream GOAP 3.1.2 package and demo sources on 2026-10-20. The upstream package advertises a Unity 2022.3 minimum and a dependency on `com.unity.collections` 1.2.4; **actual package resolution with our Unity 6 and transitive versions remains unverified**.

## Verification ladder — required before adoption or merge

| Gate | Requirement | Current |
| --- | --- | --- |
| G0 | Manifest JSON, exact official tagged URL, assembly references, .meta, and scope | Source-inspected; changes uncompiled |
| G1 | Headless pure .NET domain regressions (if/when adapters touch WorldState) | NOT RUN for this spike |
| G2 | Exact Unity 6 Editor imports UPM + resolves Unity.Collections + compiles and runs this Editor NUnit test | **NOT RUN** |
| G3 | Actual GOAP-backed PlayMode behaviors, transitions, cancel/replan, persistence | **NOT RUN** |
| G4 | Desktop/mobile/WebGL performance and platform compatibility | **NOT RUN** |

Do not hand-edit `Packages/packages-lock.json`: regenerate via an actual verified Unity Package Manager import and commit the resolved lockfile **on the branch**. Report the resolved package graph, import warnings/errors and exact Unity version. If import cannot resolve, collect UPM diagnostics; keep the main branch unaffected.

## First executable check

Use an authorized Unity Editor with the project open at this branch. Run the following **EditMode test** through the Unity Test Runner, or an equivalent licensed Editor command:

```text
FrostArk.Unity.Tests.GoapPackageAssemblySmokeTests.GoapV3ExposesDistinctPlannerAndExecutorComponents
```

This is a **package API import smoke only**. A PASS proves that those upstream public types compile/resolve, *not* that planning, Unity 6 performance, offscreen persistence or Needs/Smart Interactions work.

### Runtime fixture to implement after G2

Keep first gameplay prototype narrow; don't silently install GOAP as the final AI architecture:

1. **World snapshot / sensors:** read one known crew member's authoritative need/duty state (or a deliberate temporary read-only fixture with clearly fake values), publish GOAP world-key facts. Don't create an independent hidden hunger clock.
2. **Goal:** a severe hunger signal requests food while the crew member is staffing the engine. A small selector decides **when** to request the goal; GOAP discovers **how** to achieve it.
3. **Actions:** propose acquire/eat, work/handover/resume. Validate available interaction slot and acquire/release a temporary claim; cancellation and already-reserved slots must fail safely. On completion only a validated WorldCommandBus/Core action commits any resource/need/work changes.
4. **Tactical planner counterexample:** armor threat with ineffective current rifle and a better viable alternative; compare novice/cautious versus experienced/reckless input. Both option costs and perception must come from explicit supplied state; no magic omniscience.
5. **Trace:** log `current goal -> plan/action choices -> condition/rejection -> resulting validated Core event`. Assert no double reservation, duplicate item consumption, or free offscreen progress.
6. **Compare:** briefly compare implementation/debugging complexity against a small utility selector plus FSM. If the GOAP integration is cumbersome, document reasons and *reject* the package rather than design an enormous adapter framework.

The version 3 package distinguishes `GoapActionProvider` (chooses plan/action) from `AgentBehaviour` (runs action); it includes Reactive, Proactive and Manual controllers. See [upstream Getting Started](https://github.com/crashkonijn/GOAP/blob/master/Package/Documentation/Introduction/GettingStarted.md). Its own documentation notes GOAP effects describe planning relationships, **not actual world mutations**; actual effects must be applied by game code. FrostArk's authoritative changes still happen through the shared world session.

## Known external runtime blocker

The separate [draft Codespaces Unity runner PR #95](https://github.com/MohamedXIV/Frostark/pull/95) has not yet established reliable G2/G3: headless Unity Editor startup encountered an entitlement/license error after earlier shared-memory and Linux GL dependency blockers. Do not report this spike as tested just because its code was committed, a dotnet test passed, or a Unity package README claims compatibility.

## Exit and decision

Leave the spike as DRAFT until there is actual Unity G2 and G3 evidence. Capture exact HEAD, commands, test XML/logs, package-lock resolution, obstacles and performance notes in the engineering Issue/PR. Post **ADOPT / ADAPT / REJECT** as a design decision to Miro D10 after proof; don't create an additional living design record in GitHub.
