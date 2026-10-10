# W01a — deterministic island topology contract

**Date:** 2026-10-10 · **Issue:** #69 · **Parent:** #35 (native-blocked on #22) · **Generator version:** 1

## Source-of-truth boundary

`IslandGenerator.Generate(islandId, seed, zone, nodeCount)` is a pure, deterministic factory for **topology data**, not a Unity scene builder, a NavMesh or a persistent WorldState/SaveCodec site modification system. The authored island identity is a case-sensitive persistent `EntityId`, not `GameObject.GetInstanceID()`. A future Unity adapter must use generated site IDs and explicit data-driven scene asset references; it must **not** substitute transient runtime instance IDs.

`WorldZoneProfile` owns stable zone ID, ThermalZoneBand (Coastal, PackIce, DeepPolar), ambient milli-Celsius [-100000,50000], and bounded 0..1000 contamination/pack-ice pressure. These are authored profile parameters. They do not override live WorldClock, Weather, ShipThermalState or gear authority; connections to those existing systems are follow-up integration.

## Generator v1

A deterministic labeled `StableHash32` derivation (FNV-1a, with UTF-8 text of island ID, integer seed, generator version and site index) determines integer X/Y positions and side-branch attachments. **No mutable global RNG** is involved. Node count is strictly 4..64.

Generator builds a safe connected landing→intermediate sites→extraction spine, with at least one side salvage site attached to the spine using an unsafe/hazardous traversal edge. Every site has a stable ID of the form `<island-id>/site:<zero-padded-index>` and a distinct integer-grid X coordinate. Seed changes positions/attachments but **not** the persistent site IDs for a given island and node count. Future content revisions that change node count, site naming or generator algorithm must carry a versioned migration policy for already materialized saves; silently rerolling a revisited island would violate the game's no-respawn rule.

`IslandLayout.Create` validates authored or generated content: 2..64 unique sites and coordinates, exactly one landing/extraction, undirected sorted edges with distinct known endpoint IDs, no duplicate links, reachable extraction on safe-only edges, all optional sites reachable via some traversable links. This is *abstract graph reachability*, **not** a statement about actual Unity geometry, physics or navmesh obstacles. The immutable canonical ordered arrays are the sole source for its FNV fingerprint; this is a quick deterministic replay digest, **not** a cryptographic content hash.

## Tests and deferred gates

G1 NUnit checks same-seed repeatability, changed-seed and changed-island differentiation, 128 seeds with safe exits and optional sites reachable, stable site IDs, shuffled author order byte-digest identity, invalid/dangling/missing exits/duplicate nodes/coordinates, profile ranges, immutability and bounded generation. G0 checks files, Unity metadata pairs and source structure. **G2 Unity import/EditMode, G3 PlayMode, G4 platform builds are NOT RUN.**

**Not yet implemented:** anchoring these IDs to loaded Unity scenes or saved looted/damaged overrides, assigning world navigation/sea legs to landing beaches, validating actual physical walkability, terrain generation, NavMesh or ship transitions. Those are parent #35/#22 responsibilities and must not be marked complete by this Core subtask.
