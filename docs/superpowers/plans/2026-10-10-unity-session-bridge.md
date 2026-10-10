# FrostArk #19 — Pure Session application facade and Unity VContainer bridge

Date: 2026-10-10. Authoritative scope: [Issue #19](https://github.com/MohamedXIV/Frostark/issues/19), `docs/engineering/architecture.md`.

## Accepted design
- The only mutable authority for a run is `FrostArk.Core.Application.WorldSession`: it owns one immutable-on-commit WorldState pointer and one SimulationScheduler, accepts validated commands via WorldCommandBus and derives read projections directly from state. Do **not** duplicate mutable supply/crew totals in a MonoBehaviour.
- Bootstrap a new empty run as a **fixture** at minute 0; content-defined starting ship/crew belongs later milestones. `WorldSession.Restore` consumes a validated SaveGameDto and does not reset processed command IDs or pending events.
- `WorldSession.Changed` broadcasts a new snapshot **after** committing a successful command or a non-zero time advance, not before, and rejects further access after disposal. UI subscribes/unsubscribes during lifetime; no global service locator.
- A VContainer scoped `WorldSessionScope : LifetimeScope` registers one `WorldSession` per scene scope and a `WorldSessionPresenter` component in its scene. Presenter is only a read-through projection (minute, revision and string status); input adapters call the session's narrow command API. Presenter does not run simulation from Update or FixedUpdate.
- An explicit Editor-only `FrostArkSessionFixture.CreateScene()` generates/updates a reproducible minimal bootstrap scene under `Assets/FrostArk/Scenes/` without loading/destroying the current user's scene; it is not executed automatically.
- An Editor EditMode test creates a scope and presenter GameObjects, builds VContainer, executes one command and verifies only the injected session owns authority. This is a **G2 acceptance check**, unverified until Unity Editor actually runs.

## TDD ledger
1. RED: Add NUnit `WorldSessionTests` covering commit/reject, revision, advances/notifications, save+resume and disposal; run user-approved public GitHub Actions, confirm missing types are compile errors.
2. GREEN: Add `Application/WorldSession.cs` and Unity `.meta`. Run CI and verify real NUnit PASS at exact head.
3. Bridge: Add VContainer scope, MonoBehaviour presenter, runtime/editor asmdefs + .meta, explicit scene factory and EditMode tests. No duplicate fuel fields and no static singletons; G1 CI cannot compile Unity-only source, so be explicit about G2 status.
4. Review: Independently audit assembly dependencies against installed VContainer 1.19.0, registration lifecycle, scene generation side effects and tests. Merge only if exact-head checks green and clearly distinguish G1 from G2. Issue #19 remains OPEN until its explicit G2 Editor import + EditMode test acceptance is proven. If G2 cannot run, leave PR draft and continue other ready items.

## Intentional limits
No UI Toolkit layout, combat scene, full ship/crew fixture, save filesystem bridge, Android/WebGL build proof or cross-platform physics claim. Unity 6000.6.0f1, URP 17.6.0, VContainer 1.19.0. Never use ShellWarden or modify user's local working tree.
