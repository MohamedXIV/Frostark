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
2. Compile the entire Unity project, then run EditMode `FrostArk.Unity.Tests.CrewShipGridPathEditModeTests` **and** `FrostArk.Unity.Tests.CrewGalleySessionEditModeTests` (four authored tests total); verify route through (4,5), blocked galley stand, deterministic grid coordinates and one correctly injected VContainer WorldSession.
3. Run actual PlayMode `FrostArk.Unity.Tests.CrewGalleyPlayModeTests` (four authored tests total); inspect NUnit XML and editor logs for the full hunger + thirst journey, cancellation without resource loss, **mid-step cancellation/replanning after the highest-priority Need changes**, and the disappearing-galley case.
4. Only after clean G2/G3, invoke menu **FrostArk → Fixtures → Rebuild Crew Galley Slice**, or Editor batch method `-executeMethod FrostArk.Unity.Editor.CrewGalleyFixture.CreateScene`. Verify generated scene and material assets, prefab/MonoBehaviour identities, and metadata before committing the generated output. Do not hand-edit an unreadable/untrusted existing binary scene.
5. Load generated `Assets/FrostArk/Scenes/CrewGalleyVerticalSlice.unity` in Unity PlayMode. Observe engineer route around bulkhead, stand at slot, drink/eat, stock/Need/duty updates, return to engineering, second crew present, and no double-claim. Trigger Cancel midway and Start/Replan again. Inspect screenshots/playtest recording if available.
6. Confirm G2 compile and G3 PlayMode at exact branch HEAD, and record actual editor version and evidence links before merging or asking the user to test. G4 standalone desktop/mobile: separate gate, **NOT RUN**.

### Honest limitations, not grounds to fabricate success
- This is a **dedicated authored fixture**, not the full ship room/deck adapter (#58), arbitrary build clearance, saved crew locations, complex player selection, or production game UI. It does not automatically import or depend on GOAP PR #97.
- A future version must synchronize actual placed ship modules and dynamic obstruction sources with navigation and source removal. No fabricated complete smart-object lifecycle or room assignment is claimed.
- Current fixture controls world time **manually** to make the physical action deterministic. The actor has a real animated walk via Unity transforms, not a canonical persistent position in Core.
- The scene is generated only inside Unity and is **not** yet committed as a tested .unity scene. The authoring scripts and G2/G3 tests are on the draft branch, and passing .NET G1 cannot establish that they compile or run in Unity.

## PR #107 follow-up — safety and test hardening (2026-10-20)

- The opt-in editor scene generator now **always uses an additive scene**; it preserves open/unsaved scenes, refuses to rebuild the target if that scene is already loaded, restores the previous active scene and closes only the generated fixture scene. SceneManager moves are conditioned on scene identity.
- Mid-cell cancellation no longer clears the actor's progress along the already entered corridor segment. Restarting with a **different** Need completes that safe partial segment before taking the revised A* path; no teleport to an old cell center or corner cutting. The route state is presentation-only; WorldSession still owns inventory/claims.
- If the Galley source is destroyed, moved or its authored slot identity/offset becomes invalid **while the actor is moving**, the controller releases the owned claim through Core and pauses without consuming provisions. A cancelled mid-step can be resumed without resetting actor position.
- In addition to the original three EditMode + two PlayMode authored tests, there is now **one EditMode scope-injection test** and **two PlayMode cancellation/target-removal tests**. **Total authored Unity tests: 4 EditMode + 4 PlayMode = 8.** None have actual Unity G2/G3 execution evidence yet.
- Unity destruction is deferred until end of frame; the source-removal PlayMode test explicitly waits for the controller to observe the destroyed object, rather than relying on a single frame's ordering.
- No production Core change, no Unity-generated scene commit and no GOAP dependency. Still **Draft, not a proven player-test build**.

## User notification gate

Notify the user only after a generated scene is verified in real Unity with actual G2/G3 tests and a repeatable short manual sequence. If not verified, keep the PR Draft and avoid calling it ready to play.
