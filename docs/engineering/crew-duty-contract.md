# R2 crew duty, shore absence and repair queue — ADR-R2-001

**Status:** Accepted for the deterministic #25 pure-C# slice, 2026-10-10.
**Scope:** FrostArk.Core canonical world; Unity scene/physical G2+ remains separate.

## Source of truth and ordering

`WorldState.CrewDuty` owns persistent crew skills, one shift per person, fatigue (0–1000), wounds (0–1000), expedition absence, and ordered repair jobs. `WorldState` also retains the R0 registry of stable crew IDs and legacy exclusive `AssignCrewCommand` station map. The new duty roster must be a subset of the registry. Legacy station assignments are **not** interpreted as a second staffing graph; they remain an older command contract pending a deliberate typed-station migration.

The scheduler runs `ShipCrewMinuteSystem` in `SimulationPhase.Crew` (10), before `ShipThermalMinuteSystem` in `SimulationPhase.Heat` (30). Crew mutates only the canonical world snapshot; Heat commits the authoritative minute and fuel. The composition root **must include the Heat adapter** when using the Crew adapter, even for an empty thermal ship. Never run Crew as an independent world clock. Save capture occurs only after the complete scheduler minute.

## Staffing decision

Roles are ordered **Engineering → Watch → Cooking → Medical → Repair**. Each minute a member can staff **at most one** role. Eligible members must be present, within their cyclic 24-hour shift, and have positive condition-adjusted skill. Primary specialists are preferred; if absent, a backup with at least **200/1000 authored backup skill** can fill a higher-priority vacancy. Ties resolve by persistent ordinal ID. This 200 cutoff is a balance **v1** decision to avoid stripping a dedicated repair specialist with trivial incidental skills; revisit with target-specific proficiencies when skill catalog is authored. Shift boundaries use world-minute modulo 1440 and work over midnight.

Effective skill is `baseSkill × (1000 - floor(fatigue/2)) × (1000 - wounds) / 1,000,000` (integer). Assigned duty adds 1 fatigue per world minute; present unassigned crew recover 2 per minute. Away crew do not magically rest. Wounds persist. A ship with **no authored roster** retains pre-v5 100% boiler performance, preventing legacy migration from silently destroying a saved ship. Once roster is authored, a missing engineer can lower boiler heat and a zero-efficiency engine consumes no boiler fuel.

The repair queue progresses only when a Repair duty member is staffed. At most one job (FIFO) receives `max(1, floor(effectiveRepairSkill/100))` work units per minute; completed jobs leave the queue. Repair work is canonical persisted **work progress**, not a claim that compartment integrity or replacement-parts inventory has already been updated. Mapping jobs to damaged compartments, spare-part costs, and completed-repair effects is a subsequent integration gate and must not be reported as implemented.

## Persistence and evidence

Save v5 appends a bounded roster and repair queue after v4 thermal payload. v1–v4 decode to an empty roster without inventing duty, fatigue, wounds, or repair progress. Duplicate persistent IDs, invalid role/shift/skill/flag, and crew not in the world registry reject load. Ordering is stable, digest includes crew duty, and save/resume across sleep interruption must be byte-identical to continuous execution. No Unity APIs or platform filesystem assumptions enter the codec.

G0 static/schema checks and G1 .NET 8 NUnit must be verified from **exact PR head job logs**. G2 Unity import/EditMode, G3 PlayMode and G4 desktop/mobile/WebGL/console behavior remain **NOT RUN** until a legitimate authorized runner provides evidence. Platform file/WebGL atomic save storage adapter is not supplied by this change.

## Fatigue recovery relay (issue #98; draft engineering v1, unverified)

The canonical `CrewMember.Fatigue` state now carries a persisted `IsRecovering` latch. In the first engineering tuning, working to fatigue **900/1000** enters mandatory recovery; off-duty rest must reduce fatigue to **400/1000** before a crewmember becomes eligible for duties again. The wide gap avoids flickering in and out of duty each minute. These values are **implementation defaults subject to gameplay balancing**, not a second authoritative design decision; authoritative game-design intent is in [Miro D09](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097).

Recovery is integrated into the existing deterministic `CrewState.Staff/ Tick` pipeline; no independent hunger/recovery timer, AI engine, or imaginary worker. The staffing algorithm can assign an already eligible backup to the uncovered role, otherwise ship output/repair duty genuinely suffers. Crew dispatched away do not receive on-ship fatigue recovery. The latch follows the member across crew snapshot copies, on/off shore flags, saving and restore.

Save **v15** adds a trailing sorted recovery extension to v14. Reader checks count/order/flags against the roster and rejects impossible state. v1-v14 saves remain readable; a high-fatigue old crew starts recovery safely, while v14 naturally cannot restore hysteresis it never stored. When no crew is recovering, existing roster fingerprints are intentionally preserved for stable older fixtures. Unit tests cover threshold transitions, backup coverage, time/absence, v15 persistence/invalidation, v14 migration and uninterrupted vs resumed world ticks.

**Verification:** G0 source review only until NUnit runs on the exact feature branch; G1 .NET NUnit **NOT RUN** in this tool environment, G2 Unity and G3 PlayMode **NOT RUN**. This is not a full Needs system or physical crew navigation implementation.
