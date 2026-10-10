# ADR — Immutable WorldState and optimistic command revisions (2026-10-10)

**Status:** Accepted for R0 #17, amend through explicit design change as broader systems arrive.

## Decision

Use **immutable-on-commit WorldState revisions**, implemented as private copied collections with safe query methods. Stateless `WorldCommandBus.Execute(currentState, IGameCommand)` validates a command completely before returning a new revision; on rejection it returns the identical prior state reference and zero events. This avoids a global mutable manager and makes isolated replay testing straightforward.

Every command has a unique persistent `EntityId` and an `ExpectedRevision`. The authoritative caller serializes command commits and applies the result only if the expected revision matches. Repeating a command ID is rejected even when a new expected revision is supplied. Events are emitted after successful commitment and carry command ID, world minute, revision, stable event ID, kind, subject and related IDs; no Unity `GameObject` references.

Starting domain slices are supply withdrawals and exclusive crew station assignment/unassignment. Values are integral and non-negative; **this is not a final economy, workforce, save-schema or network concurrency framework**. Real atomic snapshot serialization and migrations belong #18. Concurrent workers must never independently commit two forks of the same revision; the application owns the single current state.

## Tradeoffs

Copies of small persistent dictionaries cost allocations. This is acceptable for R0 correctness; use measured structural sharing or a transactional domain store only if profiling demonstrates a need. The revision contract and event provenance must survive any optimization. A 32-bit stable fingerprint is a replay regression hint, **not** a cryptographic identity or a complete save representation.

## Test obligation

NUnit tests must prove rejections leave source identity/stock/assignments unchanged, commands can't replay, stale optimistic revision is rejected, event revisions are ordered, and identical traces generate identical fingerprints. GitHub-hosted G0/G1 may verify code; actual Unity G2/G3 remains separate.
