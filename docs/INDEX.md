# FrostArk documentation map

## Game design
- [Vision, pillars and boundaries](design/vision.md)
- [Gameplay and exploration loops](design/gameplay.md)
- [Accepted top-down point-and-click protagonist/squad/ship control](design/control-and-interaction.md)
- [World, climate and mysteries](design/world.md)
- [Ship survival, layout and naval play](design/ship.md)
- [Characters, crew and progression](design/characters.md)
- [Ground combat, squad control and AI](design/combat.md)

## Engineering
- [Architecture and module boundaries](engineering/architecture.md)
- [Pure-C# tactical order intent and Unity click-control adapter handoff](engineering/tactical-order-intent.md)
- [World time, scheduling and simulation LOD](engineering/time-simulation.md)
- [R2 crew duty and repair queue ADR](engineering/crew-duty-contract.md)
- [R2 ship modules, paid repairs and save v6 Core ADR](engineering/ship-modules-contract.md)
- [R4 navigation routes, ice/weather, heat-first fuel and save v7 Core ADR](engineering/navigation-core-contract.md)
- [Saving, content and procedural generation](engineering/save-content.md)
- [Platforms, rendering, controls and art](engineering/platform-art.md)
- [V01 camera/texture/fog benchmark (G2/G3 pending)](engineering/visual-benchmark.md)
- [Testing and autonomous development](engineering/validation.md)
- [Verified local bootstrap and remaining runner gates](engineering/local-bootstrap-handoff.md)

## Production
- [Dependency-ordered roadmap](production/roadmap.md)
- [Live issue/epic backlog map](production/backlog.md)
- [Superpowers R0 foundation execution plan](superpowers/plans/2026-10-10-frostark-r0-foundation.md)
- [Decision log and open questions](production/decisions.md)

### Reading order for an implementation agent
Read AGENTS.md → roadmap → the specific design feature → relevant engineering contract → issue acceptance criteria. When materials conflict, report the conflict rather than quietly changing the game design. No document here signifies verified gameplay.
