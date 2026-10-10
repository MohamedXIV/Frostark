# ADR — Deterministic save schema v2 (Issue #18)

**Status:** Accepted for R0, 2026-10-10.

## Format and authority

Save payload is a validated immutable capture of the existing WorldState + authoritative SimulationScheduler. It does not invent a parallel mutable world or serialize Unity GameObjects. Binary schema uses magic `FARKSAVE` (8 ASCII bytes), `ushort version`, `int32 payload length`, payload bytes, and 32-byte SHA-256 over header plus payload. All integers are little-endian; text is length-prefixed strict UTF-8 (max 4096 bytes/string), collection counts are bounded. Total blob must not exceed 8 MiB. SHA-256 is *corruption detection*, not authentication.

Payload v1: world minute (int64), command revision (int64), schedule seed (uint32), supply ID/quantity records, crew IDs, exclusive assignment ID pairs, processed command IDs, pending schedule events (ID/minute/kind/interrupt flag), and reserved event IDs including already fired events.

Payload v2 adds a final count and ID/value records for persistent procedural site overrides. Migration from the v1 layout interprets this absent field as **empty**, never as arbitrary reconstructed progress. Unsupported versions, truncation, modified checksum, illegal entities or inconsistent revision/command count produce an explicit failure. Any future addition requires a new schema version and tests against frozen old-save fixtures.

Canonical encoding sorts supply/crew/assignment/command IDs and pending event identities and overrides by ordinal stable strings. IDs use EntityId, never Unity instance IDs. Restored scheduled and already processed event IDs preserve deduplication. The world minute and scheduler minute must agree at capture; otherwise fail. WorldState snapshots remain copy-on-commit and cannot be mutated from callers.

## Persistence and recovery

`SaveRepository` uses `ISaveBlobStore`, a platform-injected port requiring the host adapter to atomically replace primary and rotate the **previous validated primary** as backup, or leave both unchanged on an error. This iteration verifies that obligation through a fake/transactional test store; **it does not implement a disk/WebGL adapter**. Actual desktop file APIs, atomic rename / fsync details, and WebGL storage guarantees belong to infrastructure/Unity integration. Loading rejects a corrupt primary and only uses a *valid* backup with `UsedBackup=true`; it never silently factory-resets a run.

## Proof and limitations

Issue #18 test suite checks deterministic binary round-trip, save/advance/restore against uninterrupted hazard, command/event replay dedupe, version 1 migration, unsupported schema, bad header/checksum/length, backup fallback, failure of both slots, and atomic commit failure. GitHub Actions G0/G1 gives .NET verification, not Unity Editor import, runtime save adapter, PlayMode, cloud saves, authentication, or IL2CPP/platform builds.

### Next interfaces
The application layer (#19) supplies authorized storage adapters and a single owner of live WorldState/scheduler. Future procedural generators can consume the explicit override map; detailed schemas must be versioned and independently tested.
