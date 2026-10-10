# C01a — Authoritative Core weapon firing and combat ticks

**Date:** 2026-10-10. **Issue:** #65, **PR:** #66. **Parent:** #27 (still blocked by Unity locomotion/aim #21). **Status:** Core implementation subject to exact-head G0/G1 review; not playable Unity combat.

## Why separate combat ticks from world minutes

WorldMinute is the sole integer-minute world calendar for travel, crew and weather. The game has real-time top-down combat, so authoritatively limiting one shot to one WorldMinute would be an unacceptable permanent contract. CombatState instead has a monotonic, integer **tactical fixed-step counter**. A trusted gameplay session/physics adapter must advance this tick at an agreed fixed interval; it is a local action cadence, **not** a second world calendar or an extra time source for food/fuel/weather. One WorldMinute may contain many tactical ticks. Advancing tactical ticks is an immutable WorldState projection and does not fabricate command provenance or increment command revision. Unity G3 must validate the actual timing and raycasts; fixed-tick numerical duration and input rules must be specified by that adapter, not guessed from tests.

## Authoritative transaction

WeaponDefinition has stable carried item ID, distinct stable ammo item ID, positive ammo units/shot and positive cooldown in tactical ticks. Both IDs must exist in canonical InventoryState's ItemCatalog, and the weapon itself must be possessed in Pack. CombatState contains only authored definitions, per-weapon shot count and next ready tick, **no second stock of ammunition**.

FireWeaponCommand is a revision-checked, idempotent action in WorldCommandBus. It validates command identity/revision, authored weapon, carried gun and cooldown first. It then consumes ammo only from InventoryContainer.Pack with existing atomic TryConsumePack. Only successful payment calls WorldState.WithWeaponShot, atomically recording new CombatState and InventoryState within **one** world revision, and emits one WeaponFired provenance event with the weapon ID, ammo item ID and rounds spent. Reject returns the exact old state and no event. ShipHold ammunition is not implicitly available on an expedition; player must explicitly load it into the carried Pack through a separate authorized inventory workflow.

A WeaponFired event certifies a paid shot intent **not a hit**. No target IDs or client-supplied claimed wounds/damage/line-of-sight are accepted. The Unity physics authority (raycasts, collision, ballistics, enemy health) and any damage-reduction/armor/wounds/retreat remain in #27 and must be G2/G3 verified after #21 aiming is available. This slice does **not** implement a magazine/reload workflow: rounds are consumed directly from carried Pack as a minimal authoritative ammo baseline; adding magazines must later preserve this single-transaction item accounting. It does not create a standalone weapon manager, a fake projectile, an alternate crew health state or replicated multiplayer authority.

## Persistence and migration

SaveCodec v10 appends a bounded fixed-tactical-tick int64 and a deterministic sorted weapon list after the entire v9 economy payload. Each weapon record contains the weapon item ID, ammo item ID, ammo consumption/shot, cooldown ticks, next ready tick and shots fired (all int64). Version 1–9 restore to explicit empty CombatState with tick 0; no shots/weapon unlocks are fabricated. Weapon definitions are cross-validated with actual authored inventory items during WorldState restore. Decoder rejects duplicates, invalid counters, impossible next-ready ticks (last shot must not be in the future) and aggregate shot count greater than the world's committed command revision. The SHA-256 envelope is a corruption check, **not cryptographic proof of player honesty**.

## Evidence and limitations

TDD RED started with absent Combat namespace on hosted G1 and later found persistent CombatState omitted by the old v9 codec. Tests include actual ammo deduction, gun possession, duplicate-command protection, cooldown across tactical ticks without advancing WorldMinute, stock exhaustion, save/reload continuation and backward migration. Additional checksummed negative/impossible weapon counter/cooldown payloads are intentionally rejected. Only inspect final exact-head GitHub Actions job logs before claiming green.

G0 structural checks and G1 .NET NUnit do not establish Unity compilation, PlayMode physics, framerate correctness, controller aiming or any G4 target platform performance. **G2/G3/G4 NOT RUN.** Keep parent #27 blocked by #21 until real Unity evidence is available.
