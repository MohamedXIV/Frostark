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
