# Ground combat, squad, cover and tactical AI

## Combat experience

**Real-time, top-down click-to-command squad combat**. Select the protagonist, one companion or multiple units, then issue ground move, contextual attack/interact, attack-move, take-cover, focus, hold and retreat orders. The protagonist is not a WASD/twin-stick exception. Unit AI/pathing executes intent; combat offers readable telegraphs, scarce ammunition, wounds and harsh-weather exposure. Tactical agency must not require micromanaging every movement frame; a solo protagonist uses the same selection/order model. Encounters support retreat, avoidance and partial success.

## Expedition squad proposal

Candidate cap: **player + up to 3 companions**, subject to on-device readability and AI performance playtests. This is not locked. Players may go alone, take several identical specialists, or mix roles; no mandatory assault/medic/engineer slot. Abilities, gear, risk and interactables generate **soft** reasons to bring certain characters.

Candidate orders: Move, Interact, Attack, Attack-move, Follow, Hold, Take Cover, Focus Target, Engage/Avoid, Retreat. Select one or multiple persistent units and issue orders via contextual pointer clicks, with controller wheel/focus and mobile touch adaptations later. Optional slow-motion command mode remains a candidate, **not a declared shipped feature**. Selecting any crew member for orders is baseline; directly possessing one unit for manual action control is not. [Accepted contract](control-and-interaction.md).

## Cover

Use Unity AI Navigation for coarse pathfinding and a custom **tactical cover layer**. Cover arises from actual blocking geometry, verified by line of sight/height, peek directions and exposure. Full/half are UI categories and animation behavior, not unconditional defense bonuses. Cache/register cover candidates with reservation to avoid agents occupying the same spot; optionally generate candidates in procedural scenes.

Candidate evaluation: threat line-of-fire exposure, path cost, shot availability, flanking risk, ally occupancy, retreatability and environmental hazards. Validation matters more than the label: a cover spot is unsafe against a flanking enemy.

## AI approach

Code-first **utility-based decision selection** supported by explicit state machines or limited behavior trees for execution. Each actor has an observable blackboard/perception memory (visual, sound, threat, last-known position, friendly positions, cover and health). Run tactical decisions on a scheduled budget (starting proposal 0.2–0.5 real seconds, staggered), not expensive scanning each `Update`.

Behaviors include search, advance, hold position, use cover, shoot, flank, suppress, coordinate locally, withdraw, heal/reload where appropriate, and disengage. Human raiders can reposition tactically; animals and engineered organisms have other senses/movement/attack styles. Awareness should not be omniscient.

## Architecture boundary

The tactical decision model consumes player-issued orders and queryable tactical snapshots and emits **execution intent** (target, move point, desired stance). Unity adapters convert pointer hits to stable entity/world targets and perform NavMesh paths, sensors/raycast, animation, projectile/physics events and locomotion. Presentation selection is not saved in WorldState; verified gameplay consequences are. Deterministic decision unit tests cover score ranking and intent; full combat behavior needs PlayMode/visual checks. Naval AI uses shared concepts but separate vehicle/navigation algorithms.

## Example acceptance encounter

One hostile shooter behind geometry; select protagonist and two companions, order movement/cover/focus/fire, react to flanking and optionally withdraw. Assertions: visibility does not pass through solids, no dual occupancy, player orders can override AI without invalid actions, hit/ammo/wound state persists, enemy perception responds to sound and loses last-known location after search.

## Asset policy

Start with Unity AI Navigation plus our own small AI core. Evaluate Unity Behavior or commercial AI/cover assets only against explicit gaps, licensing and CLI/editor automation compatibility. Avoid purchasing a broad AI package before a working benchmark encounter identifies need.
