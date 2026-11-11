# E11 #112 — On-stage productive Repair attendance (first authority slice)

**Status:** Implemented in **Draft [PR #123](https://github.com/MohamedXIV/Frostark/pull/123)**, child of Draft #122 (long-running slot leases), which is a child of Unity-validated Draft #121 (physical Work/Rest). **Sibling #120 remains untouched**. No merge to main.

## Authority boundary

The existing `WorldSession` / `ShipCrewMinuteSystem` / `CrewState.Tick(WorldMinute)` are still the **only** authoritative writers of crew conditions, repair work and time. On-stage physical occupancy is a *read-only, transient input* to the same minute reducer, **not** another world clock, a game-state command, or a saved animation frame.

Before this change, a fully staffed Repair role could advance `RepairJob.RemainingWork` every minute even while its Unity ship proxy was walking to the station, waiting for traffic or executing a player's separate Move. This first slice gates **actual RepairJob progress**, not all possible future engineering/thermal/combat/medical productive effects. Any role without a RepairJob continues to behave as before.

## Design and implementation

- `CrewState.Tick(minute, mayProduce = null)` retains the prior default contract (null means ordinary canonical offscreen staffing/repair). The only new gate applies inside the existing `DutyRole.Repair` head-of-queue progress calculation; it does not suppress survival Hunger/Thirst, wound conditions, shift staffing, or canonical fatigue.
- `ShipCrewMinuteSystem` optionally passes a `(crewId, role, minute)` predicate from its existing clock phase; it does not insert or reorder a new IWorldMinuteSystem.
- `WorldSession.BeginShipWorkObservation(shipSceneId)` opts in only when that exact canonical ship checkpoint is active. `ReportShipWorkPresence(crewId, slotId)` verifies that crew and Work slot exist, crew is shipboard and not incapacitated, the source module is placed, the station is the crew's persisted assignment, and the authored Work claim is unexpired and owned by that same crew. The Unity adapter establishes **physical** safe-cell arrival and rest/move priority before calling. This transient report is stamped with the canonical current WorldMinute; it creates **no revision, event or item change**.
- The minute-`N+1` repair gate requires a report made in minute `N`, still-valid Work owner/slot/assignment, and the canonical staffed Repair role selected by `CrewState.Staff(N+1)`. A single report therefore **cannot authorize N+2 through N+100** during one batched `Advance(100)` without subsequent observed frames. This prevents one last-known occupied position from granting unlimited repairs.
- `ShipCrewDutyAutonomy` attests only during a valid active Work activity when its walker is **stationary at the exact assigned arrival cell**, owns no in-flight reservation, is not recovering/selected/under player override/urgent Need, and has no route error. It retracts transient proof on each reevaluation, selection, accepted player Move, activity end, or scene teardown. `Rest` attendance never counts as repair production.
- `WorldSession.EndShipWorkObservation(sceneId)`, leaving/changing the physical theater, scene rebind and disposal clear the transient observer. Offscreen advancement returns to the preexisting deterministic staffed repair model. **SaveCodec is unchanged**: saved WorldState, actual job progress, needs and claims persist; *Unity frame attendance does not*. A restored scene must re-attest after physically loading again.

## Verifiable coverage

- **5 new G1 Core NUnit tests** (`CrewRepairAttendanceTests`): no production before physical attendance; one valid Repair production minute then stop on cancellation; loaded ship 10+2 minute batch gets no fabricated multi-minute work; owner/slot/scene validation; save/restore with no saved Unity attendance and deterministic offscreen progress; wrong/stale scene replacement behavior.
- **1 new Unity G2 EditMode test** in `ShipCrewDutyAutonomyEditModeTests`: authored RepairJob + placed Work module/claim, initial walking yields zero progress, stationary Work yields skill-based progress, selecting actor cancels proof, deselect + returning to Work resumes exactly once.
- **G0 static wiring** verifies all Core-to-Unity attendance entrypoints and test metadata.
- **Exact code HEAD `742ada7733668342a5ddf030540daa0e6e8624ce`:** [GitHub CI #39732115652](https://github.com/MohamedXIV/Frostark/actions/runs/39732115652) **27/27 G0 PASS, 317/317 G1 NUnit PASS**. CI was rerun after resolving a C# 8 conditional lambda incompatibility and a test assertion that had incorrectly assumed fatigue-independent fixed repair points. Assertions now check real fatigue-adjusted work and byte-deterministic Save/Restore/offscreen parity.
- **Unity 6000.6.0f1 Editor/PlayMode for this child PR: NOT RUN**. Prior #121 was 80/80 EditMode and 27/27 PlayMode, and prior #122 had source-only evidence. None of those are proof for #123. Actual Unity test-run, physical scene integration, and G4 build/device QA remain required.

## Important remaining system questions

1. **Loaded-scene productive authority is currently RepairJob only.** Other crew contributions such as `Engineering` thermal efficiency and Watch/Medical/Workshop must be examined individually and gated without introducing a second clock or converting mere Work-slot possession into progress. Canonical fatigue can still accumulate while an on-stage staff member walks; balancing that is a separate Need/duty pass.
2. **Long multi-minute fast-forward while a ship scene remains loaded:** a single observed minute gets at most one productive repair minute; subsequent minutes are intentionally fail-closed, not silently assumed attended. #122 lease renewal is still scene-presenter-driven, so an enormous one-shot advance may expire its Work claim. Design an explicit online/offline observation handover or per-minute validated attendance source before treating that scenario as a completed playable shift.
3. **#120 persistent player orders**, shore duty attendance, actual physical station locator/save/reentry, scene unload claim release, OS-level pointer/HUD, and G4 standalone builds are not integrated yet. #123 does not merge or change sibling branches.
4. Physical arrival truth is produced by `ShipGridWalker` and then validated against actual canonical `WorldSession` claim/slot/state. Core intentionally does not persist or trust a float Unity transform as world authority.

## Recommended next Unity QA

Run Unity 6000.6.0f1 on `feat/112-repair-work-attendance` (latest HEAD, not #121). Check clean compilation, new targeted EditMode test, all full EditMode and PlayMode. Verify RepairJob remains untouched during transit and waiting; try a real scene with an actual stationed Repair worker, run WorldSession.Advance(1), issue a player Move, confirm production stops immediately, then cancel selection and return. Repeat with scene destroy/reload, Save/Restore, and one-shot 20-minute fast-forward. Report exact Unity SHA/results and do not mark #112 complete without OS-pointer/HUD and combined #120/123 integration.
