# Authoritative time, ticks, sleeping and simulation detail

## Time domains: do not conflate

| Domain | Driven by | Examples |
| --- | --- | --- |
| Presentation | Unity Update/LateUpdate | camera, UI, animation blending, VFX |
| Local real-time action/physics | fixed step and tactical scheduler | locomotion, projectile collision, NavMesh, combat AI |
| **World simulation** | explicit session WorldClock and ordered scheduler | minutes/hours/days, ship heat, hunger, crew work, weather, factions |

World time is stored as integer `TotalWorldMinutes` plus optional subminute accumulation for active play. Wall clock and Unity frame time are **not** authoritative persistence input. Rate `WorldMinutesPerRealSecond` is data/configuration; ratio not decided. World time must pause/advance through one API.

## Tick design

Core engine exposes `Advance(deltaWorldMinutes, stepPolicy)` and `AdvanceUntil(targetMinute, interruptionPolicy)`. Resolve registered systems in deterministic ordered phases (candidate):
1. Drain validated player/system commands
2. Crew duty, training, repair and production
3. Fuel/supplies consumption
4. Ship compartment heat and weather/ice exposure
5. Hunger, temperature, wounds, fatigue and morale
6. Travel/discovery and regional events
7. Scheduled faction/story consequences and interrupt evaluation
8. Emit committed events and projections

Tick granularity is **system-specific**: heating/crew candidate 1 world minute, nutrition 10 minutes, weather 10–60, faction hour+. Do not silently skip substeps where ordering changes outcomes. Processes with analytic/aggregate advancement must prove equivalence to a reference small-step simulation in supported conditions.

## Sleep and fast-forward

Sleeping 22:00→06:00 invokes scheduler, **not** 8 hours of accelerated Unity physics. Interrupt at first relevant hazard event: raiders approaching, fire, engine failure, life-threatening cold, urgent sickness, or chosen watch alert. Report exact time, why sleep stopped and consequences; resumes from that world instant.

Sleeping/resting must honor bed availability, heat, crew shifts and fatigue. Player asleep is not the same as paused world. During disembark, ship staffing, heating and events keep running.

## Day/night and seasons

Sun altitude/azimuth derive from time + approximate latitude + season and weather profile. Polar daylight extremes and cloud/snow/fog visibility matter for tactical travel and supplies. Visual interpolation can run every frame from the snapshot while systemic cold/weather advances on coarse ticks. No hard-coded universal 6AM/6PM daylight.

## Simulation LOD

- **Active scene:** actual combat physics, navigation, detailed interactions, local presentation.
- **Nearby:** bounded tactical/environment summaries; only selected encounters materialized.
- **Distant:** persistent faction/resource/movement events, not active physics.
- **Fast-forward:** run the world scheduler to next due event/interrupt deterministically; visual state catches up on resume.

Define promotion/demotion rules (spawn, despawn, ship/island transitions) with no duplicate health/loot or free resource production.

## Determinism target

Seeded RNG (stable IDs, subsystem streams), specified ordering, integer/fixed-point as needed for critical consumables, replayable command traces and state hash checkpoints. Never assert cross-machine bit-identity for Unity physics. Event provenance and scheduling time should be testable.

## Essential regressions

1. 8-hour simulated sleep burns correct fuel, rests eligible crew, progresses duty and can interrupt at 03:15.
2. 10 × 1-minute advance equals 1 × 10-minute advance for systems promised associative.
3. Alternate render FPS does not change world state after same commands and world minutes.
4. Save/load at minute N then continue yields matching state digest against uninterrupted run.
5. Player leaves for 2-hour mission: ship remains staffed/heated, return shows true elapsed wear.
6. Two seeded world runs yield identical deterministic domain snapshots.
7. Active↔distant transitions never duplicate items, kill twice or roll encounters anew.
