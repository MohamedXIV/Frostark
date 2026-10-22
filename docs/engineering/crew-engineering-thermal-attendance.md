# E11 #112 — On-stage Engineering/boiler productive attendance

**Status:** implementation in stacked **[Draft PR #124](https://github.com/MohamedXIV/Frostark/pull/124)**, child of #123 (RepairJob attendance), in turn child of #122 and the previously Unity-validated #121. Sibling #120 (accepted Move persistence) and main are **unchanged**. This child requires exact-head Core CI and Unity 6000.6.0f1 acceptance before any merge.

## Why thermal is distinct from generic Work

The canonical `ShipThermalMinuteSystem` derives boiler efficiency from the actually staffed **Engineering** role each WorldClock minute, multiplies it by the ship layout's boiler integrity, and calls the pure `ShipThermalModel.Step` for heat, fuel and exterior energy ledger. In the original world simulation, a nominally scheduled Engineer could produce boiler heat while walking to their physical station, regardless of where their active ship actor was.

This slice applies the already-proven #123 transient physical presence mechanism to **Engineering boiler effectiveness**. It deliberately does **not** alter the canonical time/Heat phase order, numerical heat reducer, fuel stock accounting, crew fatigue, or create new jobs.

## Implementation

- `CrewState.StaffedCrewFor(DutyRole.Engineering, minute)` returns the identity chosen by the **existing fixed-priority `Staff(minute)` method**. If an Engineer is absent and a qualified Watch backup is staffed, the backup's identity is used; we do not infer the nominal Engineer's location or choose a second worker.
- `ShipThermalMinuteSystem` accepts an optional `(crewId, role, WorldMinute) => bool` production authority from the single `WorldSession`, just like `ShipCrewMinuteSystem` does for Repair. When an actual staffed Engineer exists and attendance is rejected, their effective boiler contribution is **0** for that minute. That means boiler fuel and boiler heat are **not** invented; passive exterior heat transfer and conservation still proceed normally. The existing `ShipThermalModel` remains untouched.
- When a ship scene is loaded and a physical Engineering **Work** station is **authored, placed and assigned** to the staffed member, the session requires a valid proof from the **immediately previous WorldMinute** and a matching unexpired exclusive Work slot claim. The Unity `ShipCrewDutyAutonomy` already only attests a genuinely stationary worker at an authored arrival cell, with no player override, pending movement or slot mismatch. The same proof can authorize Repair and Engineering independently, but only the member actually staffed for the corresponding role receives the effect.
- **Backward compatibility for unfinished authored content:** when the ship has no placed, assigned Engineering Work slot for that staffed member, its traditional canonical skill/boiler function remains active, even with a loaded ship scene. This avoids silently freezing heating in older ships with no physical station content; it is **not** proof of actual on-stage operation. As soon as an actual Engineering station exists, presence becomes mandatory. The user-facing production scene must eventually author real stations for complete realism.
- Outside the loaded ship observer, and across Save/Restore before scene reattachment, simulation remains the existing deterministic offscreen model; one Unity frame never authorizes 20 productive WorldClock minutes. No new saved attendance field, unit clock, or physical Transform is introduced.

## Tests authored (not Unity-executed yet)

- `CrewEngineeringAttendanceTests` 4 Core G1 regressions: unarrived Engineer adds **zero** heat/fuel while observed, then arrived/staffed member produces canonical heat and consumes exactly one fuel; leaving station suspends heat; one attestation grants only one productive WorldMinute during batch advances and ordinary offscreen operation resumes; authored-station-free legacy ship remains functional; an absent nominal Engineer cannot substitute for the actually staffed Watch backup.
- `ShipCrewDutyAutonomyEditModeTests.BoilerHeatRequiresStaffedEngineerAtAuthoredWorkStation` adds a Unity G2 fixture using a real `ShipGridWalker`, exclusive Work slot and thermal WorldState: travel yields zero heat, arrived authorizes a minute, player selection interrupts, and rearrival resumes work. This G2 source is **authored, not run in Unity**.
- G0 static contract guards the shared staffed identity, existing thermal phase injection, assigned Work source and Unity metadata.
- #123 previously passed **317/317 Core G1** and 27/27 G0 at SHA `41cfc62b`, but parent success is not evidence that this new Engineering thermal code compiles or passes; new CI must be checked separately.

## Remaining limitations and acceptance

- Engineering boiler productivity is governed by a **single staffed Engineer** at a time, consistent with the existing `EffectiveSkill(Engineering)` mechanics. This is not a multi-worker boiler boost system.
- Legacy ships without physical Work stations retain old offscreen-style boiler behavior while on-stage. A future staged content pass must add assigned Work stations before claiming universal physical presence parity.
- Work claim renewal from #122 is still evaluated by the Unity scene adapter during normal frames. A 20-minute batch advance while the scene stays loaded may expire its Work lease; after expiry, no phantom on-stage boiler heat is granted. Explicit on/offscreen fast-forward handover remains open.
- Shore duty, ship/shore transitions, #120 persistent player orders, deliberate scenes with real authored heating operator controls, OS-level mouse/HUD and platform G4 remain unverified.
- **No child Unity tests have passed yet.** Run clean compilation, new Engineering G2 and all existing #121/#122/#123 G2/G3, plus full Core; measure a real boiler ship while the operator travels, arrives, moves, becomes incapacitated or is replaced by a backup.

All branches remain Draft. Do not change #120 while separate QA/integration work continues.
