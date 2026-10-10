# FrostArk decision register

Decisions are explicit and may be changed through an ADR with impact on roadmap/tests. **Accepted** = agreed direction; **Proposed** = leading hypothesis awaiting an experiment or confirmation; **Open** = no commitment. An agent must not silently turn Proposed into Accepted.

| ID | Topic | Status | Direction / unresolved choice |
|---|---|---|---|
| D001 | Engine/language | **Accepted** | Unity 6, C#, URP; initial project 6000.6.0f1 |
| D002 | Target priority | **Accepted** | Desktop → Mobile → Web → PC handhelds → Consoles |
| D003 | Game genre | **Accepted** | PC-first top-down 3D single-player Arctic maritime survival/action-management sandbox |
| D004 | Art direction | **Accepted** | Attractive low-poly models + texture-driven pixel-art-like detail; no forced PSX rendering jitter |
| D005 | Architecture intent | **Accepted** | Modular code-first pure-C# simulation and Unity adapters; avoid universal singleton-manager graph |
| D006 | World clock | **Accepted** | Authoritative game-time scheduler separate from Unity rendering/physics ticks, supporting interruptible sleep |
| D007 | Global state / save | **Accepted** | Versioned persistent world state; scene objects not source of truth |
| D008 | Ship and player | **Accepted** | Embodied player, evolving multi-room staffable ship, persistent small meaningful crew |
| D009 | Combat theaters | **Accepted** | Real-time on-foot and naval combat, with survival/economy consequences |
| D010 | Sandbox | **Accepted** | Systemic world + optional faction/mystery content, no forced linear quest rail |
| D011 | AI/pathing | **Proposed** | Unity AI Navigation + own tactical cover/utility AI + state machine |
| D012 | DI | **Proposed** | VContainer scopes and small command/query facades; confirm compatibility |
| D013 | Squad size | **Proposed** | Player + 3 optional companions; benchmark UI/AI readability |
| D014 | Crew progression | **Proposed** | Skill use/training + limited perks; no dominant universal level |
| D015 | Dual crew roles | **Accepted** | One individual can have ship duty and expedition role, mutually exclusive in time |
| D016 | Hero death | **Open** | Game over vs crew succession/permadeath policy |
| D017 | Companion control | **Open** | Direct control of only protagonist vs limited switching; optional slow-motion orders |
| D018 | Rendering camera | **Open** | Fixed isometric vs adjustable top-down tilt; impacts cover/interiors |
| D019 | World-time ratio | **Open** | In-game minutes per real second, travel/fast-forward speed and difficulty curves |
| D020 | Weather mystery | **Open** | In-world name, causal explanations, revelations; intentionally avoid premature canon |
| D021 | Save serializer | **Open** | DTO schema version and binary/JSON/msgpack/other adapter after evidence |
| D022 | World scale | **Open** | Streaming/region size, seeded layout scope, nav/ship transition |
| D023 | Multiplayer | **Not planned** | Single-player first; no networking foundations unless explicitly approved |
| D024 | GitHub Actions | **Accepted** | Avoid by default to preserve free minutes; justify exceptions |
| D025 | Starter art | **Accepted** | Unity URP TutorialInfo assets are disposable and have no FrostArk design value |
| D026 | Engines for cloud | **Accepted** | Prefer online agents, pure .NET G1 tests, authorized headless Unity only where needed |

## ADR format

For a change, record: Context → Alternatives → Decision → Consequences → Verification → Date/Owner. Create a corresponding Issue if code or art must change.

## Priority design experiments

1. Exact art/camera/occlusion target with ground combat and ship interiors.
2. Squad control + cover benchmark using player and three companions, with controller/mobile UI proof.
3. World clock ratios, overnight sleep interruption and resource balance.
4. Protagonist death/succession and run persistence.
5. Unity headless runner and licensing feasibility before depending on fully online G2/G3.
