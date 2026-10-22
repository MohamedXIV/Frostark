# E11 #112 — Accepted Player Move persistence + physical duty integration

**Implementation:** [Draft PR #126](https://github.com/MohamedXIV/Frostark/pull/126), branch `integration/112-crew-agency-save-and-duty`. Base is the independently Unity-tested #125 (exact `0fef7291f9a7c1454f54a3d2b83e8ce9b30bd584`); second source is #120 (exact `9080e4bb7eddd901cedc13611c8c6ea9bf558a7a`). The integration branch is **not merged** to main or any original PR. No old branches changed.

## Integrated behavior

- Source of truth remains `WorldSession`: approved ship Move destinations are persistent `PendingCrewMove` entries with unique crew/scene/goal IDs, while mouse selection, route interpolation, Grid reservations and physically observed Work/Rest are ephemeral. SaveCodec V18 writes nonempty typed Move suffix and leaves legacy payloads without saved goals unaffected. Exact source branch #120's roundtrip and older-save fixtures are preserved.
- `ShipCrewAgencyCoordinator` accepts player Moves only after physical dispatch, restores accepted goals at ship-scene reentry from safe authored spawn, clears them on arrival/invalidated movement/emergency safe handover, and **keeps the #125 multi-offer Eat/Drink fallback**. No phantom consumption, duplicate slot Claim or data-filled navigation position is persisted.
- Observed `WorldSession` Work/Rest (repair and Engineering heat), slot lease renewal and deterministic fast-forward remain from #125, not overwritten by #120's older session source.
- **Integration safety correction:** `WorldSession.AcceptShipCrewMove` synchronously revokes the moving actor's transient productive proof. This prevents a previously stationed repairer from doing one extra canonical repair minute if the direct session Move is accepted before a Unity coordinator callback. Accepted Move does not itself mutate the world revision; continuing it across Save/Restore requires a fresh placed Work claim and actual physical re-attestation before further productivity.
- Additional Core G1 integration regression `SavedPlayerMoveRevokesRepairAttendanceAndRestoresWithoutPhantomWork` checks accepted Move + proof revocation + V18 binary save + restored goal continuity + loaded-scene nonproduction without re-attestation + exactly-once order completion.

## Verification boundaries

- Before the additional integration regression, exact integration merge code `6e34d0ece27d70b91f46dc3326b1f7422155c5d7` [CI #39819805266](https://github.com/MohamedXIV/Frostark/actions/runs/39819805266) **PASS 29/29 G0, 332/332 G1**.
- Exact integration HEAD after safety correction and test needs independent CI. **New branch Unity EditMode/PlayMode has NOT been run**; the 88/88 +28/28 proof belongs to source #125 and must not be carried over as integration evidence. Parent #120 Unity tests also do not automatically prove cross-branch integration.
- Required Unity QA: clean Unity `6000.6.0f1` compile; full EditMode and PlayMode; Core targeted save/order and duty tests; actual Save→destroy/restore→ship reentry with an accepted Move; continuing Work/Rest/repair/Engineering without fake attendance, re-claimed slots or duplicated Use. Preserve synthetic input Test08 flake as a separately tracked risk.
- Still open after these tests: real OS pointer/HUD, production Save-menu control and staged `ShipRtsDemo` content, long on-stage sleep/offscreen handover, G4 device and builds; do not close #112 until these evidence gates are verified.


## Second G2 integration regression: saved Move must beat thirsty auto-goals

`ShipCrewAgencyEditModeTests.SavedPlayerMoveCompletesBeforeThirstyCrewClaimsAndUsesRealWater` is an **authored Unity Editor test**, separate from Core CI. It creates a stocked/placed galley and genuine Thirst=800, issues a player-selected Move, clears selection, saves halfway across a reserved cell, restores only the typed accepted goal and original canonical stock, then rebinds a fresh walker from its authored checkpoint. Before the player arrives there is no Needs claim or water consumption; after arrival the persisted Move completes exactly once and the existing physical provision coordinator may claim a placed Drink slot, walk, and consume exactly one ship-hold water. Final Thirst=200, stock 2→1, revision=2 (one Claim + one Use), no leftover reservation/claim/pending Move or replayed action.

`ShipCrewDutyAutonomyEditModeTests.SavedPlayerMoveRestoresBeforeAutonomousRepairResumesOnActualArrival` similarly proves a restored Move keeps real Work paused and repair work unchanged until completion followed by physical return to the station. Both tests are **not run inside Unity on #126 yet**. These checks are also now protected by `tests/validation/test_ship_rts_scene_contract.py` G0 wiring.

**CI before these last two test additions:** merge head `c6402897bc8ecad6c0505d916e2fe39c43681e8e` [run #39820573582](https://github.com/MohamedXIV/Frostark/actions/runs/39820573582) PASS: 29/29 G0, 333/333 G1. Exact latest head still requires checking; do not inherit the source #125 88/88 EditMode and 28/28 PlayMode onto the combined branch.
