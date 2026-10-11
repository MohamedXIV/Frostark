# Game loops, pacing and interactable spaces

## Physical play spaces

1. **Open water / pack ice:** top-down ship command, map/waypoint route scouting, storms, visibility, ship motion, ice collision and vessel weapon stations. Actual manual helm precision is an open design choice; no assumed WASD piloting. World/route map supports informed planning but does not replace navigation.
2. **Ship interior / deck:** inspect and click compartments/workstations/crew, assign jobs, schedule repairs, rest, crafting, supply rationing and heat zones with a Fallout Shelter-like room-management view; ordered characters can move between areas without erasing persistent state.
3. **Landing zone:** select the protagonist alone or a party, click ground to order movement and click entities/targets for context actions; human/creature AI executes real-time fights, cover and retreat while resources and wounds persist.
4. **Strategic layer:** map, weather, known settlements/factions, rumor leads, mission selection, economic decisions, expedition roster and gear.

## Player control across contexts

The **protagonist is a selected persistent unit**, not a directly steered WASD pawn. Solo and squad play use the same selection → point-and-click move/target/action → unit/AI execution loop. Multi-selection and per-unit actions extend the same contract; mouse bindings, camera tilt and tactical pause are separately validated. The ship uses inspect/select/assign/interact and waypoint orders rather than an isolated first-person survival mode. See [accepted control contract](control-and-interaction.md).

## Moment-to-moment loops

- **Sea:** chart/scout → select a route → navigate through water/ice → manage power and heat → detect/evade/engage threats → dock.
- **Land:** equip party → disembark → explore in real time → use cover/stealth/combat → extract resources/intelligence/survivors → return with consequences.
- **Ship:** monitor temperature and stores → move scarce staff among duties → heal/fix/produce → upgrade modules/equipment → prepare next voyage.

## Early-to-late trajectory

- **Early:** unreliable small vessel, poorly insulated rooms, fragile engine, modest supplies, 1–few allies, only nearby safe-ish coastal routes. First hostile landing demonstrates why retreat matters.
- **Mid:** specialization and replacement staffing, partial ship expansion, improved hull/ice-breaking, better medical/winter protection, competing trade/contract choices; navigable biomes diversify.
- **Late:** deliberate trade-offs for harsh polar/deep-freeze zones, modular ship loadouts, higher-risk expeditions into restricted labs/military sites, autonomous world consequences and optional leads/endings.

No arbitrary gear treadmill. Some regions are gated by **infrastructure/environment tolerance** (warmth, breathing gear, hull strength), not only combat level.

## Stakes and economy

Resources include fuel, food, water, medicine, ammunition, insulation, spare parts, salvage, equipment and information. Crew can be absent, tired, sick, wounded or killed; losing a unique specialist causes meaningful reduced capacity. Compartment heat can be selectively rationed. Ship/shore damage persists until repaired.

## Agency and fail states

No forced 'assault + medic + engineer' slots. Skills/equipment affect optional paths and the risk of a self-chosen composition. Decisions must expose enough information to be understandable, while outcomes remain uncertain under weather, morale and encounters. Whether protagonist death is run-ending or permits crew succession is explicitly open (decision log).

## Functional first playable

A small but real coastal loop: click-select the protagonist and order them to walk/interact on a top-down shore, start with a damaged ship and meaningful heat/fuel, cross navigable ice, land on one environment, face one enemy/contact, bring salvage home, assign repair crew and advance world time. Its systems must be the actual game modules extended later, not separate prototype scripts.
