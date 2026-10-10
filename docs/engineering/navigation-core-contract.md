# FrostArk #33 — Navigation Core Contract and Verification

**Status:** Core-first pure C# implementation on PR #59. User explicitly approved written [design](../superpowers/specs/2026-10-10-sea-navigation-core-design.md) and [Native TDD plan](../superpowers/plans/2026-10-10-navigation-core-implementation.md) on 2026-10-10.

## Intent / scope boundary

The ship can follow a pre-authored connected chain of directed sea legs while weather, pack ice, engine condition, hull and fuel have deterministic consequences. This **is not** player-operated physical ship handling. Real-space Unity helm/collision/ice graphics and actual device evidence are tracked in [#60](https://github.com/MohamedXIV/Frostark/issues/60); modular ship deck visual appearance is separately tracked in #58. Core uses the existing canonical WorldState, WorldClock and scheduler, never a scene-owned time/economy ledger.

## Authoritative state and route planning

- `WorldState.Navigation: ShipNavigationState` owns the immutable authored graph and current position/route/hull. Waypoint IDs and directed legs are stable and sorted canonically by ID; active leg order is preserved as an ordered path.
- Waypoint X/Y are **bounded authoring units** (±1,000,000), not claimed physical meters. Distances are 1–1,000,000 authored units; ice, storm and visibility are 0–1000 permille. `NavigationTuning` stores explicit fixture parameters (speed, fuel per unit, ice/storm drag, safe ice threshold and hull wear), not accepted global difficulty tuning.
- `PlanShipRouteCommand` and `CancelShipRouteCommand` validate location, connected directed legs, duplicate IDs, current waypoint and mid-leg placement. Stale/duplicate command IDs retain the identical WorldState pointer and no event; successes append event provenance and exactly one player-command revision.
- `ShipLayoutState.PropulsionPermille` is the *only* engine capability input to travel; no second engine durability value. Hull integrity is separate, ship-wide. A navigation state migrated from an old save is deliberately empty/unlocated and cannot travel until authored.

## One clock / heat-first fuel

`ShipTravelMinuteSystem` is an `IWorldMinuteSystem` in existing `SimulationPhase.Travel = 50` (after `Crew = 10`, `Heat = 30`). Travel demands `WorldState.Minute == tickMinute` rather than advancing its own minute; `ShipThermalMinuteSystem` advances the same minute and has prior claim to boiler fuel.

In a moving minute, effective progress is floor of base speed × engine permille /1000, then ice/storm drag, bounded by leg remainder. Movement does not round zero speed up to a free unit. Fuel due = `distanceMoved * fuelPerUnit + ceil(distanceMoved * fuelPerUnit * icePermille / 1000)`, deducted atomically from the **remaining** `supply:fuel`; insufficient fuel means no movement, fuel debit or new damage. While moving, hazardous ice above `safeIcePermille` and storms cause explicitly tuned integer hull damage; hull=0 blocks further movement. Completing a leg updates the current stable waypoint and leg index; ending a plan remains a persisted replayable state. No unseeded RNG or free teleport at a scene boundary.

**LOD:** Active scene may interpolate/render position; off-scene/fast-forward/interruptible sleep uses the same per-world-minute authority. Real-time scene kinematics and ice deformation are outside this Core PR.

## Persistence and constraints

SaveCodec **v7** appends a bounded, versioned navigation-present flag and graph/route fields after v6 module data, preserving historical bytes and ID ordering. v1–v6 saves load with `ShipNavigationState.Empty` and no invented position/hull. Active route rehydration validates IDs, chain connectivity, current waypoint against active leg index, progress below leg length, range checks and legitimate zero hull. Recomputed SHA-256 alone does **not** authorize semantically invalid nav. SHA-256 is an accidental-corruption detector, not a tamper-proof signature.

**Global design decisions unchanged:** D019 world-time multiplier and D022 world/map scale remain OPEN. The fixture distances and fuel/hull coefficients aren't final world-balance decisions.

## Real verification (not Unity)

- Original navigation graph RED: [Actions 38051241438](https://github.com/MohamedXIV/Frostark/actions/runs/38051241438) G0 PASS, .NET missing-type G1 FAIL as expected → [38051374213](https://github.com/MohamedXIV/Frostark/actions/runs/38051374213) **103/103 NUnit G1 PASS, 7/7 Python G0 PASS**.
- Route command RED: [38051438544](https://github.com/MohamedXIV/Frostark/actions/runs/38051438544) missing commands → [38051503418](https://github.com/MohamedXIV/Frostark/actions/runs/38051503418) **106/106 NUnit PASS, 7/7 G0 PASS**.
- Travel RED: [38051599738](https://github.com/MohamedXIV/Frostark/actions/runs/38051599738) missing Travel system → [38051673337](https://github.com/MohamedXIV/Frostark/actions/runs/38051673337) **112/112 NUnit PASS, 7/7 G0 PASS**.
- Save v7 RED: [38051767494](https://github.com/MohamedXIV/Frostark/actions/runs/38051767494) 5 expected Save assertion failures. First Save implementation [38051912077](https://github.com/MohamedXIV/Frostark/actions/runs/38051912077) 116 PASS/1 FAIL exposed uncaught `InvalidDataException`; corrected with TDD and [38051977279](https://github.com/MohamedXIV/Frostark/actions/runs/38051977279), **117/117 NUnit G1 PASS, 7/7 Python G0 PASS**, GitGuardian success on exact product head `05dcdb43655b1ce6792c17e789424c49da69ef25`.
- **Unity G2 Editor/package, G3 PlayMode/physical ice and helm, G4 desktop/mobile/WebGL/device checks: NOT RUN.** No screenshots, ships in physics scenes, or platform builds are claimed. The PR is not production-accepted as a physical ship feature.

## Future work

[#60](https://github.com/MohamedXIV/Frostark/issues/60) must stay open for actual in-scene ship control, helm input, visible snow/ice/visibility, collision consequences, save-synchronized transforms and target-platform acceptance. [#58](https://github.com/MohamedXIV/Frostark/issues/58) is the separate modular deck visual adapter. Other broader systems (sea combat #34, ice/world generation #35, UI #39) depend on appropriately verified platform gates as documented in those issues.
