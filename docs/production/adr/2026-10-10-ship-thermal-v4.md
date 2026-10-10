# ADR — Ship thermal graph and version 4 saves

**Status:** Implemented in #24, pending final exact-head merge review. Date 2026-10-10.

## Canonical ownership and scheduling

One `WorldState.Thermal` snapshot owns every ship room, edge, boiler and cold-exposure counter. `ShipThermalMinuteSystem` implements the existing `IWorldMinuteSystem` at `SimulationPhase.Heat`, reading the session-owned state and publishing one new immutable `WorldState` per world minute through injected callbacks. Only `SimulationScheduler` advances the authoritative `WorldClock`. Neither UI frame delta nor Unity physics drives fuel consumption.

**Important revision distinction:** `WorldState.Revision` and `processedCommands` track *validated commands*, not passive heat ticks. A 30-day cruise cannot generate 43,200 fake command IDs and exceed the save codec's bounded command list. A thermal tick changes the world minute, room energy, cold streak and fuel stock atomically while leaving command revision unchanged. All thermal state nevertheless participates in the deterministic WorldState fingerprint and save payload.

## Quantities and conservation

Temperature is integer milli-Celsius derived from signed thermal energy / positive heat capacity. Heat energy units are purely game-model units, *not* Joules or a real-world heat transfer certification. A room has stable ID, energy `E` (signed int64), heat capacity `C>0`, exposure conductance, insulation [0–1000], integrity [0–1000] and uninterrupted cold exposure minutes.

Within a minute, every undirected room connection is sorted by canonical IDs. From pre-tick temperatures `T_a = E_a/C_a`, `T_b = E_b/C_b`, flow `Q_ab = trunc((T_a-T_b) * conductance/1000)`, bounded to half the difference times the minimum room heat capacity. Subtract exactly `Q_ab` from a and add exactly `Q_ab` to b. Internal exchange therefore cancels in the ship-wide energy ledger.

Exterior exchange for each room is signed, positive into the ship:

```
outside = clamp((ambientMilliC - T_room)
    * exteriorConductance
    * (1000 - insulationPermille)
    * (2000 - integrityPermille) / 1_000_000_000,
    -abs(ambientMilliC - T_room) * heatCapacity,
     abs(ambientMilliC - T_room) * heatCapacity)
```

All operations use checked integers and deterministic truncation. Ambient weather is an explicit outside reservoir, not hidden heat generation. Boiler burns `min(burnUnitsPerMinute, authoritativeSupply["supply:fuel"])`, injecting `burnedUnits * energyPerFuelUnit` into its authored room and decrementing exactly the same units in WorldState. It cannot generate heat without stock. Per-minute proof:

```
totalEnergyAfter - totalEnergyBefore
    == boilerHeatAdded + signedExteriorTransfer
```

The solver throws if this equality is violated. Rounding is performed only on flow calculations; room state persists *energy*, never a rounded temperature. The chosen conductance and heat yields are placeholder balance parameters for authored data, not scientifically calibrated buildings.

## Hazards, rationing and door models

Weather exposure, integrity, insulation and adjustable edge conductance create meaningful thermal consequences while sleeping. An edge with zero conductance is a closed insulating connection (e.g. shut bulkhead), a nonzero edge is a thermal passage. Below 5°C for 30 consecutive minutes produces `Cold`; below -5°C for 90 consecutive minutes produces `CriticalCold`. Recovery above the low-temperature threshold resets the counter. **No artificial crew deaths are invented** before crew health/injury actors are integrated; the initial acceptance for #24 is explicit cold warnings, not simulated casualties without actual crew membership.

Sleep until 06:00 advances real reference world minutes, not accelerated Unity physics, and stops at the first interrupting raid/weather event: fuel and temperature stop at the precise interruption minute.

## Save v4 and backwards compatibility

The v4 `FARKSAVE` binary envelope preserves the prior v3 payload and appends: bounded ordered rooms (ID, capacity, energy, exterior conductance, insulation, integrity, cold streak), ordered connections (two stable IDs, conductance) and boiler configuration (room ID string or empty if disabled, burn units/min, heat/fuel). The 28-byte empty graph tail is two empty collection counts plus one empty boiler ID and two zero long rates. Decoding validates identities, duplicates, graph closure, physical scalar bounds and checksum. v1/v2/v3 decode to an explicit **empty** thermal graph instead of fabricated fuel/heat history. Exact binary encoding is stable for identical graphs regardless of authoring list order.

Integration gates: G0 Python checks the shared sources and Unity metadata; G1 NUnit tests actual thermal conservation, cold/fuel behavior, batch reference equivalence and save/reload continuation. Neither gate proves Unity Editor import G2, Unity PlayMode G3, device/IL2CPP G4, or real-world thermodynamics. PR #52 Unity VContainer bridge remains independently Draft and G2-blocked.
