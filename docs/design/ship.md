# Ship: survival base, evolving home and warship

## Start and growth

Begin in a fragile fishing trawler, tug or research craft (starting hull undecided), partially damaged and badly insulated. Grow through **constrained modular expansion**: reopen sealed compartments, reinforce hull/icebreaking bow, rework boiler/engine, add workshops, medical facilities, bunks, storage, insulation, cranes, lights, defensive systems and weapons. Ship physical appearance must reflect improvements, wear and repairs.

## Top-down ship management and commands

The ship is a visible multi-room moving home: inspect compartments, click-select crew or rooms, issue assignments/repairs/production and view fuel/heat/injury consequences in the same persistent WorldState. Selected protagonist and crew receive contextual move/interact orders inside reachable areas; there is no assumption of WASD avatar walking. This combines Frostpunk pressure and Fallout Shelter-like compartment management with Last Train Home crew stakes. [Accepted interaction contract](control-and-interaction.md).

## Compartments and staffing

Compartments have spatial slots/footprints, integrity, insulation, thermal connectivity, energy needs, storage limits, worker assignments, repairability and unlock prerequisites. Core spaces: bridge, engine/boiler, sleeping quarters, kitchen, infirmary, workshop, stores. Occupancy affects work output and occupant thermal/health exposure. Roles are jobs, not unique character classes.

Crew schedules, illness and expedition absence change staffing efficiency. No worker performs simultaneous full-rate ship duties and shore expedition work. A lower-skill replacement can operate critical stations at a cost.

## Coupled thermodynamics and survival

Local climate + ship hull/compartment insulation + boiler/engine heat + fuel type/rate + piping/heaters + door/vent states → heat loss/generation and compartment temperatures. There must be trade-offs such as shutting cold decks to preserve fuel and critical bunks. Food/water/fuel/medicine/ammunition and replacement parts share capacity, production and procurement constraints.

Simulate thermal economy in authoritative world ticks (e.g. 1 world-minute design candidate), not arbitrary MonoBehaviour frame order. Start with conservative aggregate rooms and scale sophistication only when decisions benefit.

## Navigation and icebreaking

Ice fields have strength/thickness and local movement costs; hull shape, engine power, speed and condition affect passage and damage risk. Some routes favor short dangerous icecuts versus longer open-water routes with storm/pirate risk. Full deformable ice physics is **not required**: strategic tile/volume data + collision/visual break effects can deliver the mechanic.

Ship navigation must coexist with player presence and crew duty. Top-down route/waypoint commands and helm/workstation interaction are the baseline interface. Fine-grained steering/ship physics and optional direct manual helm control remain design/test decisions; no WASD or first-person ship possession is implied.

## Naval combat

Player ship becomes an improvised armed ship, not a pristine battleship. Candidate weapons: scavenged naval guns, machine guns, harpoons, crude rockets, mines, searchlights and boarding defenses; armor plates and reinforced bow. Opponents use converted fishing craft, patrol boats, barges, tugs and speedboats. Ship modules take locational damage, affecting propulsion, weapons, crew health and heat.

Naval encounters permit evasion, pursuit, environmental use and repair consequences. Ship combat and on-foot combat share resources/economy, but use specialized AI and physics adapters.

## Planned test fixtures

Cold overnight with blocked heater; missing chief engineer; damaged engine during pack ice; fuel rationing across multiple rooms; pirate attack reducing boiler power; compartment repair while crew are away. Cross-system states must survive save/load and accelerated time.
