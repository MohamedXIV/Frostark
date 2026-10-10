# FrostArk full-game roadmap (dependency-oriented)

**Version:** foundation baseline (2026-10-10). This is a complete production architecture plan, **not a promise of dates**. All epics are production systems; slices provide early integration feedback without disposable code. GitHub Issues [#2–#43](https://github.com/MohamedXIV/Frostark/issues) now link to these epics and are the execution source of truth; see [live issue map](backlog.md) and [Frostark Development Project #5](https://github.com/users/MohamedXIV/projects/5).

## Lifecycle

- **R0 — Foundation / production contracts.** Documentation, architecture, issue graph, Unity/GitHub hygiene, editorless .NET test harness, DI, command/query ports, authoritative clock, world persistence baseline. Exit: deterministic headless simulation + explicit G2 integration evidence or named blocking gate.
- **R1 — Embodied playable coastal loop.** Player movement/aim/camera, input, near-isometric visual baseline, scene assembly, interactive ship exterior/interior, small landing site, real inventory and resources, basic extraction/return. Exit: actual Unity playable transition with stable world state.
- **R2 — Ship as survival settlement.** Module/layout/room integrity, thermal networks, heat/fuel/food/water, crew jobs/shifts, fatigue/medical effects, repair/crafting and constrained ship growth. Exit: meaningful cold/fuel staffing decisions across days including sleep interrupt.
- **R3 — Shore combat & expeditions.** Weapons/armor/ammo/wounds, real-time enemies, AI navigation/perception, cover/utility decisions, squad orders and roster/loadout, loot/extraction. Exit: controllable fight/avoid/retreat loop with consequences and persistence.
- **R4 — Marine travel, ice and combat.** Navigation, weather currents, ice thickness, hull/engine, naval weapon stations/raiders, ship damage and retreat. Exit: alternate-risk routes and naval encounter materially change supplies and survivability.
- **R5 — Crew/player careers and long-haul simulation.** Skills/perks/training, recruitment/relationships/morale, scheduling and replacement, advanced sleep/travel, environmental gear and deep region access. Exit: unique specialist losses/skill development create strategic consequences.
- **R6 — Living world, regions, economy and mysteries.** Biome/thermal zones, generated plus authored sites, persistent world actors/factions/settlements/markets/contracts, rumor/intelligence and science/biopunk content. Exit: world advances outside player scenes and produces divergent credible outcomes.
- **R7 — Content and presentation production.** Content authoring/validation pipeline, ship/character/environment art, sound/weather/VFX, accessibility, onboarding, usable controller/mobile interface, tool-driven content expansion. Exit: cohesive artistic and interaction quality across core loops.
- **R8 — Stability, optimization and platform release gates.** Save migrations, performance budgets, correctness/security/content QA, target platform compiles/builds/tests, distribution, compatibility and release readiness. Desktop release first; later ports gated by hardware/platform evidence.

### Cross-cutting integration slices

- **Slice A: First shore return (R0→R1):** player leaves vessel, acquires salvage, returns and can save/resume.
- **Slice B: Cold night (R1→R2):** crew assignments, heating ration, time advance and interrupted sleep.
- **Slice C: Expedition danger (R2→R3):** AI opponent, cover, wounded companion, valid extraction.
- **Slice D: Ice and pirates (R3→R4):** tactical voyage choice and real naval combat.
- **Slice E: Consequential sandbox (R4→R6):** active/near/distant simulation, factions, training and optional leads.

## Epic board

| Epic ID | Epic | Earliest dependency |
|---|---|---|
| E00 | Governance & cloud agents | none |
| E01 | Core simulation/time/save | E00 |
| E02 | Unity input/camera/visual runtime | E00 |
| E03 | Ship systems, survival and modularity | E01,E02 |
| E04 | On-foot combat, navigation and cover AI | E01,E02 |
| E05 | Crew, progression, squad coordination | E01,E03,E04 |
| E06 | Sea travel, ice and naval encounters | E01,E02,E03 |
| E07 | World generation, factions and narrative | E01,E03,E04,E06 |
| E08 | Inventory, crafting, trade and content | E01,E03,E04 |
| E09 | Production art, UI, audio and accessibility | E02, with incremental dependencies |
| E10 | QA, tooling, optimization and release | E00, cross-cutting |

Issues need criteria and test gates, not checklists that merely say 'implement subsystem'. No arbitrary artificial dependency between parallel independent epics: once E01/E02 interfaces stabilize, ship, land and UI work can proceed concurrently. R numbers express *integration outcomes*, not a mandate for one giant serial PR per phase.

## Gates at each slice

1. Cross-system integration demonstrably uses shared C# world state, clock and save.
2. Minimum G1 domain/unit tests pass; G2 engine compile/import proven before declaring Unity-playable.
3. G3 scene proof and G4 target acceptance required when called 'playable/release-ready'.
4. Performance and content size tracked on representative desktop, then mobile/web/handheld.
5. Any unavailable tool/SDK is a clearly recorded deferred gate, not silently 'passed'.

## Scope warnings

Single-player first; no multiplayer. Do not demand deep RTS squads, physically accurate fluid/ice simulation, AAA open-world streaming, Steam Deck/console SDKs, large procedural story generation or a complete talent tree before core loops work. Existing Unity URP initial template and existing AI Navigation/Input System/Test Framework are baseline assets.

## Ready-now sequence

1. Review and merge baseline documentation PR #1; track GitHub Projects creation explicitly in #43.
2. Create core assembly and .NET tests, baseline benchmark and deterministic clock.
3. Use bootstrap-validated VContainer 1.19.0 and Addressables 2.11.2; integrate scoped DI after assembly architecture exists, then add save schema. See the local bootstrap handoff for validation and unresolved runner gates.
4. Unity import/compile/build runner contract (authorized) and first scene bridge.
5. First playable coastal slice; add subsequent cross-system survival and combat behaviors.

Track each open decision in `decisions.md`, connect resolved decisions to acceptance and migrate docs/issues together. Timelines follow actual verified throughput, not speculative dates.
