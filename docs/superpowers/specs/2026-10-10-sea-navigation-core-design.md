# FrostArk #33 — Sea Routes, Ice and Weather: Core-First Design

**Status:** Accepted for implementation planning on 2026-10-10; plan approval remains separate from code authorization. **Issue:** #33, parent #8.  
**Main baseline:** `38fa1e18ce72cd6f69b43b1da0bba6e65ae8b575` (after Core ship PR #57).  
**Dependency truth:** #24 and #26 CLOSED; Unity-facing deck follow-up #58 is separate and not evidence of G2/G3/G4.  
**Direction:** Preserve full-game quality and correctness, while continuing without an available Unity runtime. No new Github Actions workflows, privileged jobs, game-engine or local-device claims.

## Player intent and acceptance boundary

The player navigates an improvised polar vessel across dangerous sea and ice. An apparently shorter icy route should be measurably faster **or** more hazardous / expensive than a longer open-water alternative. Crew duty, heating, inventory, hull damage, and fuel are persistent consequences, not scene-only numbers. Save/reload and sleep must not create free movement, lost fuel or surprise completion. Ship handling and visual ice collisions ultimately occur in real space under Unity; this spec establishes the deterministic **strategic simulation truth** for those later adapters.

### Alternatives considered

A. Make Rigidbody/Unity NavMesh the owner of route progress: direct scene feedback but breaks headless travel, sleep, and deterministic save. Rejected.
B. Persist only arbitrary travel ETA and fuel numbers: cheap but a throwaway implementation that cannot later explain ice exposure and damage. Rejected.
C. **Chosen for Core:** authored graph of spatially grounded route legs with immutable integer coordinates/distance, ice/sea state and visibility modifiers; route progress and hull consequences belong to canonical WorldState. Actual Unity helm/physics uses this as a time-and-resource domain contract, not a duplicate clock. We intentionally do not lock real-world scale, world-time multiplier D019, or global map extent D022.

## Domain model

- `SeaWaypoint`: immutable stable EntityId, integer map coordinate `X`, `Y` in **authoring units** (not asserted to be real meters yet). Valid bounds; no coordinate-based or Unity InstanceID identifiers.
- `SeaLeg`: stable ID, directed `From` / `To` waypoints, strictly positive authoring distance, `IcePermille` (0..1000), `StormPermille` (0..1000), `VisibilityPermille` (0..1000), optionally controlled weather profile key. All authored data validated; no invalid/self/duplicate edges or disconnected leg composition.
- `ShipNavigationState`: persistent canonical immutable graph/catalog plus `CurrentWaypoint`, active ordered leg IDs, active index, nonnegative progress, hull integrity permille, route command provenance and paused/terminal status. Save/restore must not mutate graph order or admit phantom destinations.
- `HullCapability` / `NavigationTuning`: immutable, bounded input configuration for ice strength threshold, base vessel speed-per-world-minute, fuel-per-distance and weather hazard cost. These values are **data inputs and fixtures**, not permanent D019 time-scale or balance decisions.
- `ShipLayoutState.PropulsionPermille` is the **existing** engine readiness projection. Do not create a parallel engine integrity value. At 0 propulsion progress stops, and an engine repair later resumes eligibility. Hull state is separate because ship hull integrity is not currently represented by a module blueprint; never create a second hull truth in Unity.
- `RoutePlanCommand`: revision-guarded route start/replace through `WorldCommandBus`; rejects unconnected legs, bad IDs, duplicate replay, non-current origin, illegal state and invalid fuel/speed. On accepted commands, revision increments once, event carries provenance; rejected command preserves state object and emits no events.
- `ShipTravelMinuteSystem`: `IWorldMinuteSystem` in the existing **Travel** phase, after Crew + Heat. It reads and commits the same authoritative `WorldState` without stepping its own world clock. Heat receives fuel priority according to scheduler phase; travel burns only the **remaining canonical** `supply:fuel`. No negative fuel, hidden RNG, wall clock or frame-rate dependencies. If supplies are absent/insufficient, travel pauses (no displacement, no partial fuel debit); it must not fake a free final leg.
- One world-minute tick advances a deterministic fixed-point slice of distance, consumes bounded fuel, applies ice/storm damage only by explicit profile rules, and updates active leg / destination. A stopped voyage can retain its plan. Completing a leg has no teleport outside recorded waypoints. Floating point isn't used for the simulation ledger.
- `WorldState.AtMinute`, command-copy, CrewTick, ThermalTick, Fingerprint and Save read/write preserve navigation data. Command revision ≠ minute progression. Presentation owns interpolated transforms only.

## Route trade-offs

Compare at least two authored paths between the same endpoints in deterministic tests:
1. **Short thick-ice cut:** fewer authoring units but high `IcePermille`, reduced effective movement, higher hull risk and fuel overhead for an under-reinforced ship.
2. **Longer sheltered/open-water detour:** more distance but minimal ice damage; different weather/visibility exposure. It may be faster or safer depending on tuned hull/engine. Tests assert a *specific authored fixture* comparison, not a universal claim that all ice is slower.
3. Damaging the Engine in #26 reduces vessel progress. A paid repair restores propulsion readiness. Fuel still pays ship Heat first; interrupted sleep stops at the exact world minute with matching cumulative nav/hull/fuel history.
4. High storm/low visibility affects visibility and navigability in the domain projection; Unity fog, particles, ship rolling and collision remain deferred.

## Save v7 and legacy

- Add a new append-only, bounded `SaveCodec` version **7** after v6 module data; schema records graph definitions, route position/progress, tuning and hull integrity. Do not reorder or repurpose v1-v6 payloads.
- A saved v1–v6 world migrates to an **empty/inactive** navigation state. It is not silently moved to a fabricated route or given a free reinforced hull, and existing v1-v6 regression fixtures are updated for exact byte append lengths.
- Malformed payload, duplicate IDs, non-connected routes, progress past leg length, invalid 0/negative distances, out-of-range weather or hull values, and valid-checksum forged invalid data must be rejected. The SHA-256 envelope is a corruption check, not a signature.
- Saving after N ticks and loading then running M more matches N+M uninterrupted ticks in full-world fingerprint and canonical bytes. Existing thermal/crew/ship save tests must continue passing.

## Interfaces / subsystems

1. Core World/Navigation: stable waypoints, legs, navigation snapshot and pure step function, without Unity.
2. Core Commands: validated route start/cancel/modify with revision and events; no player input device contract in Domain.
3. Core Time: scheduled Travel phase consumes the already-advanced Heat minute, never a separate clock.
4. Save v7: new partial SaveCodec file with `.meta` and strict v1-v6 migrations.
5. Presentation/integration later: Unity helm, Rigidbody/current vectors, collision, on-screen route UI and real-space ice visuals on dedicated downstream acceptance; do not treat strategic leg traversal as verified real-time handling.

## Evidence and scope truth

- G0 Python structure and actual G1 .NET 8 NUnit can prove deterministic Core simulation only, using the already authorized read-only `.github/workflows/core-checks.yml`, and actual job-log inspection.
- Unity G2 Editor/import/PlayMode integration, G3 real ship movement/collision/weather rendering, G4 desktop/mobile/WebGL/IL2CPP device builds are **NOT RUN** until actual engine evidence exists.
- A Core-only merge must record an explicit downstream Unity follow-up before issue #33 can be marked Core-only Done; otherwise leave #33 OPEN. Avoid silently removing requirements.
- Decision register D019 world-time ratio and D022 world scale stay **Open**. Per-minute speed, distance units, hull thresholds and weather curves are authored fixture/tuning parameters, not accepted global balance.
- No mock arbitrary gameplay progress, real ocean-scale claims, or fabricated physical collisions. Full production interface contracts rather than a throwaway prototype.

## Primary red tests to implement after design review

- Short icecut vs detour has distinct fuel/time/hull consequences; insufficient hull sees deterministic damage
- Engine-zero / no-fuel cannot travel, heat/crew still progress; repair restores opportunity
- Route start rejects invalid/disconnected/duplicate leg sequences and stale revision without state mutation
- 10×1 minute equals 1×10 minutes; scripted sleep interruption advances no future travel
- Non-empty v7 save/resume matches exact bytes; legacy v1-v6 migrations yield empty/inactive navigation
- Forged-valid-checksum navigation with impossible progress/route does not load
