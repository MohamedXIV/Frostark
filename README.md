# FrostArk

**FrostArk** is a PC-first, single-player **3D top-down Arctic maritime survival/action-management sandbox** built with **Unity 6 (6000.6.0f1), C#, and URP**.

> Your ship is your home, settlement, lifeline, and improvised warship in a freezing, collapsing ocean world.

The player inhabits a real protagonist, pilots a growing vessel and commands a small persistent crew, navigates pack ice and storms, lands on islands and installations, scavenges, fights human raiders and anomalous biological threats, manages heat/fuel/food/health, and follows optional mysteries. The world progresses whether or not the player pursues a quest.

## Product commitments

- **Priority:** Desktop (Windows/Linux/macOS) → Mobile → Web → PC handhelds → consoles (subject to platform SDKs and approvals).
- **Aesthetic:** readable, attractive low-poly 3D with pixel-art-informed, texture-driven detail. No mandatory PSX vertex snapping, unstable perspective, or deliberate visual discomfort.
- **Combat:** real-time on-foot and ship-to-ship. Player-controlled protagonist; optional AI-led squad; cover and tactical commands.
- **Survival:** interdependent ship heat, fuel, space, crew staffing, wounds, fatigue, weather, ice and navigation; few but consequential survivors.
- **Structure:** sandbox and systemic narrative first; no compulsory linear main quest. The catastrophe's nature remains uncertain.
- **Architecture:** Unity-specific presentation and runtime adapters around a testable, predominantly pure-C# simulation; explicit simulation clock, state and commands.
- **Delivery:** full-game architecture implemented incrementally through playable vertical slices; no throwaway prototype subsystems.
- **Agent workflow:** online/GitHub-first. Prefer engine-independent validation; batch/headless Unity where actually needed; avoid GitHub Actions by default to conserve free minutes.

## Documentation

Start with [documentation index](docs/INDEX.md), [game vision](docs/design/vision.md), [technical architecture](docs/engineering/architecture.md), [roadmap](docs/production/roadmap.md), and [GitHub backlog](docs/production/backlog.md). See [AGENTS.md](AGENTS.md) before contributing. Release-related changes follow the [versioning and changelog policy](docs/production/versioning-and-changelog.md) and the curated [Changelog](CHANGELOG.md). Design decisions still open are logged explicitly, not guessed into code.

## Current state

The integrated `main` includes the accepted E11 crew-control/agency runtime ([PR #126](https://github.com/MohamedXIV/Frostark/pull/126)) and the bounded E12 persistent physical ship, gangway and shore slice ([PR #133](https://github.com/MohamedXIV/Frostark/pull/133)). Further work is tracked in [live GitHub Issues](https://github.com/MohamedXIV/Frostark/issues). Unity currently records `bundleVersion = 0.1.0` as an **internal development setting**, not proof of a tagged or distributed game release. See the [release policy](docs/production/versioning-and-changelog.md). The Unity template's `Assets/TutorialInfo` is disposable starter guidance.
