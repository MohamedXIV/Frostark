# FrostArk — Top-down command-and-control contract (2026-10-11)

**Status: Accepted gameplay/control direction by owner, 2026-10-11.**
This document overrides earlier suggestions of WASD movement, direct mouse-aim shooting, and a protagonist-first action RPG loop. It does **not** freeze camera pitch, squad cap, button bindings, ship steering physics, or pause/slow-motion mechanics.

## Reference blend, not a license to copy

- **Last Train Home:** select individual members or squads, issue real-time tactical orders and manage a vulnerable moving home between expeditions.
- **Frostpunk:** severe cold, infrastructure/fuel priorities, consequential scheduling, visible systemic trade-offs.
- **Fallout Shelter:** inspectable room/compartment operations, assigned staff, living crew and upgrades within one mobile vessel.
- **Fallout 1 & 2:** top-down readability, context-sensitive point-and-click world interaction, characters and a systemic RPG world. **Not** a mandate to use turn-based combat.
- **FrostArk identity:** a low-poly/pixel-textured arctic ship that travels through ice, explores islands and survives hostile people, anomalies and environmental threats.

## Non-negotiable player interaction model

1. **One top-down/near-isometric world camera family.** It looks down on on-foot characters, ship scenes and tactical environments; tactical visibility and room readability win over cinematic close-up action. Fixed vs adjustable tilt is still an art/occlusion experiment in #20.
2. **Select → inspect → command → observe.** Click a persistent character (including the protagonist) to select; support single or multiple selection, explicit selection feedback, and context-sensitive commands. A click on passable ground orders the selected character(s) to move there; clicking an entity offers/executes a valid interaction as appropriate. Selection and order UI must not mutate health/fuel/inventory itself.
3. **The protagonist is selectable, not a WASD-driven pawn.** The player character has distinct persistent identity, stats, skills, equipment and stakes, but is directed through the same click-to-move/action authority as companions. Solo party = select the protagonist and issue orders; squads may contain that protagonist and chosen crew. Direct keyboard-axis walking or mouse-look firing is **not** baseline control.
4. **Real-time combat remains.** The player orders movement, attack, attack-move, take cover, focus target, hold or retreat; actors execute with tactical AI and Unity locomotion/navigation. Weapons/ammo/wounds remain authoritative WorldState commands/events; an input sample is never an automatic per-frame fire command. Target visibility, line-of-sight, cover and path feasibility require engine evidence.
5. **Scene interactions are contextual.** Ground, salvage, doors, ship workstations, crew, enemies, station UI and world map have different meaningful targets/actions. Invalid actions are rejected or made unavailable with an explanation; clicking a dead/endangered/inaccessible target does not teleport a unit or fabricate resources.
6. **The vessel is a commanded moving home.** Inspect compartments, allocate crew, queue repairs/workshop jobs, manage heat/fuel and plan the voyage via map/helm waypoints and on-ship controls. Do not assume WASD ship or crew piloting; exact manual helm modality remains to be designed/tested. Fallout Shelter-style room inspection is not a separate economy, clock or authoritative simulation.
7. **Mouse is a default PC interaction surface, not the only platform.** A conventional selection-click + contextual/ground-order click arrangement is the baseline **interaction semantics**, while final left/right button assignments, modifiers, controller radial/focus navigation and touch gestures need usability tests. Camera pan/zoom/rotate bindings remain distinct from unit movement commands.

## Contract boundary — Unity vs independent C#

```text
Pointer / touch / gamepad selection (Unity Input System and UI)
 -> Camera hit test, world-point and entity-id resolution (Unity physics)
 -> Selection model (transient, presentation/application)
 -> Validated tactical intent {selected actor IDs, order kind, entity target OR world point}
 -> Scene executor / path feasibility / local AI (Unity adapters)
 -> Authoritative command/result when supplies, injuries, combat, crew jobs,
    inventory, ship layout or world simulation actually change (WorldSession)
 -> Derived UI/presenters and scene proxies
```

- Input intent is **orders**, not normalized `MoveX/MoveY/AimX/AimY/FireHeld` player axes. A previous Core contract of that shape was merged in PR #74 and is superseded by this decision; see follow-up refactor.
- Selection/highlight/camera hover are not serialized into `WorldState` and do not advance world clock.
- Persistent selected actors must be identified by stable `EntityId`; scene objects are proxies. Move/target coordinates must be validated and cannot be trusted as reachable until Unity navigation checks them.
- Route, salvage, combat and crew state transitions **reuse** canonical commands and persistence, without inventing an independent squad state authority or per-frame calendar.
- Pure C# may validate, normalize, order and test tactical requests; Unity Editor/PlayMode is required for actual physics, NavMesh, click-hit accuracy, rendering and multi-unit animation.
- World simulation remains deterministic where defined; Unity local movement interpolation and avoidance need not be cross-platform bit-exact.

## Slice-oriented gameplay acceptance

### R1 playable slice (first priority)

- One selectable protagonist on a top-down test landing scene; click the ground and visibly walk via Unity navigation (no WASD); select one crew member, then both; issue a contextual salvage action; return aboard without duplicating loot or losing world-state changes.
- Show a real ship compartment with assignable crew and fuel/heat readout; a time advance changes the existing shared `WorldSession` state.
- One simple threat may demonstrate ordered avoidance/engagement; do not claim a tactical combat system before the actor/weapon/path integration is in-engine.

### Combat / squad milestone

- Selection and group orders work for solo/mixed-role parties; attack-move, hold, retreat and cover are explicit, contextual orders. Manual tactical control does not require micromanaging every frame.
- AI respects orders and local hazards, uses navigation/LOS/cover where proven, and pays finite ammo and damage through authoritative commands. Both success and retreat are legitimate outcomes.
- No fixed `player + 3` roster cap unless separately accepted after readability/performance validation.

### Ship / sea milestone

- Room interactions, duty assignments, heating priorities, upgrades, dock/undock and plotting routes share the same persistent crew/ship data and clock.
- Top-down vessel/ice presentation and naval combat must be tested in Unity separately; the ship is not a first-person or joystick-only minigame.

## Explicit exclusions and still-open design choices

- No mandatory WASD, twin-stick direct aiming, action-RPG possession mode or Fallout 1/2 turn-based combat.
- No locked squad size, RTS-like construction economy, pixel-perfect 2D mandate, automatic squad pathfinding implementation in Core, or guaranteed cross-platform mouse gestures.
- Open: exact isometric tilt/zoom, selection modifier/button defaults, order queueing, speed controls and tactical pause, controller/touch parity details, playable protagonist succession after death, direct ship-helm control precision.
- All completion claims must distinguish G0/G1 pure-C# evidence from Unity G2/G3 and target G4 proof.

## Implementation migration — status 2026-10-11

- **Docs complete:** PR #75 merged the accepted control direction, decision register D017/D027, visual/crew/ship/architecture/roadmap documentation.
- **Issue migration complete (baseline):** #21 (selected-protagonist Unity click locomotion/interaction), #32 (advanced squad orders), #27–#30 (ordered combat, AI and cover), #20 (camera click-target readability), #22 (ship/shore click extraction), #39 (tactical management UI), #34/#58/#60/#64 (top-down ship/naval/station adapters) and relevant parent epics. Remaining work is implementation, not a second controller design.
- **Core correction complete:** [PR #77](https://github.com/MohamedXIV/Frostark/pull/77) / [#76](https://github.com/MohamedXIV/Frostark/issues/76) replaced the superseded `PlayerInputIntent` axes contract from PR #74 with `TacticalOrderIntent`. [Core handoff](../engineering/tactical-order-intent.md) explains its typed ground/entity targets and Unity adapter obligations. Saved WorldState / SaveCodec did not change for transient UI input.
- **Unity still unverified:** #19/#20 need Editor checks; #21 needs physical click-navigation/interaction G2/G3; #32 needs real tactical scene/AI evidence. No completed Core test implies that the game is playable yet.
