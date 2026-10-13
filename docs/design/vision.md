# Game vision and design pillars

**Genre:** top-down / near-isometric 3D real-time tactical squad-command + ship/crew survival-management sandbox, PC-first, single-player. **Control:** point-and-click selection, movement and contextual orders for protagonist and crew (not WASD action-RPG control).

**Fantasy:** As a vulnerable survivor with an aging working vessel, turn a desperate trawler/tug/research ship into a customized moving homestead and scrap warship. Explore a partially frozen maritime world under pressure from an expanding cold, humans, and biologically altered unknowns. Your ship and crew acquire personal history through risks, losses and repairs.

## Non-negotiable pillars

1. **Ship as a persistent character.** Interior compartments and exterior equipment are spatial, staffable, damageable and visibly evolving; initial vessel is flawed, small, repairable—not a full city or arbitrary voxel raft.
2. **Cold and motion drive choices.** Fuel/heat/insulation, weather, moving ice, stamina, wounds and navigation interact. Staying indefinitely in one rich location is not a safe winning strategy.
3. **Selectable protagonist, consequential crew.** Player is an embodied persistent character with distinct skills/equipment, but you command them with the same click-to-move/action interface as other selected units. Crew typically ~8–30, each with health, morale, traits, relationships, professional and expedition skills, schedules, training and mortality.
4. **Two connected real-time command theaters.** Click-command ground exploration/combat and tactical ship-to-ship engagements feed back into the survival economy. Guns use scarce ammunition; injuries cost treatment; damaged engine means reduced maneuverability and heat.
5. **Systemic sandbox, optional narrative.** Factions, distress signals, contracts, rumors, scientific/industrial sites and competing accounts of the calamity enrich exploration, not a mandatory main-quest spine.
6. **Nature / Humans / Unknown.** Nature (ice, storms, illness), people (desperate communities, raiders, rival crews), and biopunk anomalies (mutations, experiments, contamination) pose qualitatively different threats.
7. **Readable stylization.** Good-looking low-poly silhouettes and crisp/pixel-art-like surface textures, attractive lighting, clear tactical readability; explicitly avoid deliberate PSX vertex snapping/visual instability.

**Reference blend:** Last Train Home (real-time selection/squad expeditions and moving home) + Frostpunk (pressure and prioritization) + Fallout Shelter (persistent crew/room management) + Fallout 1/2 (top-down contextual world interaction, not mandatory turn-based combat). [Accepted control contract](control-and-interaction.md).

## Core world loop

Navigate → Scout → Fight/Avoid → Dock/Land → Explore/Scavenge/Hunt/Rescue → Return → Treat/Repair/Manage Crew → Craft/Upgrade/Trade → Choose Route. No artificial separation of action consequences and management.

## What this game is not

- Not a first-person Raft clone, traditional city builder, turn-based XCOM, twin-stick/WASD action RPG, or pure tower defense.
- Not an ocean physics/destruction tech demo; ice-breaking and weather may be credible abstracted systems with strong feedback.
- Not a fully linear quest sequence where ignoring the main story suspends simulation.
- Not designed around a disposable prototype architecture.
- Not committed to any unapproved final squad cap, class lock, ending format or global character level system.

## Targets and constraints

Desktop first (Windows, Linux, macOS), then mobile, web, PC handhelds, and eventually consoles with platform-specific support. One simulation/gameplay truth with platform UI, renderer and control adaptations. Solo/small-team/cloud-agent-friendly production demands bounded scenes, tooling, procedural/data-driven content and independent C# tests.

## Success criterion

The player makes meaningful, legible trade-offs across voyage, ship and expedition; losing one engineer or a heat system failure tangibly changes possibilities; ship progression is visible; a session generates a distinct story without a forced campaign.
