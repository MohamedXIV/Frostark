# Ground combat, squad, cover and tactical AI

## Combat experience

**Real-time player-led combat** with direct protagonist movement/aiming, AI companions executing intent, readable telegraphs, realistic scarcity of ammunition, wounds and time-to-exposure under harsh weather. Tactical agency must not require RTS-level micromanagement. Encounters support retreat, avoidance and partial success.

## Expedition squad proposal

Candidate cap: **player + up to 3 companions**, subject to on-device readability and AI performance playtests. This is not locked. Players may go alone, take several identical specialists, or mix roles; no mandatory assault/medic/engineer slot. Abilities, gear, risk and interactables generate **soft** reasons to bring certain characters.

Candidate orders: Follow, Hold, Move, Take Cover, Focus Target, Engage/Avoid, Retreat. Issue orders via keyboard/mouse and controller wheel; mobile touch adaptations later. Optional slow-motion command mode is a candidate, **not a declared shipped feature**. Directly switching active protagonist control to other party members is undecided.

## Cover

Use Unity AI Navigation for coarse pathfinding and a custom **tactical cover layer**. Cover arises from actual blocking geometry, verified by line of sight/height, peek directions and exposure. Full/half are UI categories and animation behavior, not unconditional defense bonuses. Cache/register cover candidates with reservation to avoid agents occupying the same spot; optionally generate candidates in procedural scenes.

Candidate evaluation: threat line-of-fire exposure, path cost, shot availability, flanking risk, ally occupancy, retreatability and environmental hazards. Validation matters more than the label: a cover spot is unsafe against a flanking enemy.

## AI approach

Code-first **utility-based decision selection** supported by explicit state machines or limited behavior trees for execution. Each actor has an observable blackboard/perception memory (visual, sound, threat, last-known position, friendly positions, cover and health). Run tactical decisions on a scheduled budget (starting proposal 0.2–0.5 real seconds, staggered), not expensive scanning each `Update`.

Behaviors include search, advance, hold position, use cover, shoot, flank, suppress, coordinate locally, withdraw, heal/reload where appropriate, and disengage. Human raiders can reposition tactically; animals and engineered organisms have other senses/movement/attack styles. Awareness should not be omniscient.

## Architecture boundary

The tactical decision model uses queryable tactical snapshots and emits **intent** (target, move point, desired stance). Unity adapters perform NavMesh paths, sensors/raycast, animation, projectile/physics events and locomotion. Deterministic decision unit tests cover score ranking and intent; full combat behavior needs PlayMode/visual checks. Naval AI uses shared concepts but separate vehicle/navigation algorithms.

## Example acceptance encounter

One hostile shooter behind geometry; player and two companions have to move and find cover, be flanked and optionally withdraw. Assertions: visibility does not pass through solids, no dual occupancy, player orders can override AI without invalid actions, hit/ammo/wound state persists, enemy perception responds to sound and loses last-known location after search.

## Asset policy

Start with Unity AI Navigation plus our own small AI core. Evaluate Unity Behavior or commercial AI/cover assets only against explicit gaps, licensing and CLI/editor automation compatibility. Avoid purchasing a broad AI package before a working benchmark encounter identifies need.
