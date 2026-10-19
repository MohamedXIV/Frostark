# Agent Operating Contract — FrostArk

Applies to AI agents and contributors across the repository. Read `docs/INDEX.md`, `docs/production/roadmap.md`, and relevant feature spec before changes.

## Scope and authority

1. **GitHub main is the source of truth for implementation and engineering evidence** after validating the latest live head, open PRs, issues and related dependencies. **Miro is the single authoritative home of FrostArk game-design decisions**, as defined below. Do not overwrite unpushed local changes or invent completion evidence.
2. This is a **full game, not a prototype**. Deliver production-shaped, modular vertical slices. Temporary art/fixtures are acceptable when explicitly named and replaceable; throwaway architecture is not.
3. Preserve the hierarchy: desktop → mobile → web → PC handheld → console. Avoid a feature that silently breaks an earlier target.
4. The user's local Unity project may contain unpushed work. Never silently reset, delete, or reconfigure local files. `Assets/TutorialInfo` is known removable starter content; clean it only as a deliberate separate commit.
5. **GitHub Actions explicitly approved for public FrostArk on 2026-10-10.** Use targeted, read-only hosted CI for G0/Python and G1/.NET NUnit tests (currently `.github/workflows/core-checks.yml`) and inspect real job logs before merging. Do not run privileged PR workflows, expose secrets, introduce paid runners, or claim Unity G2/PlayMode/target platform proof from G1.
6. Never assume ShellWarden grants Unity execution. The 2026-10-10 bootstrap verified local Unity CLI/headless import and two package tests, but the installed ShellWarden broker still permits only git. See `docs/engineering/local-bootstrap-handoff.md`; new engine checks remain **not run** until execution evidence exists for that change.

## Cross-app single-source-of-truth rule (FrostArk Design Lab)

**Each piece of information has exactly one authoritative owner; every other system links to it. Never create or maintain competing copies of the same design decision.**

- **Miro owns game-design truth** for FrostArk: original ideas, discussion, rejected alternatives and rationale, accepted/deferred directions, unresolved questions, visual references, screenshots and sketches. Canonical board: [FrostArk — Design Lab](https://miro.com/app/board/uXjVEYctyzg=/). Read the relevant detailed Miro design record before interpreting or changing a design decision; one-line canvas cards are navigation only, not complete requirements.
- **GitHub owns implementation/engineering truth**: source code, technical interfaces/contracts, actionable Issues, PRs, tests, checks, releases and real implementation status. An Issue should link to its authoritative Miro decision and contain only its specific implementation scope, acceptance criteria and technical details, not a second independent narrative of that decision.
- **RepoPilot is coordination, not a third authority**: surface and link the live Miro/GitHub truth; keep only RepoPilot-specific execution state, bindings and handoffs. Do not copy design decisions into RepoPilot/Convex or GitHub Project Draft Issues as an alternate design database.
- **Design status and implementation status are separate.** Miro can mark an idea Proposed / Researching / Accepted / Rejected / Deferred. Do not manually assign `Implemented` as a Miro design status: derive completion from verified live GitHub engineering evidence. **Accepted does not mean implemented.**
- If Miro design and GitHub implementation disagree, explicitly identify the divergence, reconcile with the user, then update the appropriate authoritative owner. Do not silently sync, reword, replace or "fix" one system based on the other.
- Historical GitHub design files, older decisions and discussion transcripts remain useful *sources and snapshots*; do not delete them wholesale or treat their old proposals as equally authoritative to later accepted Miro decisions. During adoption/migration, reference source history and mark superseded material clearly rather than creating two living design specifications.
- If Miro is unavailable in an execution environment, state that the design record was **not verified**; do not invent a current decision from stale docs or memory. GitHub remains independently authoritative for actual code and tested outcomes.

## Engineering style

- Pure C# domain/application logic with explicit immutable IDs, serialized DTO/state contracts and controlled mutations; UnityEngine isolated behind adapter/bridge assemblies.
- Dependencies point inward: Presentation → Application → Domain, Infrastructure implementing ports. No service locator or roaming static singleton graph.
- Use narrow command/query APIs and event notifications for state changes, not a global mutable mega-manager/event bus.
- Central authoritative world clock; deterministic ordered world ticks and scheduled events. `Update` handles presentation/input and `FixedUpdate` physics, not independent authoritative hunger/fuel clocks.
- Sleep/travel acceleration advances simulation and handles interrupts; never simulate thousands of accelerated physics frames.
- Keep simulation decisions seeded/replayable when feasible; **do not claim cross-platform bit-exact Unity physics**.
- Respect entity lifecycle, scene changes, cancellation and save/load boundaries. Author regression tests for cross-system effects and save migrations.
- All systems must declare active/nearby/distant simulation LOD where relevant.

## Work units and verification

Each issue must state outcome, prerequisites, implementation scope, tests/evidence, and out-of-scope items. Prefer one meaningful independently reviewable PR per dependency-ready issue.

Evidence ladder:
1. Docs/schema consistency and static review (always possible).
2. Pure-C# unit/integration tests using `dotnet test` (after test harness exists).
3. Unity package import/compile and EditMode tests via an authorized headless Editor.
4. PlayMode/integration, build smoke and visual/controller/mobile validation on actual targets.
Never conflate levels 1–2 with Unity compile or gameplay proof.

After each PR, provide: exact head SHA, files changed, commands and test counts actually run, blockers/unverified gates, platform impacts and next dependency-ready issue. Do not mark manual device or engine tests PASS from inference.

## Repository hygiene and collaboration

Use feature branches and PRs for engine/code changes. Keep Unity text serialization and corresponding `.meta` GUID pairs. Commit `Assets/`, `Packages/manifest.json`, `Packages/packages-lock.json`, `ProjectSettings/`, and docs; never commit `Library/`, `Temp/`, `Logs/`, `Build/` outputs or secrets. Git LFS is configured for binary art—verify pointers resolve before relying on files.

Prefer C# coding styles: focused types, meaningful names, no speculative interfaces, no unnecessary DOTS/ECS/third-party dependency. Add VContainer only as a targeted foundation choice; use Unity AI Navigation rather than writing basic pathfinding anew.

## Definition of done

Code reviewed; requirements and regression tests met; serialization/migrations addressed; accessibility and platform constraints considered; representative scene/build tested when engine evidence is available. If a gate cannot run, mark it **pending/unverified**, and do not call the feature production-accepted.

## Decision governance

`docs/production/decisions.md` remains a historical/engineering decisions record. For **current and future game-design rationale**, use the canonical Miro Design Lab record; link to it from GitHub engineering work rather than authoring a parallel design specification. Treat older proposed options (squad cap, succession/permadeath, clock ratios, direct companion control, AI library selection, save format) as proposals unless the current design record or verified implementation evidence explicitly resolves them.

## Continuous worker protocol

The canonical coordination contract is [docs/production/worker-protocol.md](docs/production/worker-protocol.md). Start from LIVE GitHub/RepoPilot state every invocation; scheduled ChatGPT runs may lack plugin access and **must not** pretend they wrote code or merged a PR. The automation schedule is not a substitute for an authorized executable cloud environment.
