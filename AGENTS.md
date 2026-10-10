# Agent Operating Contract — FrostArk

Applies to AI agents and contributors across the repository. Read `docs/INDEX.md`, `docs/production/roadmap.md`, and relevant feature spec before changes.

## Scope and authority

1. **GitHub main is the source of truth** after validating the latest live head, open PRs, issues and related dependencies. Do not overwrite unpushed local changes or invent completion evidence.
2. This is a **full game, not a prototype**. Deliver production-shaped, modular vertical slices. Temporary art/fixtures are acceptable when explicitly named and replaceable; throwaway architecture is not.
3. Preserve the hierarchy: desktop → mobile → web → PC handheld → console. Avoid a feature that silently breaks an earlier target.
4. The user's local Unity project may contain unpushed work. Never silently reset, delete, or reconfigure local files. `Assets/TutorialInfo` is known removable starter content; clean it only as a deliberate separate commit.
5. **No GitHub Actions** unless a necessity is demonstrated and the user approves the cost/benefit. Prefer .NET test runners, agent cloud environment, an approved headless Unity runner, and manual playtest evidence.
6. Never assume ShellWarden grants Unity execution. The 2026-10-10 bootstrap verified local Unity CLI/headless import and two package tests, but the installed ShellWarden broker still permits only git. See `docs/engineering/local-bootstrap-handoff.md`; new engine checks remain **not run** until execution evidence exists for that change.

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

`docs/production/decisions.md` records adopted decisions, proposals and undecided questions. Proposals (squad cap, succession/permadeath, clock ratios, direct companion control, AI library selection, save format) remain proposals until approved or a documented implementation experiment decides them.
