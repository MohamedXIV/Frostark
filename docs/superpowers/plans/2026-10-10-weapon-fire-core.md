# FrostArk C01a (#65) — Core weapon fire as a single authoritative transaction

Date: 2026-10-10. Base: 955ff74f40594fa4193051019b1c8c4682307506. #27 remains native-blocked by #21 Unity aim/locomotion. This is an independently verifiable domain prerequisite, not an attempt to close the parent.

## Contract
- `WeaponDefinition` has stable ID matching its carried Pack item, ammo item ID, positive rounds/shot, positive cooldown in fixed combat ticks. Authoring definitions unique and validated; no separate phantom ammo balance.
- `WorldState.Combat` stores immutable weapon definitions, global combat tick and each weapon's next ready tick + accepted shot count. A combat tick is an abstract fixed step for near/active tactical simulation, **not** a new calendar or an unconstrained client timestamp; Unity physics/input adapter must drive/authorize it, G2/G3 deferred. A weapon's 1-3 tick cadence permits rapid shots in the same integer WorldMinute; never throttle a gun to one bullet/minute.
- `FireWeaponCommand` validates carried weapon stock and ammo item IDs in Pack, positive amounts, ready tick and idempotence; in ONE `WorldState` revision it deducts actual Pack ammo via existing `TryConsumePack`, marks a weapon fired and emits `WorldEventKind.WeaponFired` with command/weapon/ammo/round quantity. No hit or wound is created from an unvalidated client target.
- `WorldState.AtCombatTick` is a pure projection for a trusted session cadence and does not touch WorldMinute or inflate command revision. It only increases tick and may not rewind. Separate persisted tick from world clock.
- SaveCodec v10 appends sorted CombatState snapshot (combat tick and weapon definitions/stored shot state); v1-v9 migrate to explicit empty combat without phantom shots. Reject duplicate weapons, invalid cooldown, negative counters and implausible ready tick.
- TDD RED for stock conservation, cooldown, replay, save/restore, invalid roster/content IDs, stale revisions. Verify hosted G0 and G1 real logs, then focused review and merge. Do not claim #27 or Unity PlayMode completion.

## Boundaries / follow-ups
Physics/raycast hit validation, recoil and animations, weapon magazine/reload, wounds and armor, cold combat and actual player input remain #27, blocked on #21. Only trusted Unity session should call combat tick stepping; no GameObject owns authoritative ammo or shot counters. The public Core-only stepping seam is deliberately not user network input. No ShellWarden/local PC, paid workflows or hidden test assumptions.
