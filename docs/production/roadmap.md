# FrostArk full-game roadmap (dependency-oriented)

**Version:** foundation baseline (2026-10-10). This is a complete production architecture plan, **not a promise of dates**. All epics are production systems; slices provide early integration feedback without disposable code. GitHub Issues [#2–#43](https://github.com/MohamedXIV/Frostark/issues) now link to these epics and are the execution source of truth; see [live issue map](backlog.md) and [Frostark Development Project #5](https://github.com/users/MohamedXIV/projects/5).

**Accepted controls:** Top-down/near-isometric point-and-click selection, movement and context action for **protagonist and crew**, real-time squad orders and spatial ship/room management. Not WASD action-RPG locomotion. See [control contract](../design/control-and-interaction.md) and [decisions](decisions.md).

## Lifecycle

- **R0 — Foundation / production contracts.** Documentation, architecture, issue graph, Unity/GitHub hygiene, editorless .NET test harness, DI, command/query ports, authoritative clock, world persistence baseline. Exit: deterministic headless simulation + explicit G2 integration evidence or named blocking gate.
- **R1 — Embodied playable coastal loop.** Click-to-move protagonist/party selection and contextual tactical orders, input/camera, near-isometric visual baseline, scene assembly, interactive ship exterior/interior, small landing site, real inventory and resources, basic extraction/return. Exit: actual Unity playable transition with stable world state.
- **R2 — Ship as top-down survival settlement.** Click-inspect/assign room management, module/layout/room integrity, thermal networks, heat/fuel/food/water, crew jobs/shifts, fatigue/medical effects, repair/crafting and constrained ship growth. Exit: meaningful cold/fuel staffing decisions across days including sleep interrupt.
- **R3 — Shore combat & expeditions.** Selected-unit/group tactical orders, weapons/armor/ammo/wounds, real-time enemies, AI navigation/perception, cover/utility decisions, squad orders and roster/loadout, loot/extraction. Exit: controllable fight/avoid/retreat loop with consequences and persistence.
- **R4 — Marine travel, ice and combat.** Navigation, weather currents, ice thickness, hull/engine, naval weapon stations/raiders, ship damage and retreat. Exit: alternate-risk routes and naval encounter materially change supplies and survivability.
- **R5 — Crew/player careers and long-haul simulation.** Skills/perks/training, recruitment/relationships/morale, scheduling and replacement, advanced sleep/travel, environmental gear and deep region access. Exit: unique specialist losses/skill development create strategic consequences.
- **R6 — Living world, regions, economy and mysteries.** Biome/thermal zones, generated plus authored sites, persistent world actors/factions/settlements/markets/contracts, rumor/intelligence and science/biopunk content. Exit: world advances outside player scenes and produces divergent credible outcomes.
- **R7 — Content and presentation production.** Content authoring/validation pipeline, ship/character/environment art, sound/weather/VFX, accessibility, onboarding, usable controller/mobile interface, tool-driven content expansion. Exit: cohesive artistic and interaction quality across core loops.
- **R8 — Stability, optimization and platform release gates.** Save migrations, performance budgets, correctness/security/content QA, target platform compiles/builds/tests, distribution, compatibility and release readiness. Desktop release first; later ports gated by hardware/platform evidence.

### Cross-cutting integration slices

- **Slice A: First shore return (R0→R1):** player is click-selected, ordered ashore to salvage, returns and can save/resume.
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
| E11 | Cross-theater RTS crew selection, orders and distinct Ship Grid / Shore NavMesh execution | E02,E03,E04,E05; command & location truth from E01 |

Issues need criteria and test gates, not checklists that merely say 'implement subsystem'. No arbitrary artificial dependency between parallel independent epics: once E01/E02 interfaces stabilize, ship, land and UI work can proceed concurrently. R numbers express *integration outcomes*, not a mandate for one giant serial PR per phase.

## E11 integration track — shared RTS control, separate movement theaters (2026-10-20)

**Engineering Epic:** [#108](https://github.com/MohamedXIV/Frostark/issues/108). **Canonical design authority:** [Miro D01 — Ship-local navigation](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660187), [D02 — Placement/clearance](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03 — Smart interactions](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188), [D09 — Crew Agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). This section is an **execution map**, not a parallel gameplay design specification.

The **same** selected-unit RTS input, group commands and stable crew identity must work across two distinct local navigation implementations:

| Movement theater | Adapter / engineering owner | Never substitute |
|---|---|---|
| **Shipboard crew** | Canonical module/layout-derived **ship-local walkability**, compartment door/stair portals, local A* and smooth Unity movement; [#109](https://github.com/MohamedXIV/Frostark/issues/109), [#110](https://github.com/MohamedXIV/Frostark/issues/110), [#113](https://github.com/MohamedXIV/Frostark/issues/113), [#58](https://github.com/MohamedXIV/Frostark/issues/58) | NavMeshAgent baked/rebaked on a moving ship, or using the build grid as the navigation grid |
| **Shore crew/protagonist** | Existing **Unity NavMeshAgent** and tactical navigation with unified order outcomes; [#28](https://github.com/MohamedXIV/Frostark/issues/28) / [#32](https://github.com/MohamedXIV/Frostark/issues/32) | Ship-local A* or independent shadow actor state |
| **Ship at sea** | Strategic Core sea travel / later marine physical motion; [#33](https://github.com/MohamedXIV/Frostark/issues/33), [#60](https://github.com/MohamedXIV/Frostark/issues/60) | Either crew walking system |

**Cross-cutting player-control milestones (R1→R3, no speculative dates):**

1. **P0 shared interfaces:** [#109](https://github.com/MohamedXIV/Frostark/issues/109) routes a Move/Interact order to the correct theater; [#111](https://github.com/MohamedXIV/Frostark/issues/111) adds single/box/group selection and explicit **empty-ground deselect**; [#112](https://github.com/MohamedXIV/Frostark/issues/112) arbitrates player order, standing duty and idle autonomy without a per-unit `Resume Auto` toggle. Pure selection is transient; outstanding player orders and assignments must survive deselection. World time/Needs never freeze just because an actor is selected.
2. **P0 ship spatial integration:** [#110](https://github.com/MohamedXIV/Frostark/issues/110) supplies actual room/deck walkability from the same layout truth as placement. Build footprint != traversability; invalid door/clearance blocks interactions, no teleport.
3. **P1 visible locomotion:** [#113](https://github.com/MohamedXIV/Frostark/issues/113) smooths A*-guided ship motion, safe turning/replanning, small-group traffic and slot reservations; visual feedback from [#39](https://github.com/MohamedXIV/Frostark/issues/39). Existing Galley fixture [#106](https://github.com/MohamedXIV/Frostark/issues/106) / Draft [PR #107](https://github.com/MohamedXIV/Frostark/pull/107) is a focused **test slice**, not the full ship spatial system.
4. **P1 on-foot parity:** existing [#32](https://github.com/MohamedXIV/Frostark/issues/32) owns shore multi-unit NavMesh orders/formations and per-actor acceptance, integrated with [#28](https://github.com/MohamedXIV/Frostark/issues/28); avoid a duplicate shore Epic.
5. **Integration G2/G3:** [#114](https://github.com/MohamedXIV/Frostark/issues/114) proves 2–4 crew select/group move/deselect on ship, keep existing orders or resume idle autonomy, then transfer ashore and reuse the same UX on NavMesh. Save/reentry and resource/claim ownership must be coherent. No 'complete' without actual Unity scene and test evidence.

**Player-observed polish/semantics:** ship-grid route computation remains discrete but actor movement and rotation must not look robotic; `Hunger = 1000` / `Thirst = 1000` represent **maximum severity**, not fullness. Final values and interpolation tuning require human playtest evidence, not a new global stat system.

## Gates at each slice

1. Cross-system integration demonstrably uses shared C# world state, clock and save.
2. Minimum G1 domain/unit tests pass; G2 engine compile/import proven before declaring Unity-playable.
3. G3 scene proof and G4 target acceptance required when called 'playable/release-ready'.
4. Performance and content size tracked on representative desktop, then mobile/web/handheld.
5. Any unavailable tool/SDK is a clearly recorded deferred gate, not silently 'passed'.

## Scope warnings

Single-player first; no multiplayer. Do not demand deep RTS squads, physically accurate fluid/ice simulation, AAA open-world streaming, Steam Deck/console SDKs, large procedural story generation or a complete talent tree before core loops work. Existing Unity URP initial template and existing AI Navigation/Input System/Test Framework are baseline assets.

## Current next dependency gates — revised 2026-10-11

1. Verify draft **[#52](https://github.com/MohamedXIV/Frostark/pull/52)** scoped Unity WorldSession bridge with actual Editor G2, then cleanly integrate. Shared Core systems remain the only authoritative simulation source.
2. Verify draft **[#56](https://github.com/MohamedXIV/Frostark/pull/56)** visual benchmark with actual Unity camera/occlusion/fog scene evidence (G2/G3). Use top-down click-target readability, not WASD aim legibility, as the camera decision benchmark.
3. Use merged **[#77](https://github.com/MohamedXIV/Frostark/pull/77)** `TacticalOrderIntent` in [#21](https://github.com/MohamedXIV/Frostark/issues/21) to build the first physically playable **click-select protagonist → click ground to walk → contextual salvage/return** Unity scene. G0/G1 for this Core envelope is not G2/G3 proof of scene movement.
4. Extend the same command/selection UI to ship rooms/crew and later tactical party execution (#32), cover/perception (#28/#29), combats and naval work. Avoid duplicate gameplay and data authorities or unneeded pre-R1 Core breadth.

Track each open decision in `decisions.md`, connect resolved decisions to acceptance and migrate docs/issues together. Timelines follow actual verified throughput, not speculative dates.
