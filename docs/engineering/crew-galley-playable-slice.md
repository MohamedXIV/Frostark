# First crew galley slice — Unity G2/G3 **not yet verified**

**Engineering issue:** [#106](https://github.com/MohamedXIV/Frostark/issues/106).
**Canonical game-design intent:** [Miro D02 spatial slots](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03 Smart Interactions](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188) and [D09 Crew Needs & Agency](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). Technical proof here does **not** supersede those design decisions.

## This branch — authored source, not confirmed gameplay

- Explicit Unity Editor generator `FrostArk.Unity.Editor.CrewGalleyFixture.CreateScene` creates **only** `Assets/FrostArk/Scenes/CrewGalleyVerticalSlice.unity`, never modifying `ShipDeckEnvironment.unity` or `SampleScene.unity`. It saves palette materials as Unity assets in `Assets/FrostArk/Generated/Galley/`. Generator is not called automatically at editor startup or package import.
- The generated scene uses a top-down orthographic camera, small deck and bulkheads with one door opening, a galley work source, two visual Eat/Drink standing positions, and two colored crew capsules. Scene IDs, local position offsets and slot verbs correspond to `CrewGalleyFixtureState`.
- One scoped `WorldSession` owns canonical CrewState/Needs, shift eligibility, food/water stock, claims, revisions, clock, and command provenance. VContainer injects the same session into `CrewGalleyController`.
- Ship-local `CrewShipGridPath` uses deterministic A* on an authored walkability grid. It is **not** the sea-route `ShipNavigationState`, and does not import shore NavMesh or GOAP. The generator's bulkhead geometry matches its known blocked grid cells. It does not yet reflect runtime module placement, dynamic doors or general multi-deck topology.
- On scene Start, the engineer has critical Hunger + Thirst and the controller considers read-only Core provision offers. Before travel, it checks grid path, authored SourceId, spatial slot offsets and current scene Transform; the Core claims exactly one slot. During Unity Update, only visible position interpolates between passable cell centers. No WorldMinute changes in Unity Update.
- On confirmed proximity to the owned Eat/Drink standing slot, the controller requests the already merged `UseCrewProvisionInteractionCommand`, atomically spending one real item, reducing Needs and freeing the claim in WorldState. It then addresses the next critical Need or physically returns to the initial engineering position. The existing staffing logic automatically supplies actual backup relief when the engineer cannot work.
- On cancellation, missing claim, expiration or invalid spatial authoring, no food is generated or spent and a remaining owned claim is released via a validated Core command. IMGUI fixture controls show reason, WorldMinute, revision, Needs, ship stock, engineering eligibility and claim, with Start/Replan, Cancel, and explicit Advance 10 min buttons. **The world only advances when told to**, not implicitly per Unity frame.

## Verification required before notifying user that it is worth trying

These are explicit **instructions and authored tests, not execution evidence**:

1. Use a trusted actual Unity **6000.6.x** Editor session with project's pinned packages correctly imported. If an Editor import fails, fix that first rather than claiming playable. Previous Codespaces headless Unity environment may have UDS/shared-memory issues (draft PR #95).
2. Compile the entire Unity project, then run EditMode `FrostArk.Unity.Tests.CrewShipGridPathEditModeTests` **and** `FrostArk.Unity.Tests.CrewGalleySessionEditModeTests` (five authored tests total); verify route through (4,5), blocked galley stand, deterministic grid coordinates and one correctly injected VContainer WorldSession.
3. Run actual PlayMode `FrostArk.Unity.Tests.CrewGalleyPlayModeTests` (five authored tests), plus `FrostArk.Unity.Tests.CrewGalleyScopedPlayModeTests` (one authored test). Inspect NUnit XML and editor logs for physical use and duty restoration, cancellation without resource loss, **mid-step cancellation/replanning after priority changes**, moved/removed galley cleanup, and live VContainer injection with no direct `Construct()` call.
4. Only after clean G2/G3, invoke menu **FrostArk → Fixtures → Rebuild Crew Galley Slice**, or Editor batch method `-executeMethod FrostArk.Unity.Editor.CrewGalleyFixture.CreateScene`. Verify generated scene and material assets, prefab/MonoBehaviour identities, and metadata before committing the generated output. Do not hand-edit an unreadable/untrusted existing binary scene.
5. Load generated `Assets/FrostArk/Scenes/CrewGalleyVerticalSlice.unity` in Unity PlayMode. Observe engineer route around bulkhead, stand at slot, drink/eat, stock/Need/duty updates, return to engineering, second crew present, and no double-claim. Trigger Cancel midway and Start/Replan again. Inspect screenshots/playtest recording if available.
6. Confirm G2 compile and G3 PlayMode at exact branch HEAD, and record actual editor version and evidence links before merging or asking the user to test. G4 standalone desktop/mobile: separate gate, **NOT RUN**.

### Honest limitations, not grounds to fabricate success
- This is a **dedicated authored fixture**, not the full ship room/deck adapter (#58), arbitrary build clearance, saved crew locations, complex player selection, or production game UI. It does not automatically import or depend on GOAP PR #97.
- A future version must synchronize actual placed ship modules and dynamic obstruction sources with navigation and source removal. No fabricated complete smart-object lifecycle or room assignment is claimed.
- Current fixture controls world time **manually** to make the physical action deterministic. The actor has a real animated walk via Unity transforms, not a canonical persistent position in Core.
- The scene is generated only inside Unity and is **not** yet committed as a tested .unity scene. The authoring scripts and G2/G3 tests are on the draft branch, and passing .NET G1 cannot establish that they compile or run in Unity.

## Preflight and practical Unity execution constraints (2026-10-20)

- The opt-in Editor generator now runs **authored model checks before modifying scene assets**: the starting point and Eat/Drink standing cells must be walkable, the galley source itself must be blocked, and both authored slot IDs, source IDs, verbs, local offsets and reachable paths must match their canonical fixture world. Invalid fixture authoring fails explicitly before saving the generated scene. This is source-level preflight, **not** a G2 verification pass.
- The scene Controller now clearly reports missing VContainer/WorldSession injection instead of disguising that failure as an invalid slot.
- The galley EditMode scope test registers `CrewGalleyFixtureBootstrap` **before adding the active `WorldSessionScope`**; a VContainer scope may build immediately during `Awake`, so tests must use the same provider-before-build contract as actual scene loading.
- Initial PlayMode movement speed is intentionally slow for cancellation tests; the full-journey test explicitly speeds up after setup. The mid-step replan test waits until the actor is physically off its previous cell center instead of assuming editor frame duration.
- **Known external blocker:** [#95 Codespaces Unity headless proof](https://github.com/MohamedXIV/Frostark/issues/95) is still open. Earlier experiments got past shared-memory and Linux library problems but could not obtain a valid headless Editor license (`Access token is unavailable`, exit 198). **This PR has NOT independently run Unity G2 or G3.** Do not merge unproven Unity code or ask the user to install/open this as a playable build.
- The correct editor version is pinned by `ProjectSettings/ProjectVersion.txt`: **6000.6.0f1** (earlier Codespaces probe downloaded 6000.6.6f1, which is *not* the exact pinned version). Actual test evidence must identify editor version, PR HEAD and full NUnit XML/logs.

## PR #107 follow-up — safety and test hardening (2026-10-20)

- The opt-in editor scene generator now **always uses an additive scene**; it preserves open/unsaved scenes, refuses to rebuild the target if that scene is already loaded, restores the previous active scene and closes only the generated fixture scene. SceneManager moves are conditioned on scene identity.
- Mid-cell cancellation no longer clears the actor's progress along the already entered corridor segment. Restarting with a **different** Need completes that safe partial segment before taking the revised A* path; no teleport to an old cell center or corner cutting. The route state is presentation-only; WorldSession still owns inventory/claims.
- If the Galley source is destroyed, moved or its authored slot identity/offset becomes invalid **while the actor is moving**, the controller releases the owned claim through Core and pauses without consuming provisions. A cancelled mid-step can be resumed without resetting actor position.
- In addition to the original three EditMode + two PlayMode authored tests, there is now **one EditMode scope-injection test** and **two PlayMode cancellation/target-removal tests**. **Total authored Unity tests after scoped PlayMode integration and moved-source safety: 5 EditMode + 6 PlayMode = 11.** None have actual Unity G2/G3 execution evidence yet.
- Unity destruction is deferred until end of frame; the source-removal PlayMode test explicitly waits for the controller to observe the destroyed object, rather than relying on a single frame's ordering.
- No production Core change, no Unity-generated scene commit and no GOAP dependency. Still **Draft, not a proven player-test build**.

## Unity component serialization and fixture composition correction (2026-10-20)

- **Blocking scene serialization risk corrected:** `CrewGalleyFixtureBootstrap` is a real serialized MonoBehaviour and must have its **own matching source file** `Assets/FrostArk/Unity/CrewGalleyFixtureBootstrap.cs` and stable `.meta` GUID. It was previously defined beside the static `CrewGalleyFixtureState` class in a differently named source asset. A new G2 EditMode test checks `MonoScript.GetClass()` and `MonoScript.FromMonoBehaviour()` against the precise source path, to catch a broken/missing script reference **inside Unity**.
- `CrewGalleyFixture.CreateScene` now builds an **inactive** `FrostArkSessionScope`, adds the provider and presenter/runner as children, adds `WorldSessionScope` last, then activates the complete tree. This prevents premature VContainer build before the world provider or injected runner is present; the Bootstrap also keeps earlier execution order and the scope has a provider fallback.
- Fixture generation deliberately does **not** add a testing scene to `EditorBuildSettings.scenes` anymore. Production build scenes are unrelated to a developer-authored test fixture; generate and **open** the new scene explicitly in the Editor.
- `Assets/FrostArk/Scenes/CrewGalleyVerticalSlice.unity` is still **not generated or tested** by this tool session. No Unity G2 import, EditMode, PlayMode or screenshot evidence exists. This correction increases the chance of a valid first import; it does not establish one.

## 2026-10-20 — Direct scene-composition PlayMode coverage

- Added a new independent `CrewGalleyScopedPlayModeTests` test that **does not manually inject the WorldSession**. It reproduces the inactive-scene-root + Bootstrap + Presenter + Controller + LifetimeScope assembly sequence, activates the root, then inspects VContainer's resolved session, active claim, physical movement and eventual ration/water consumption.
- The Editor session-injection test now likewise creates the root inactive and activates it only after all children and `WorldSessionScope` exist, reflecting the generated scene instead of an unrelated active-object registration path.
- Added a moved-galley test to complement the missing-galley test. Moving the physical source during an active journey must release the reservation without allocating provisions.
- All new/updated Unity tests are **authored only; still zero executed G2/G3 tests**, no generated `.unity` asset or visual evidence. Earlier Core G1 runs validate Core independently, not these Unity tests.
- **Total authored:** 5 Editor/EditMode and 6 PlayMode = 11. The playtest readiness threshold remains unchanged.

## User notification gate

Notify the user only after a generated scene is verified in real Unity with actual G2/G3 tests and a repeatable short manual sequence. If not verified, keep the PR Draft and avoid calling it ready to play.

## Human visual feedback — RTS controls and movement polish (2026-10-20)

**Human playtest feedback on the generated Unity scene:** turns and travel looked robotic, severity was easy to misread as satiety/fullness, and the player wanted genuine RTS-style click-to-select and right-click movement.

### New changes on PR #107 (NOT retested by Unity yet)
- **Smooth presentation only:** `CrewGalleyController` now uses `Vector3.SmoothDamp` for translation, turn anticipation and `Quaternion.RotateTowards` for facing. It advances before the exact center of a right-angle bend, creating a small curved transition instead of a hard stop. `CrewShipGridPath.CanTraverse` samples the full short displacement and rejects smoothing through blocked bulkheads, falling back to the original route segment as necessary. Core WorldMinute, crew Needs and inventory remain authoritative; movement is still fixture-local presentation.
- **RTS manual override (fixture engineer only):** Left click the engineer capsule to select; right click a valid, reachable tile to move. Mouse handling uses Unity Input System and an actual camera ray; no physics/graphics-specific Core dependencies. Invalid/blocked/unreachable right clicks do not interrupt current activities. Valid move orders cancel/release a held autonomous Interaction Slot via Core, do not consume stock, interrupt the current autonomous path, and leave the AI paused after the manual destination. Press **Resume Auto** to reassess Hunger/Thirst and resume the next valid galley interaction. Right-click the original workstation to issue a return order.
- **UI semantics clarified, Core unchanged:** `Hunger` and `Thirst` are **severity** (0 = comfortable, 1000 = starving/dehydrated), NOT fullness bars. Values >= `CrewMember.CriticalNeedAt` display in warning colors with text STARVING/DEHYDRATED. The fixture UI describes what the values mean, and shows whether the engineer is selected/manually controlled.
- **Authored regression tests added:** G2 EditMode validates screen/world tile conversion and checks a potential rounded displacement against a blocked bulkhead; G3 PlayMode asserts invalid orders leave claims unchanged, manual override releases the claim without spending stock, movement stops at player destination and resumes autonomy on request, and selection/right-click use actual camera rays. These tests are **written but unexecuted at this newer branch HEAD**.
- **Important scope:** this is a minimal in-scene RTS interaction for the engineer, not full multi-selection/group formations, queueable orders, pathfinding for multiple decks or persistent assigned world-space positions. The next step should reuse/extend the existing TacticalCommandController selection infrastructure rather than copy player-input logic across production scenes.

### Verification history (do not conflate commit versions)
- Previous exact head `6fb19910e328b351c55a8131a03f212978eb04f2`: user's Gemini reported Unity CLI `1.0.0-beta.13`, Unity Editor `6000.6.0f1`, EditMode **39/39**, targeted Galley PlayMode **6/6**, generated/reopened scene, visible consumption and return to duty. The full PlayMode suite had an unrelated input timing failure (**24/25**); exact XML was not committed.
- **The changes above postdate that test run** and are NOT currently proven by Unity. Rerun `unity test . --mode EditMode` and both Galley PlayMode classes using the official Unity CLI, then manually inspect the generated scene in PlayMode for smoothness, UI semantics and mouse selection/right-click. Do not merge the Draft until fresh G2/G3 proof and observed UX acceptance.

