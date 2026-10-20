# GOAP 3.1.2 / Unity 6 — opt-in import and integration spike

**Status:** GOAP 3.1.2 import and actual code-first planner **fixtures authored**; Unity package import, EditMode test execution, PlayMode behavior and target builds **NOT RUN**. These C# tests are uncompiled hypotheses until the Editor can run. Do not merge this branch as proof of GOAP adoption.

**Engineering issue:** [#96](https://github.com/MohamedXIV/Frostark/issues/96).  
**Current design authority:** [D09 crew agency/needs](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097), [D10 GOAP evaluation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875098). Keep *game-design decisions in Miro*, not here.

## Changes on this experimental branch

1. `Packages/manifest.json` pins `com.crashkonijn.goap` to the upstream UPM source `https://github.com/crashkonijn/GOAP.git?path=/Package#3.1.2`.
2. The existing FrostArk Editor **test** assembly alone references upstream `CrashKonijn.Goap.Runtime`, `CrashKonijn.Agent.Runtime`, `CrashKonijn.Goap.Core` and `CrashKonijn.Agent.Core` assemblies. Core and the production FrostArk.Unity assembly do not acquire new references.
3. `GoapPackageAssemblySmokeTests` checks V3 public class import only.
4. `GoapHungerPlanningEditModeTests` **constructs a real upstream `GoapBehaviour`, ManualController and code-first AgentType/Capability, creates an upstream GOAP agent and requests a real GOAP plan**. Two attachable Unity fixture MonoBehaviours have matching `.cs` names and `.meta` files.
5. An explicit **test-only** sensor source exposes a binary critical-hunger fact and food-availability fact to the planner. The planned `GoapRequestMealAction` records a *command request*, never deducts supplies or authoritatively updates Needs. This is NOT integrated into gameplay and there is no real FrostArk Hunger component yet.

These assembly names and the pin were verified against the upstream GOAP 3.1.2 package and demo sources on 2026-10-20. The upstream package advertises a Unity 2022.3 minimum and a dependency on `com.unity.collections` 1.2.4; **actual package resolution with our Unity 6 and transitive versions remains unverified**.

## Verification ladder — required before adoption or merge

| Gate | Requirement | Current |
| --- | --- | --- |
| G0 | Manifest JSON, exact official tagged URL, assembly references, .meta, and scope | Source-inspected against upstream GitHub **3.1.2 tag**; changes uncompiled |
| G1 | Headless pure .NET domain regressions (if/when adapters touch WorldState) | NOT RUN for this spike |
| G2 | Exact Unity 6 Editor imports UPM + resolves Unity.Collections + compiles and runs assembly + real-planner Editor NUnit tests | **NOT RUN** |
| G3 | Actual GOAP-backed PlayMode behaviors, transitions, cancel/replan, persistence | **NOT RUN** |
| G4 | Desktop/mobile/WebGL performance and platform compatibility | **NOT RUN** |

Do not hand-edit `Packages/packages-lock.json`: regenerate via an actual verified Unity Package Manager import and commit the resolved lockfile **on the branch**. Report the resolved package graph, import warnings/errors and exact Unity version. If import cannot resolve, collect UPM diagnostics; keep the main branch unaffected.

## First executable check

Use an authorized licensed Unity 6000.6.x Editor with the project open on this branch, let Package Manager finish resolving (collect and commit the generated lockfile), then run **EditMode**:

```text
FrostArk.Unity.Tests.GoapPackageAssemblySmokeTests.GoapV3ExposesDistinctPlannerAndExecutorComponents
FrostArk.Unity.Tests.GoapHungerPlanningEditModeTests.CriticalHungerWithFoodResolvesOfficialGoapActionWithoutMutatingNeeds
FrostArk.Unity.Tests.GoapHungerPlanningEditModeTests.CriticalHungerWithNoFoodDoesNotEmitMealCommand
FrostArk.Unity.Tests.GoapHungerPlanningEditModeTests.NoncriticalHungerKeepsExistingDutyInsteadOfRequestingFood
```

These test methods are **authored and not executed**; before publishing any PASS collect NUnit XML plus Unity Editor log and exact Editor/package versions. Their GOAP integration uses a real planner from the official package, and a *clearly fake* Needs/food snapshot: a successful run would verify V3 planning and the request-only boundary, **not** real food consumption or crew duty handover.

### What remains to implement after this G2 checkpoint

1. **Authoritative needs/commands:** Design/implement a real crew Hunger and food-use command in the canonical Core as appropriate under the separate Needs/character work; replace fixture-sourced values with a read-only projection from that state. It must not create a second hidden hunger clock.
2. **Ship interaction:** Hook actual food/store interactions, availability, approach, claim and release, including cancellations, missing food and loss of eligibility. The current spike action is targetless and does **not** test physical slots, pathing or occupancy.
3. **Duty and interruption:** Integrate priority/handover with existing shift/duty scheduler; record reason codes and consequent station effects.
4. **Tactical counterexample:** Build a second real GOAP scenario for perceived armor + ineffective current rifle + viable alternative or retreat. Vary fixed Trait and Skill inputs and capture explainable plans. Not implemented in this first batch.
5. **World persistence/offscreen:** Verify resource conservation and save/reload under canonical ticks. Don't run separate agent-owned survival state in Unity Update.
6. **Decision:** Compare GOAP's complexity with a small utility selector and FSM before ADOPT / ADAPT / REJECT in Miro D10.

The V3 package distinguishes `GoapActionProvider` (goal/plan) from `AgentBehaviour` (action execution); it includes Reactive, Proactive and Manual controllers. The example uses `ManualControllerBehaviour` for an isolated synchronous planning invocation, not a full game-life scheduler. See [upstream Getting Started](https://github.com/crashkonijn/GOAP/blob/3.1.2/Package/Documentation/Introduction/GettingStarted.md). The upstream documentation notes GOAP effects describe planning relationships, **not actual world mutations**; actual effects must be applied by game code. FrostArk's authoritative changes still happen through the shared world session.

## Known external runtime blocker

The separate [draft Codespaces Unity runner PR #95](https://github.com/MohamedXIV/Frostark/pull/95) has not yet established reliable G2/G3: headless Unity Editor startup encountered an entitlement/license error after earlier shared-memory and Linux GL dependency blockers. Do not report this spike as tested just because its code was committed, a dotnet test passed, or a Unity package README claims compatibility.

## Exit and decision

Leave the spike as DRAFT until there is actual Unity G2 and G3 evidence. Capture exact HEAD, commands, test XML/logs, package-lock resolution, obstacles and performance notes in the engineering Issue/PR. Post **ADOPT / ADAPT / REJECT** as a design decision to Miro D10 after proof; don't create an additional living design record in GitHub.
