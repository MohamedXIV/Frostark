# #26 — canonical ship modules, integrity and paid repairs (G1-first)

**Live baseline:** main `7f7e1c7461dc7ff2c6e2b99d576874c4d67b19c8`; #23/#24/#25 closed; no #26 PR at selection. Existing PR #52 (Unity bridge) and #56 (visual benchmark) are Draft and must remain untouched.

## Decisions / boundaries
- Add `ShipLayoutState` to canonical `WorldState`, not a Unity-only or independent ship clock. Bounded compartment slot grids, versioned blueprint identities, deterministic non-overlapping module footprints and immutable integrity/repair reservation.
- Construction consumes **actual** `InventoryContainer.ShipHold` item stock by blueprint cost in the same revisioned command. Damage is a command with provenance. Starting a repair consumes parts **once**, reserves that module and queues a `CrewState.RepairJob` keyed by module ID. On Crew-phase completion, commit the module integrity change before Heat phase; no unpaid repair or free parts.
- Heat adapter combines authored module efficiency with existing engineering skill; engine integrity exposes a propulsion read projection (no invented navigation runtime). No module catalog authored in v1-v5 saves means legacy heat remains unchanged.
- Save v6 appends bounded slot/blueprint/module state after v5 crew payload; v1-v5 migrate to empty layout, without invented ship modules. Snapshot fingerprint and deterministic sleep/resume must match.
- Unity renderer/deck geometry adapters, Editor G2, PlayMode G3 and builds G4 are **NOT RUN** and remain separate acceptance gates. No fake physical repairs or scene claims.

## TDD sequence
1. Commit NUnit RED tests for build/collision/material cost, engine damage and paid staffed repair, reject atomicity, save/resume and v5 migration.
2. Observe actual failing G1 job logs from public read-only Core workflow; then implement production code with Unity-independent C# and corresponding .meta files.
3. Verify actual G0/G1 run and job logs at exact PR head. Review migration offsets, no-duplication, checked bounds and head divergence. Keep PR Draft if tests or platform gates remain unverified.
