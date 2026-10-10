# FrostArk W01a / #69 — Seeded island graph (Core-first)

2026-10-10. Start live main 88458946cbd7575871917698c80ccadf9d9a56a5; parent #35 remains blocked by Unity bridge #22. Use Superpowers RED→GREEN TDD and real Actions G0/G1. No direct changes to #52/#56 or machine.

## End-to-end invariant
An authored stable island EntityId + integer seed + WorldZoneProfile + bounded node count always generate the same canonical island layout (stable IDs, integer positions, anchors and links) across repeated runs and shuffled authoring list inputs. Extraction must always be reachable along safe edges from landing, optional sites must be reachable on the general graph, and no site/corridor may have dangling/duplicate identities, coordinates or invalid zone parameters. The layout is pure Core data, not a Unity GameObject or a procedural texture.

## Design decisions
- WorldZoneProfile: persistent zone ID, band enum Coastal/PackIce/DeepPolar, ambient milli-Celsius and 0..1000 contamination and pack-ice pressure. Explicit author parameters, no hidden RNG or current weather provider.
- IslandNode: stable site ID, integer x/y grid coordinates, role enum Landing/Extraction/Salvage. IslandEdge: sorted undirected distinct site IDs and safe/unsafe flag.
- IslandLayout.Create: canonical sort nodes/edges, reject duplicate positions/site/edges, maximum 64 sites, exactly one landing and extraction, reject disconnected optional sites, validate safe-only landing→extraction BFS. Immutable arrays exposed via read-only snapshots; reproducible stable FNV fingerprint includes the complete profile and graph.
- IslandGenerator.Generate: pure seeded hashing of (islandId, numeric seed, stable per-index labels), generator version 1. A guaranteed safe connected spine of landing, intermediate sites and extraction, with optional side salvage attached to spine by non-safe edges. Integer coordinates keyed to stable index without collision, changed seed changes map positions / attachments, never mutable RNG state. Stable IDs not Unity instance IDs.
- **Not in scope:** save/restore of looted/damaged site overrides; Unity scene placement, environmental VFX, site materialization, streamed actors or landing collision. Explicitly track parent #35 and #22 as G2/G3 blocked; W01a is G1 contract only.

## TDD / verification
1. RED: test 128 seeds, zone/ID changes, route reachability, canonical input order and all malformed graph cases.
2. GREEN: pure Worldgen classes (Assets/FrostArk/Core/Worldgen) with paired .meta. G1 actual NUnit logs, G0 structural validation.
3. ADR documenting generation version and safe traversal assumptions. Exact-head PR review and RepoPilot merge gate before merge, never claim Unity G2/G3/G4.
