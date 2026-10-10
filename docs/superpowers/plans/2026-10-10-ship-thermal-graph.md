# FrostArk #24 — Ship compartments, boiler, fuel, and conservative thermal graph

Date: 2026-10-10; starting main 74d734aea6db0cc1ff265e6b09d5c6eda99965e2. Owner explicitly authorized read-only GitHub Actions for G0/G1; no ShellWarden, local edits or unverifiable Unity claims.

**Goal:** A cold night is a consequential, repeatable fuel/insulation/door decision in the ship's *canonical* WorldState. Heat conservation must be independently auditable, not simulated by Update or Unity physics.

## Design
- All thermal energy uses checked signed `long` heat units. Temperatures use integer milli-Celsius = energy / positive heat capacity. An exchange between rooms transfers exactly `q` units, preserving total energy; ambient is an explicit outside reservoir. Boiler adds `energyPerFuelUnit * fuelConsumed` and withdraws the same fuel units from the `WorldState` supply `supply:fuel` atomically.
- Room contract: stable ID, capacity, energy, exposure conductance, insulation [0..1000], integrity [0..1000], cold streak minutes; edges undirected, unique canonical room pairs with conductance. Every minute process interior edges in lexicographic order from pre-tick temperatures, then weather exposure per room, then boiler. Document and test the *exact* rounding/clamping. Negative temperatures are allowed. At < 5000m°C for >= 30 consecutive minutes: cold warning. At < -5000m°C for >= 90 minutes: critical exposure warning; crew casualties belong to later medical/crew state, do not fabricate them.
- `ShipThermalState` immutable. `ShipThermalModel.Step(thermalState, ambientMilliC, fuelAvailable)` returns `ThermalTickResult` with next state and the ledger: fuel consumed, heat added, signed exterior transfer, total before/after energy. Check `after-before == heatAdded+exteriorTransfer`, and assert externally across tests.
- `WorldState` owns optional thermal state (Empty/default is permitted for old saves), supplies; a `ShipThermalMinuteSystem : IWorldMinuteSystem` is injected into the existing `SimulationScheduler` with a `Func<WorldState>`/ `Action<WorldState>` authority port. It is NOT a second state manager. Tick is exactly one world minute, with validated monotonic time and deterministic provenance/revision if state changes. No parallel Update/FixedUpdate clocks.
- `FARKSAVE` version 4 appends sorted thermal state and static boiler/edge config after existing v3 inventory payload. Version 1/2/3 readers migrate missing thermal data to an explicit empty state; no made-up heat or fuel. Restore then continue must reproduce uninterrupted state and ledger.
- No UnityEngine in Core. PR #52 is a distinct still-Draft Unity bridge; do not duplicate or merge it without Unity G2 import+EditMode evidence.

## TDD ledger
1. [ ] RED for heat-transfer conservation, fuel, exposure, warnings, invalid graph and overnight behavior.
2. [ ] GREEN implement Core/Ship/Thermal domain types and meta GUIDs; run G0 + G1 via Actions with actual job logs.
3. [ ] RED/GREEN integrate *canonical* WorldState supplies and scheduler one-minute tick; validate 60×1 = 1×60 and interrupted sleep fuel totals.
4. [ ] RED/GREEN version 4 save codec with strict v1-v3 migration, byte-stable round-trip and resumed multi-hour thermal trace.
5. [ ] ADR includes explicit heat/fuel balance table for closed vs open/insulated compartments, warnings and approximations. Exact-head scope review, real CI counts, merge if and only if requirements and checks met; G2/G3/G4 remain NOT RUN.

**Boundaries:** Crew fatalities are not fabricated from abstract exposure; warnings and severe-cold streaks are the accepted early consequence until Crew/medical system. No CFD, Unity physics, virtual heat leaks, realtime physics, or untested hardware target acceptance.
