# TacticalOrderIntent Core contract — 2026-10-11

**Design:** [accepted top-down click-to-command direction](../design/control-and-interaction.md).
**Tracking:** [#76](https://github.com/MohamedXIV/Frostark/issues/76), fixes the unsuitable `PlayerInputIntent` from #73 / PR #74.
This is a **transient** selection/order envelope, not an executed command, saved actor controller or Unity pathfinding substitute.

## API
`FrostArk.Core.World.TacticalOrderIntent` has three factories:

- `MoveTo(IEnumerable<EntityId> actorIds, double worldX, double worldZ)` for resolved world-plane coordinates.
- `InteractWith(IEnumerable<EntityId> actorIds, EntityId targetId)` for an interaction with a persistent entity.
- `AttackTarget(IEnumerable<EntityId> actorIds, EntityId targetId)` for requested engagement with a persistent entity.

`ActorIds` is a defensively copied, read-only list sorted by ordinal stable ID, with invalid/duplicate/empty selections rejected. Move coordinates must be finite but **are not proven walkable**. Entity actions require valid target IDs. `Kind`, `HasDestination`, `HasEntityTarget` and target/destination properties distinguish mutually exclusive target shapes.

**No `MoveX/MoveY` axes, WASD controls, `AimX/AimY` reticle, `FireHeld`, actor physics, direct damage, UI selection persistence, bespoke simulation clock or second authoritative resource state.**

## Unity adapter responsibility (NOT implemented in this PR)

1. Pointer/controller focus selects one/more scene actors; resolve to persistent `EntityId` using the session's authoritative character mapping. **Both the protagonist and crew** may be selected.
2. Click ground or entity, raycast with Unity camera/physics, translate to stable world X/Z (camera/pitch agnostic at Core boundary) or target ID; expose contextual action availability.
3. Create `TacticalOrderIntent`, then reject dead/incapacitated/non-ownable actors and invalid or unreachable/out-of-range targets at the application/scene executor. Resolve pathing through Unity AI Navigation; provide visible failure feedback. Group execution should not imply units teleport or form perfect formations.
4. Only validated gameplay consequences (salvage, ammo spending, wounds, crew assignment, ship changes) pass through canonical WorldSession / WorldCommandBus with actor identity and provenance. **AttackTarget is a request**: it does not bypass cooldowns, aim, LOS or consume one shot per frame.
5. Command selection/hover/path preview stay transient; world items, crew and encounter consequences are persistent. Scene transitions re-resolve actor proxies by stable IDs.

## Verification gates

- G0: shared Core source remains Unity-independent; C# asset has stable paired Unity `.meta`.
- G1: independent NUnit tests for solo/group actor selection, deterministic order, non-mutable inputs, target-type distinction, invalid actors/targets and non-finite points. Run full .NET suite after RED failure.
- **G2/G3/G4 not established here.** [#21](https://github.com/MohamedXIV/Frostark/issues/21) owns actual Unity import, input action bindings, selection/highlighting, click navigation/interaction and PlayMode proof. [#32](https://github.com/MohamedXIV/Frostark/issues/32) owns advanced squad behaviors/formation. [#20](https://github.com/MohamedXIV/Frostark/issues/20) still tests camera tilt.

Saved WorldState and SaveCodec are unchanged: the removed axis payload was never authoritative or serialized. The historical PR #74 remains in Git history for audit but is superseded, not a shipping player-control contract.
