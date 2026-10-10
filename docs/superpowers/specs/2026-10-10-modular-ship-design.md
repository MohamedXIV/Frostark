# FrostArk #26 — Modular ship authority and paid repairs

**Design status:** Proposed for review · 2026-10-10. **Branch:** `feat/26-modular-ship-layout`.  
**Issue:** #26 · **Dependencies:** #23, #24, #25, all closed.  
**Baseline:** `main` at `7f7e1c7461dc7ff2c6e2b99d576874c4d67b19c8`.  
**Existing RED fixture:** `tests/FrostArk.Core.Tests/ShipModuleWorldTests.cs` (GitHub Actions 38043991612, G0 passed and actual .NET G1 failed to compile because ship module types do not yet exist). Do not call this a completed implementation.

## Player intent and design boundary

FrostArk's ship is a persistent, repairable survival base whose physical appearance eventually reflects its modules. This phase builds the single authoritative, deterministic *Core data and command* contract for modules, damage, resource-limited construction and crew repairs. It **does not** add a Unity deck renderer, actual propulsion/NavMesh, combat scene or real device proof. The player can change module state through validated commands, then save and restore that state without free supplies or duplicate work.

Main alternatives: (A) Unity GameObjects own modules (rejected: breaks offline simulation); (B) separate mutable ship-service graph (rejected: duplicates canonical state); (C) immutable `ShipLayoutState` inside `WorldState` plus a narrow Unity read-only projection (chosen). This preserves the previous Inventory, Crew and Thermal source of truth.

## Authoritative data

- `WorldState.Layout: ShipLayoutState` is immutable-on-commit. Create with bounded `ShipCompartmentSlot(Id, Width, Height)` grid definitions, versioned `ShipModuleBlueprint(Id, Kind, Width, Height, PartId, BuildCost, RepairCost)`, and `ShipModule(Id, BlueprintId, RoomId, X, Y, IntegrityPermille)` instances.
- Bound positive room sizes and module footprints (candidate 1..64 cells/axis); reject duplicate stable IDs, missing blueprint/room, negative origins, out-of-grid positions and rectangle overlap using checked arithmetic to avoid overflow. Every footprint is integer and deterministic. No `UnityEngine` types or GameObject IDs in Core.
- Module integrity is integer 0..1000. `ShipModuleKind` initially differentiates Engine, Boiler and Auxiliary; preserve stable enum values in the persisted schema. Missing authored layout yields efficiency 1000, preserving old saves without silently reducing boiler heat.
- A zero-integrity engine decreases the published `PropulsionPermille` projection; until navigation #33 consumes it, propulsion is a **read projection, not working vehicle movement**. `BoilerOutputPermille` combines damaged engine/boiler limits with existing crew engineering skill in the Heat phase, using deterministic integer ratios. Avoid double consumption of fuel or an independent heat clock.
- Separate authored blueprint/slot definitions from mutable module snapshots. Stable ordering (ordinal IDs) for fingerprints and snapshot bytes.

## Commands and economic integrity

- `BuildShipModuleCommand(Id, ModuleId, BlueprintId, RoomId, X, Y, ExpectedRevision)` rejects duplicate/missing IDs, invalid placement or collision, absent blueprint and insufficient canonical `ShipHold` parts; success spends exactly `BuildCost`, adds module at integrity 1000, increments command revision exactly once and emits an event.
- `DamageShipModuleCommand(Id, ModuleId, DamagePermille, ExpectedRevision)` rejects missing module, invalid/zero damage and ineffective state; success reduces integrity with clamping and records provenance.
- `StartShipModuleRepairCommand(Id, ModuleId, ExpectedRevision)` requires damage, no existing job and sufficient `ShipHold` stock for `RepairCost`. It **spends once** when creating a persistent `CrewState.RepairJob` identified by the module ID. If rejected it must return the identical prior WorldState reference, no events and no inventory/queue mutation.
- The command bus's existing `ExpectedRevision`, command ID and provenance guards remain authoritative. `InventoryState.TryConsumeShip` should perform a single validated immutable decrement and never treat world supply IDs or uncollected pickups as free parts.
- A repair completion event at the Crew phase boundary may restore module integrity from recorded work completion, **only** if the module still exists; jobs with legacy non-module IDs continue to represent tracked work without fictitious ship repair. No free module resurrection or duplicate refund. The 500 work-unit starting quantity is a balance candidate, subject to test and ADR; it is not a physics-based repair rate.

## Scheduling and cross-module semantics

- `ShipCrewMinuteSystem` runs in `SimulationPhase.Crew` before `ShipThermalMinuteSystem` in Heat; both read/commit to one externally owned `WorldState`. The Crew phase progresses jobs and triggers module integrity restoration on completion. The Heat phase advances the canonical minute and consumes fuel once, factoring both crew efficiency and module efficiency.
- Sleep interruption stops at the event minute; no fatigue, repair work or thermal updates occur after that boundary. A mid-job save/reload must yield identical final fingerprint and binary snapshot to uninterrupted simulation.
- A repaired module may restore heat/propulsion projections; actual spare-part inventories remain spent. No competing counters in Unity. Legacy `AssignCrewCommand` station compatibility map remains intact, not silently interpreted as an additional duty schedule.

## Persistence and validation

- `SaveCodec.CurrentVersion = 6`, preserving prior binary `FARKSAVE` envelope and its SHA-256 **accidental-corruption** check. Append bounded layout slot, blueprint and module sections after the v5 Crew payload, each with validated counts and stable sorting.
- v1–v5 decode to `ShipLayoutState.Empty` and must never fabricate a module, repair or lost resources. Existing v1/v2/v3/v4/v5 downgrade regression tests must adjust payload deltas for the new empty v6 layout, not skip legacy validation.
- Reject duplicate IDs, missing blueprint/room, unexpected enum values, overlapping placements, negative and oversized costs, invalid integrity, stray/truncated data and forged overflowed lengths. A checksum recalculation is not authority to inject invalid state.
- Fingerprint includes authored geometry, blueprint costs and mutable module integrity, in addition to existing inventory, thermal and crew state. Non-empty module states must survive save/reload byte-identically.

## Verification contract / platforms

- **RED:** PR #57 at `ab14bba903b2d944b247c76c0f09f14ca3799245`; [Core workflow 38043991612](https://github.com/MohamedXIV/Frostark/actions/runs/38043991612) proves G0 and missing-symbol G1 compile failure.
- **GREEN:** implement in small test-first slices on this exact branch; use existing public read-only `.github/workflows/core-checks.yml` for actual .NET 8 `dotnet restore` and NUnit G1 plus Python G0. Inspect actual job logs at the exact head; never conflate static source checks and compilation.
- Review high-risk cases: near-int-max coordinates, duplicate blueprint ID, queue existing repair twice, no-parts atomicity, zero-integrity further damage, v5 payload compatibility, save/reload during an interrupted repair, concurrent command revision races and old worlds with no layout.
- **G2 (Unity import/compile/EditMode), G3 (scene/PlayMode/visual/controller) and G4 (desktop/mobile/WebGL builds and perf) are not run** in this environment. If unavailable, Core-only delivery can be evaluated separately, but Issue #26's physical-layout acceptance and scene adapters stay open/pending. No user local machine edits, ShellWarden or privileged workflows.

## Integration/readiness criterion

Deliver Core state, commands, save migrations and real G0/G1 tests in a focused PR. The Unity-facing scene mapping remains an explicit downstream gate (stable identity/footprint projection into transforms). Only advance PR to merge review after exact-head checks, code review, native dependency recheck, migration evidence and honest exception list. Don't close the whole issue by calling a headless layout a tested visual vessel.
