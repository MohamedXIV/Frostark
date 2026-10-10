# W01b — canonical persistent island site state (#71)

Date: 2026-10-10; baseline 85b27b930f06f766a3ad5ed6c72a3f260c6056b2 after #69.
Pure-C# independent subtask of native-blocked #35. Unity bridge #22, actual crate GameObjects/NavMesh and reward authority remain deferred.

Source of truth: one immutable `WorldState.SiteProgress` registry of ordered (IslandId, SiteId, Looted, DamagePermille) values, originally materialized from validated IslandLayout output. No mutable scene-only flags; records survive island transitions and save/restore. `LootSiteCommand` only marks consumption with revision/event ID, NEVER creates inventory items; physical gate/reward integration is later Unity logic. `DamageSiteCommand` monotonically increases damage 0..1000 with checked bounded arithmetic, cannot resurrect geometry. Both reject unknown IDs, invalid amounts, already consumed/unmodifiable states with no partial mutation and use existing command/replay validation.

SaveCodec v12 appends bounded count + canonical per-site records after v11 medical catalog; v1-v11 migrate to explicit empty registry. Decoder rejects invalid/duplicate IDs, bad boolean, negative/out-of-range damage and extra bytes even when SHA-256 was re-signed. Value fingerprint includes records. Tests: 128 seed registry consistency, multiple islands, immutable changes, command idempotence and stale revision, uninterrupted vs save/reload, corrupted checksummed payload, v11 compatibility. Validate actual hosted G0/G1 only; G2/G3/G4 NOT RUN.

Do not treat stored loot flag as authoritative proximity/hit reward. Code boundary stays production-grade but scene interaction is deferred to #35/#22.
