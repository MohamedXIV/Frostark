# Verification and cloud-agent execution strategy

## Goal

Agents should execute the maximum useful work **online** with reproducible validation, minimizing reliance on the owner's local Windows Unity Editor. There is no claim that a cloud agent can currently run Unity itself: an authorized Unity binary, modules, platform SDK, appropriate licensing, storage and runner are required.

## Verification layers

| Gate | Tool/environment | Proves | Does not prove |
|---|---|---|---|
| G0 | schema/document/static diff reviews | docs and contracts coherent | C# compiles |
| G1 | `dotnet test` isolated Core, content validators | pure-domain behavior, reproducible simulations | Unity import |
| G2 | approved Unity -batchmode -nographics import + EditMode tests | Unity assembly/package import, Editor tests | real-time gameplay |
| G3 | PlayMode scene tests and build smoke | scene binding and engine integration | visuals/controls on every platform |
| G4 | desktop/mobile/Web/handheld manual and automated smoke | target hardware behavior | untested consoles |

At every milestone report which gates ran, exact commands, platform, evidence and reasons a gate was blocked. **Never call G2–G4 PASS based solely on G1**.

## Starting local/connector constraints

The [2026-10-10 local bootstrap](local-bootstrap-handoff.md) supersedes the initial installation assumptions: local Unity CLI and headless tests work, but ShellWarden's installed broker remains git-only and WebGL support is absent from the exact 6000.6.0f1 installation. Use `tools/Validate-Unity.ps1` on an authorized local runner; do not confuse it with remote ShellWarden readiness.

Unity project was created in `F:\UnityProjects\Frostark`, version 6000.6.0f1; GitHub is canonical after push. As of planning, ShellWarden's bootstrap policy rejects `Unity.exe`, `unity`, and `unity-cli` and does not itself prove Unity CLI availability. Enable narrowly scoped executable permission only after user-approved policy configuration; do not bypass ShellWarden. Avoid assuming headless execution can overlap with an already open same Unity project.

## Agent development loop

1. Read current GitHub main + open PR/Issues, avoid duplicate branch/work.
2. Choose smallest **dependency-ready** acceptance unit; create feature branch.
3. Implement production-grade domain/data contract and tests before relying on scenes.
4. Run permitted G0/G1; if G2 unavailable, preserve clear 'engine integration unverified' evidence.
5. Add deterministic fixtures, generated content/scene setup where beneficial; Unity Editor scripts are allowed but not substitutes for execution.
6. Produce PR with actual test evidence, serialization/portability impacts and explicit blocked gates.
7. Integrate at approved quality thresholds; do not consume GitHub Actions minutes for routine verification.

## Cost and tooling

- Prefer connected GitHub operations for remote writes; RepoPilot Dev for GitHub Project visibility and exact-head coordination.
- Use `dotnet` in agent environment for pure-C# tests; install correct SDK after validating Unity compatibility.
- Headless Unity `-batchmode -nographics` only on authorized runner; plan module installation/activation carefully. Unity Pipeline package present in manifest is experimental, **optional**.
- GitHub Actions is **opt-in only if necessary** and justified; do not create workflows by default.
- Track LFS binary assets and integrity; never commit `Library/` or `Temp/`.

## Regression portfolio to build

Clock/sleep interrupts, resource/heat conservation, crew reassignment while landed, character injuries/skills, expedition scene persistence, ice damage, naval gun economy, tactical AI cover reservations, seeded world reproducibility, save migrations, multi-platform input map and scene import. Test failure artifacts should be small enough for cloud review.

## Engine acceptance gates

Before first vertical-slice merge, require actual Unity compile + import smoke in a runner. Before gameplay slice sign-off, require representative PlayMode and visible interactive proof. If unavailable, code can be landed with status 'G2/G3 pending' but the corresponding **playable** milestone remains open.
