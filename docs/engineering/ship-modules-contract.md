# FrostArk R2 #26 — Ship module authority and paid repairs

**Status:** Implemented as a **Pure C# core slice only** on Draft PR #57. No Unity engine import, renderer, device build or performance proof has been executed for this PR.

## Canonical authority and geometry

`WorldState.Layout` is the single immutable module/blueprint/slot snapshot; the inventory and crew duty remain the existing canonical `WorldState.Inventory` and `WorldState.CrewDuty`. Slot and module footprint dimensions are integer 1–64. The layout validates persistent IDs, sorted ordering, blueprint references, positive origins, bounded rectangles and overlap; no UnityTransform or GameObject ID is treated as authoritative. Damage reduces module integrity in 0–1000 permille. `PropulsionPermille` is a **read projection only**, not a running ship navigation system.

The first authored module categories are Engine, Boiler and Auxiliary. With no authored layout, propulsion and boiler efficiency remain 1000 permille to preserve old worlds. The current thermal reducer scales boiler heat by both available engineering and damaged module output. The limitation of an engine affecting heat/propulsion is a deliberate initial system abstraction, not validated vehicle physics.

## Paid economy and repairs

`BuildShipModuleCommand` deducts exactly the blueprint construction cost from `InventoryContainer.ShipHold` and places one footprint in one atomic revisioned command. `DamageShipModuleCommand` reduces integrity with event provenance. `StartShipModuleRepairCommand` consumes real ShipHold replacement parts once and atomically reserves the module in the immutable layout and queues exactly one CrewState repair job; its work budget is **500 abstract work units** as an initial balance decision, not physical minutes. Bounded blueprint material costs: **1–1,000,000 units** (v1 policy).

The existing Crew phase progresses the FIFO repair queue using staff/shift/away/fatigue effective skill. On job completion, the matching module is restored only when its persisted `RepairReserved` flag is present; a legacy/forged unreserved matching job cannot heal a module. `WorldState.Create` and save hydration reject a reserved module lacking a matching crew job. The public module constructor cannot set the reservation flag. Rejections retain the exact prior WorldState with zero events; no refunds or free parts.

## Persistence

SaveCodec v6 appends three counted collections (slots, blueprints and modules) after v5 crew data; the module record includes the 0/1 paid-reservation flag. Sorting is ordinal and snapshot fingerprints include geometry, blueprint costs, installed modules, integrity and repair status. Earlier v1–v5 payloads decode with `ShipLayoutState.Empty` without fabricating construction or repair. The SHA-256 envelope checks **accidental corruption**, not authenticity or anti-tamper security. Invalid enum values, missing references, malformed reservation flags, overlap and detached paid repair marks reject hydration. The separate platform file/WebGL storage adapter is still missing and is not included here.

## Scope and verification

The tests-first RED phase is documented in Actions run #38043991612 (missing types), followed by a full Core GREEN run #38045993655 (96/96 NUnit), then reservation forgery RED #38046069037 (96 PASS/1 FAIL) and repair gate GREEN #38046121692 (97/97 NUnit). The oversized-cost bound is being verified as a separate RED→GREEN cycle in run #38046187419. **This document deliberately does not claim final merge readiness until the last exact-head G0/G1 logs are inspected.**

The user approved **Core-First, Unity-Deferred**: G0 Python and actual .NET 8 NUnit G1 are the only current execution gates. **Unity G2 Editor/import/EditMode, G3 PlayMode/UI/deck rendering and G4 desktop/mobile/WebGL/console builds/performance: NOT RUN.** No scene-ready visual layout adapter, in-game propulsion, collision/animation or device acceptance is claimed. Issue #26 physical visual adaptation must remain tracked separately; do not close it just from headless tests.
