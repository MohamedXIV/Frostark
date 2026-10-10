# FrostArk #18 — Versioned snapshots, corruption-safe restore, and backup contracts

**Date:** 2026-10-10. **Status:** Implementation plan / execution ledger.

**Goal:** Persist the *actual* immutable-on-commit WorldState and deterministic SimulationScheduler state so an interrupted 22:00–06:00 sleep and later commands resume identically after serialization. Reject corrupt/unsupported data without altering the current run.

**Architecture choice:** Explicit bounded binary schema (`FARKSAVE` magic, ushort version, int32 payload length, payload, SHA-256 checksum). No UnityEngine or external JSON dependency; version 1 migration omits procedural overrides (default empty) and version 2 adds them. SHA-256 detects accidental changes, **not authenticity**. Strict parsing rejects trailing garbage, invalid IDs, duplicate entries, count bombs and wrong checksums; no silent defaults for truncated fields. Canonical lexicographic collection ordering produces stable bytes. Use `SaveGameDto` as a validated capture/restore envelope, not a second live state. Backup/persist remains an injected `ISaveBlobStore` with an atomic-commit obligation; platform-specific filesystem/API writes belong downstream infrastructure #19/#42 and must not be invented in Core.

**Files:**
- `tests/FrostArk.Core.Tests/SaveRoundTripTests.cs`: RED for resume, duplicate command IDs, event queue / hazard, procedural overrides, deterministic bytes, migration and corrupt/backup cases
- `Assets/FrostArk/Core/World/WorldState.cs`: export/import R0 snapshots, safe `AtMinute` copy; no live mutable state leakage
- `Assets/FrostArk/Core/Time/SimulationScheduler.cs`: export+restore pending events, seed and reserved event IDs to preserve deduplication
- `Assets/FrostArk/Core/Save/*.cs` and corresponding Unity `.meta`: codec, envelope, bounded parser/DTO, result/error, storage interface/repository
- `docs/production/adr/2026-10-10-save-schema-v2.md`: contract, schema details, migration and atomicity boundaries

**TDD tasks**
1. [ ] Commit red test suite; open Draft PR; verify actual GitHub Actions red failure due to missing save types.
2. [ ] Implement snapshot exports/restores and bounded deterministic v2 binary codec, decode result without mutation; version 1 migration + checksum and content validation; documented schema.
3. [ ] Add injected storage port and safe repository backup fallback; test that failed encoding/commit never mutates active state; that backup fallback is reported (not silent reset).
4. [ ] Run exact-head GitHub Actions G0 and NUnit G1, repair actual failures, review complete diff independently, exact-head merge if all #18 scope criteria met.

**Boundaries:** World/command mutation stays in `WorldCommandBus`, scheduler remains authoritative for world minutes. No filesystem assumption in Unity Core, no Cloud Save, no Unity PlayMode/IL2CPP/WebGL acceptance claim. G2/G3/G4 remain NOT RUN. No historic pre-v1 saves to invent. G1 cannot certify actual Unity package compilation.
