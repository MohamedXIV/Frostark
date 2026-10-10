# Persistence, game data and content pipeline

## Save is world truth, not the scene

Versioned saves capture world minute, RNG/seeds, player and all crew, ship compartments and modules, fuel/food/stores/equipment, inventory ownership, wounds, job queues, schedules, quests/contracts, factions, weather fronts, discoveries, active expeditions, damaged/looted sites and persistent procedural overrides.

Persistent stable IDs reference entities and authored definitions; no GameObject instance ID or Unity scene reference is valid as a permanent identity. Use explicit SaveGame DTO/schema with migrations and compatibility checks; the exact serializer/format remains an open foundation decision.

## Separate definition from instance

- **Definitions:** authored catalog data (ship modules, weapons, items, traits, zones, creatures, recipe rules, factions) with stable IDs and validated schemas.
- **Runtime instances:** individual condition/ammo/durability, ownership, upgrades, crew work, world incidents and generated geometry state.
- **Projections:** UI-friendly read models, rebuildable from authoritative state.

ScriptableObjects can serve Unity-friendly authored catalogs, but the simulation should receive clean data DTOs. For cloud agents, consider a JSON/YAML → validation → generated catalogs pipeline; choose by proof, not preference alone.

## Procedural content

World generation needs seed + versioned rules + authored anchors + permanent overrides (buildings opened, casualties, goods taken, cover destroyed). Procedural layout tools should validate pathable bounds, landing/exit, interactable access, cover affordances, sightlines and thermal exposure. Build/generation must not replace authored game content silently.

## Scene boundary and lifecycle

Load/unload zones without resetting crew/ship/time; no double-spawn or duplicate loot. Local Unity proxies register/unregister with stable IDs. On landing or aboard transfer, flush combat and event consequences before scene teardown, preserving game-world authority.

## Data safety

Atomic save-write pattern where supported, backup/recovery and corruption reporting; WebGL uses platform-appropriate storage adapters, not arbitrary filesystem writes. Explicit migration testing and fixture saves accompany schema changes. Cloud save and modding are *future candidates*, not launch requirements.

## Acceptance fixtures

Round-trip inventory/crew/ship after 48 world hours; resume at interrupting pirate encounter; migrated older save; same seeded scene revisited after salvage; forced unload mid-expedition; two zones generated with equal seeds produce reproducible base layouts but honor persistent modifications.
