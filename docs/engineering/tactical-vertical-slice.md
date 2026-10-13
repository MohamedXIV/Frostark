# Tactical Click-to-Move Vertical Slice (#21) — 2026-10-13

## Overview

Delivers the Unity presentation/application vertical slice for **top-down / near-isometric point-and-click tactical squad command and contextual interaction** under [Issue #21](https://github.com/MohamedXIV/Frostark/issues/21).

Adheres strictly to [docs/design/control-and-interaction.md](../design/control-and-interaction.md) and [docs/engineering/tactical-order-intent.md](tactical-order-intent.md):
- **Click-to-move and contextual orders only.** No WASD, no twin-stick shooting, no direct avatar velocity overrides.
- Pure immutable Core `TacticalSelection` and `TacticalOrderIntent` snapshots are consumed without duplication.
- Authoritative state modifications flow exclusively through `WorldSession` via typed commands.

## Architecture & Components

```
                    [ Pointer / Mouse Raycast ]
                                 |
                                 v
                 [ TacticalCommandController ] (MonoBehaviour)
                     |                       |
      [ TacticalSelectionController ]   [ TacticalOrderIntent ] (Core snapshot)
                     |                       |
                     +---- Dispatches to ----+
                     |                       |
                     v                       v
          [ TacticalActorProxy ]     [ TacticalInteractableProxy ]
          (NavMeshAgent navigation)   (Contextual approach & action)
                     |
                     v
             [ WorldSession ] (VContainer Scoped)
```

### 1. `TacticalSelectionController` (`Assets/FrostArk/Unity/TacticalSelectionController.cs`)
- Manages transient, UI-scoped selection using immutable `TacticalSelection` snapshots.
- Exposes `Select(EntityId)`, `Toggle(EntityId)`, and `Clear()`.
- Dispatches `SelectionChanged` event whenever selection state transitions.

### 2. `TacticalActorProxy` (`Assets/FrostArk/Unity/TacticalActorProxy.cs`)
- Scene bridge representing an expedition character (protagonist or crew companion).
- Holds stable `EntityId ActorId` (e.g., `actor:protagonist`, `actor:companion`).
- Owns visual selection indicator GameObject, updated on selection events.
- Navigates via `UnityEngine.AI.NavMeshAgent` when receiving `MoveTo(Vector3)`.
- Handles `ApproachAndInteract(TacticalInteractableProxy, onComplete)`: paths towards the target until within interaction range before executing the callback.

### 3. `TacticalInteractableProxy` (`Assets/FrostArk/Unity/TacticalInteractableProxy.cs`)
- World object representing contextual interactables (e.g. `salvage:supply_cache`).
- Configurable `interactionRange` and `depleted` state.
- Dispatches `Interacted` event when approached and executed by an actor.

### 4. `TacticalDestinationMarker` (`Assets/FrostArk/Unity/TacticalDestinationMarker.cs`)
- Visual destination indicator displayed at the target coordinate upon issuing a `MoveTo` command.
- Features fade animation and automatic deactivation.

### 5. `TacticalCommandController` (`Assets/FrostArk/Unity/TacticalCommandController.cs`)
- Integrates pointer raycasting, actor/interactable detection, order generation, and execution.
- Left-click on actor: selects actor (or toggles selection if Shift is held).
- Left-click on ground: builds `TacticalOrderIntent.MoveTo`, displays destination marker, commands selected actors.
- Left-click on interactable: builds `TacticalOrderIntent.InteractWith`, commands selected actor to approach and execute.
- Injected with `WorldSession` via VContainer DI.

### 6. `TacticalSliceFixture` (`Assets/FrostArk/Editor/TacticalSliceFixture.cs`)
- Headless / Editor menu item (`FrostArk/Fixtures/Rebuild Tactical Slice Scene`).
- Generates `Assets/FrostArk/Scenes/TacticalVerticalSlice.unity`:
  - 55° steep-tilt orthographic tactical camera (Phase 2 baseline).
  - Walkable ground plane and cover wall obstacle.
  - Directional sun lighting.
  - Selectable Protagonist (`actor:protagonist`) and Companion (`actor:companion`).
  - Contextual Salvage Cache (`salvage:supply_cache`).
  - `WorldSessionScope`, `WorldSessionPresenter`, and `TacticalCommandController`.

## Verification Evidence

- **G0 Static Checks:** 11/11 PASS (`tools/validate_core_structure.py` + `tests/validation/`).
- **G1 .NET Core NUnit:** 200/200 PASS (`dotnet test`).
- **G2 Unity EditMode Tests:** 7/7 PASS (`FrostArk.Unity.Tests` & `FrostArk.Bootstrap.Tests`):
  - `TacticalSelectionBridgeTests.SelectionController_SelectToggleClear_MaintainsOrderAndEventNotification`
  - `TacticalSelectionBridgeTests.CommandController_MoveOrder_BuildsIntentAndDispatchesToActors`
  - `TacticalSelectionBridgeTests.CommandController_InteractOrder_BuildsIntentAndTriggersContextualInteraction`
  - `WorldSessionBridgeTests.ScopedPresenterResolvesOneAuthoritativeSessionAndReleasesIt`
  - `PackageSmokeTests` (Addressables + VContainer)
- **G2 Scene Generation:** `Assets/FrostArk/Scenes/TacticalVerticalSlice.unity` generated via headless batchmode without warnings or errors.

## Scope Boundaries

- **Implemented:** Selection snapshots, pointer raycast picking, NavMesh agent movement dispatch, contextual interaction range/approach, destination feedback, DI session wiring, headless scene generation, comprehensive EditMode tests.
- **Out of Scope / Future:** Squad formation offsets (#32), weapon firing / combat mechanics (#22), real-time path replanning under fire, and ship compartment layout transitions.
