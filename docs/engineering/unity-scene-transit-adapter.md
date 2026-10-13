# Unity ship ↔ shore scene transit adapter (PR #88, #22)

**Status: authored code and editor test fixtures; Unity Editor G2/G3 NOT RUN for this PR.** This is an opt-in Unity adapter to already-merged [Core crew transfers and save v13](ship-shore-transfer.md). It is not a feature-complete playable ship/shore flow until actual scenes, transitions and restore have passed PlayMode.

## Runtime architecture

- **Run-scoped authority:** existing `WorldSessionScope` remains the ONLY VContainer registration of `WorldSession`. Attach `ShoreSceneTransitCoordinator` to the same **root GameObject** to opt into preservation with `DontDestroyOnLoad`. Do not attach it to every scene. Source runtime scene may contain `WorldSessionScope`; **destination scenes MUST be environment-only** and contain no other `WorldSessionScope`.
- **Saved runs:** `WorldSessionScope.SetRestoreSnapshot(decodedSave, ambientMilliC)` must be called **before the scope builds**. The session provider chooses `WorldSession.Restore` over fixture state when supplied. A thermal world still requires explicit climate; do not fabricate 0°C. For an already-built scope, do **not** replace the active WorldSession behind its dependent presenters.
- **Authored scene mapping:** `SceneTransitDestination` holds theater (Ship/Shore), build-listed Unity scene path, stable `SceneId`, `SpawnAnchorId`, and shore `SiteId`. A scene-local `SceneReentryAnchor` marks the matching physical spawn transform. No Unity instance ID is persisted in the save.
- **Typed UI calls:** `TryDepartSelected(destination, transitionId)` uses existing `TacticalCommandController.Selection.SelectedActorIds`. `TryDepart(destination, crew, id)` and `TryReturn(shipDestination, id)` accept explicit party operations. Return derives the expedition roster from the current `PhysicalSceneCheckpoint`; restored saves can use `TryRestorePhysicalScene()` against inspector/authored mappings. A boolean `true` from TryDepart means **async operation started**, not scene/crew success; observe `TransitionCompleted` or `LastError` and `IsBusy`.
- **Before mutation:** verify target is in Unity build settings, load it additively, locate **exactly one** matching spawn anchor, reject any destination with its own WorldSessionScope, reject duplicate stable actor IDs and missing party proxies/camera, verify NavMesh landing and warp destination proxies. Any invalid scene unloads and leaves world crew state unchanged.
- **Domain commit after scene verification:** use the existing `ShoreCrewTransfer.Transfer` with unique operation ID (no new authoritative ship/crew system), then `WorldSession.SetSceneCheckpoint` with stable IDs. Only after successful commit activate destination and unload previous environment scene. On scene switch the retained commander clears transient selection and re-registers new proxies.
- **Rejection / recovery:** a mid-batch reentrant failure in the Core sequential transfer can leave a subset canonically away; the adapter rejects scene activation and reports error, **never silently rolls back WorldState**. Resolve from actual crew availability before retry. Additional recovery/error screen UX is required before production shipping. This is not claimed atomic.

## Editor and fixture requirements

The existing `Assets/FrostArk/Scenes/TacticalVerticalSlice.unity` is a *standalone test fixture* with its own WorldSessionScope; **it must not be loaded as a destination** by this new adapter. Author separate **sessionless** ship and shore environment scenes, each with at least:

1. Real Unity camera, baked Unity NavMesh and spatial ground;
2. Exactly one `SceneReentryAnchor` with the matching serialized authored scene+anchor IDs;
3. A uniquely identified `TacticalActorProxy` with enabled NavMeshAgent for every arriving party member (same stable actor IDs, new scene-only GameObjects);
4. Context interactables using canonical `CollectSalvageCommand` through the persistent commander and session;
5. Entry point UI/hotspot that issues `TryDepartSelected` or `TryReturn`; do not implement WASD or direct possession.

The current `TacticalSliceFixtureState` now includes canonical `CrewDuty` records for its two authored actor IDs, so `SetCrewAwayCommand` can actually validate them. Engine scenes must verify serialized VContainer registration and injection, particularly `WorldSessionScope` plus retained commander surviving origin scene unload.

## Required engine proof (not inferred from .NET)

- **G2 EditMode:** `FrostArk.Unity.Tests.ShoreSceneTransitEditModeTests` covering typed scene mapping, duplicate/invalid marker rules, real crew roster and v13 decoded saved scope session reconstruction. Actual import/compilation including NavMesh/Unity SceneManagement/VContainer must pass.
- **G3 PlayMode:** authored launch scene (run scope + commander + UI), an environment-only landing, an environment-only return deck, all included in Editor Build Settings. Click-select hero/companion → depart → active shore → collect salvage → advance same session minute → return → ship fuel/crew/loot preserved; no scene duplicates, no second session created and no phantom actor. Save while ashore → exit/relaunch restored scope → load scene/anchor according to v13 → actor rebind; test rejected missing/unmapped anchor, bogus missing actors and old `v12` null checkpoint without guessing ship. Track real logs and XML.
- **G4 devices/platforms:** not covered. The Core tests passing (211/211 at baseline) **do not** prove Unity scene lifecycle.

## Known limitations to resolve at acceptance

- No shipped ship/shore environment scenes or connected world UI portal are included in this adapter-only PR; these must be authored/tested. The Unity runtime adapter is opt-in until then.
- No per-actor precise position; the authored anchor and deterministic spacing control reentry.
- Unexpected async load/activation failure *after* successful crew commit is **not transactional**; the adapter reports failure and preserves canonical evidence for reconciliation, rather than inventing rollback.
- Restoring legacy saves with `SceneCheckpoint=null` requires an explicit user/game policy; no silent ship default.
- Global `DontDestroyOnLoad` lifecycle must be tested for multiple save/load/end-run operations, and the game must destroy/dispose the scope when a run truly ends.
