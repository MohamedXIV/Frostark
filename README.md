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

Start with [documentation index](docs/INDEX.md), [game vision](docs/design/vision.md), [technical architecture](docs/engineering/architecture.md), [roadmap](docs/production/roadmap.md), and [GitHub backlog](docs/production/backlog.md). See [AGENTS.md](AGENTS.md) before contributing. Design decisions still open are logged explicitly, not guessed into code.

## Current state

An initial Unity URP template is present; no FrostArk gameplay implementation is claimed. Documentation, execution roadmap, and issue backlog are established in PR #1 and Issues #2–#43. [Frostark Development Project #5](https://github.com/users/MohamedXIV/projects/5) now contains all 42 Issues, with priority values assigned and #13/#14 marked Ready. Native blocked-by and parent-child relationships and optional Phase/Type fields remain tracked in Issue #43. The Unity template's `Assets/TutorialInfo` is disposable starter guidance, not game content.
