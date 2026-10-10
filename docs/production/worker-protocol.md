# FrostArk autonomous development worker

**Purpose:** Continue implementing FrostArk from actual GitHub state with minimum manual intervention. The work queue is [GitHub Project #5](https://github.com/users/MohamedXIV/projects/5); the source of truth remains [MohamedXIV/Frostark](https://github.com/MohamedXIV/Frostark). This document defines *execution behavior*, not a claim that every scheduled run has all required tools.

## Required tools and boundaries

- Use connected **GitHub** for remote repository source, issues, PRs and comments; **RepoPilot Dev** for the Project issue/dependency graph, current-head checks, claims and checkpoints; **Superpowers** skills for design/plans, test-driven changes, debugging, review and verification.
- Online work only. No **ShellWarden**, no edits to other repositories, no compulsory local Editor operation, no GitHub Actions (unless user explicitly changes this policy). Do not download Android/WebGL support solely for the worker.
- Never assume a scheduled ChatGPT task inherits interactive connector permissions. On each run, **probe tool availability first**. If missing, stop truthfully and avoid empty fabricated status reports. A scheduled task is not a persistent compute process. No claims of completed work after a tool-free run.
- Don't use GitHub Actions just to work around an absent `dotnet` SDK; install/use an independently authorized .NET environment when available and record exactly what ran.

## At every invocation

1. Read actual `main` HEAD SHA, open PRs, recent issues and [AGENTS.md](../../AGENTS.md). Refresh Project #5 with RepoPilot. Inspect native blocked-by edges and existing claims/handoffs before selecting work.
2. Resume the **canonical active PR** first, if non-duplicated and dependency-ready. Do not create a competing PR when a branch already exists.
3. Select a PR-sized, dependency-ready child issue. Respect native GitHub dependencies, not merely textual status. If the Ready queue is empty, examine In progress and open PRs, then independently executable documentation/review tasks. Do not close tasks that lack evidence.
4. Follow the relevant design spec and Superpowers plan; for code changes do test-first red/green cycles where executable. If compilation cannot run, limit assertions and keep the PR Draft (or clearly separate incomplete integration).
5. Prefer production-shaped, shared pure-C# modules; keep UnityEngine out of FrostArk.Core; use standalone `dotnet test` for G1. Actual Unity import/EditMode/PlayMode is a distinct G2/G3 gate and may be unavailable remotely.
6. Use exact-head writes. Inspect patches, package/meta health, regressions and CI checks without starting a GitHub Actions workflow. Merge only when all relevant attainable acceptance gates are genuinely satisfied, explicitly recording any deferred Unity/device evidence; never use GitGuardian success as C# test evidence.
7. Update GitHub issue + RepoPilot Project status and checkpoint with exact refs, test commands/results and next ready item. Close child issues only when delivery and required acceptance gates are complete. Don't close Epic parents merely because docs are done.

## Immediate queue (2026-10-10 baseline; revalidate)

- **Issue #15**: first priority is [PR #46](https://github.com/MohamedXIV/Frostark/pull/46), Draft on `feat/15-core-standalone-tests` at `7ae6a809c8d75f33cbcb4cd784e3d7876ec9b7ca` (snapshot, **not** an evergreen SHA). Shared Unity-independent Core primitives and NUnit tests have been authored; **real `dotnet restore/test` and Unity compile on this PR are UNVERIFIED**. Reinspect source and run/fix tests using an authorized .NET environment before promoting or merging. The separate prior local Unity 2/2 smoke applies to **PR #45**, not #46.
- Once #15 completes, prioritize #16 clock/scheduler and #17 authoritative WorldState/commands (both blocked by #15), then #18 saves and #19 Unity composition.
- ShellWarden remote execution is intentionally optional/outside FrostArk; never block pure-domain work on it. Keep WebGL build-module gap deferred until relevant platform work.

## Progress/report contract

A useful run ends with `issue / PR / exact SHA / tested vs untested / dependencies / next`. Distinguish `PASS`, `FAIL`, `NOT RUN`, `BLOCKED`. Never mark an issue Done because code seems plausible. If tools or test runner are unavailable, state that **once per meaningful change**, keep existing PRs Draft as appropriate, and avoid spawning placeholder branches or issues.

This coordination protocol is not a replacement for a real persistent code-execution agent. When connector permissions are unavailable to ChatGPT scheduled tasks, autonomous GitHub modifications are not possible from that run; use an interactive, explicitly connected session or a separate authorized cloud coding worker.
