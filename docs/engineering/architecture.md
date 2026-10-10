# Technical architecture — Unity + predominantly pure C#

## Main design rule

The Unity Editor is a delivery/runtime tool, **not the owner of gameplay truth**. The project should be implementable/testable on cloud agents as much as possible, without pretending .NET tests substitute for Unity integration. Prefer simple modular architecture over Manager singletons or premature ECS.

```text
Presentation (MonoBehaviours, UI Toolkit, Cameras, VFX, Animations)
      ↓ reads projections / sends commands
Application (use cases, scene/session orchestration, facades)
      ↓
Domain (world state, rules, clocks, events, economy, progression)
      ↑ implementations of ports
Infrastructure (Unity physics/navigation/asset adapters, persistence, I/O)
```

**Dependency direction** points inward. Domain code never references `UnityEngine`. Split core into targeted `asmdef` assemblies and a standalone `FrostArk.Core` .NET test project when compatible; adapters translate Unity types into domain values. Do not create a mirror of Unity scene GameObjects in the domain without reason.

## Proposed modules

- Core: IDs, results, deterministic random, commands/events, world state, scheduler, math helpers.
- World: zones, weather, ice, discovery, factions, encounter/event generation.
- Ship: compartments, integrity, heat networks, fuel, navigation, repairs, stations and naval damage.
- Crew: roster, skill, conditions, assignments, shifts, social state and training.
- Inventory/Economy: stores, transport, crafting, salvage, trade, gear.
- Combat: character attributes, weapons, damage, cover scoring, tactics intents.
- Story: clues, rumors, optional contracts, narrative/state conditions.
- App: session bootstrap, validation, command handlers, read models, save orchestration.
- Unity: rendering/scene bridges, animations, player input, physics, navigation, audio, UI.
- Tooling: batch-mode validators, fixtures, deterministic scenario runner, editor generators.

## Composition and dependency injection

**VContainer** is the preferred DI candidate, not yet installed/locked. Scopes: app infrastructure, persistent run/session, local encounter/scene. Register concrete services by composition root; constructor injection for pure services; MonoBehaviour bridge injection where safe. Avoid global `GameManager.Instance`, giant `FacadeManager` and nondeterministic callbacks for state authority.

Small **command/query facades** let UI submit validated intents (`AssignCrew`, `SetHeatPolicy`, `RequestTravel`) and access read models. Only dedicated command execution mutates state; event dispatch follows committed changes. Explicitly order cross-module reactions; no unbounded event bus loops.

## Ownership and data

- One canonical `WorldState`/session identity for a run, composed of domain snapshots; unique stable IDs not GameObject instance IDs.
- State updates as validated commands; provenance/time/seed captured for consequential events.
- Declarative content definitions separate from runtime state: IDs, schema versions, balance parameters and reusable authored resources.
- GameObjects are presentation and interaction proxies; spawn/unspawn is not creation/destruction of simulation truth.
- Restrict Unity serialization to Unity-facing assets and adapters; use explicit versioned DTOs for saves.

## Tactical and visual integrations

Unity Input System → player intents; Unity AI Navigation for pathing; physics/raycast and animations remain real-time. Ship/shore/world simulated at suitable LOD, with common persistent state. Timeline/cinematics optional, not foundation dependencies.

## Performance and portability

Acknowledge WebGL limits, mobile memory/thermals and console SDK constraints; avoid mandatory desktop-only native plugins, dynamic JIT requirements or synchronous disk file access in runtime. Do not assert jobs/Burst/DOTS are required before profiling. Test core assembly under target Unity-compatible C#/.NET subset.

## Anti-patterns

- Mutating fuel/health in individual arbitrary `Update` methods.
- Serializing scene objects as the only canonical crew/world data.
- A feature-specific sleep skip that ignores hazards.
- Huge interface graphs for single-use classes or an untested service locator.
- AI navigation tied to world minutes, or physics accelerated to simulate a long sleep.
- Assets with unversioned schemas or missing `.meta` identity.

## Foundation exit criteria

A headless pure-C# scenario can step a minimal ship/crew/world for simulated days, compare repeatable digests, save/reload and resume with equal outcomes; a separate Unity adapter scene runs the same authoritative tick. Actual Unity compile, runtime and visual outcomes remain explicit later gates.
