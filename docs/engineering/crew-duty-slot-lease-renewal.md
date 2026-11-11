# E11 #112 — Long-running Work/Rest interaction lease renewal

**Branch:** `feat/112-duty-slot-lease-renewal`; [Draft PR #122](https://github.com/MohamedXIV/Frostark/pull/122), stacked on the **exact Unity-tested parent #121 head `5c78a50a1fb97a4b401d8767305689ee8c886ee7`**. Sibling #120 order persistence and `main` were not modified. Do not merge until child Unity verification.

## Problem and ownership

An authentic Work/Rest interaction claim initially lasts 15 **WorldClock minutes**, not 15 Unity rendering minutes. The actual previous #121 adapter held the same Work/Rest claim as long as a worker was busy or stationed, but `CrewState.Tick` correctly expired it at its canonical `ExpiresAt` minute. The visual worker then cancelled, released/rebooked work and could lose its station after an otherwise valid long shift. Disabling expiry entirely would leave abandoned claims forever and violate #102/#104. Nothing should get resources or progress merely by renewing a lease.

## Executable first fix

- `RenewCrewDutySlotCommand` is an explicit revisioned, replay-safe world command. It can renew **only** an existing, currently owned `Work` or `Rest` slot claim for an available shipboard crew member. `Eat` and `Drink` claims cannot be extended through this command. Reject unknown/busy/mismatched, away/incapacitated or expired claims, illegal lifetime, premature renewals and non-extension.
- An owner may renew during the **final 5 WorldClock minutes**, for a fresh 15-minute lease; `CrewInteractionState.RenewDuty` produces an immutable registry with the **same owner/slot and exactly one claim**, new `ExpiresAt = now + lifetime`, without inventory use or fatigue/work progress. A success is recorded as `WorldEventKind.CrewDutySlotRenewed` with regular command ID provenance; failed/replayed commands cannot mutate state.
- `ShipCrewDutyAutonomy.Tick` requests this renewal **only while its actual activity is still valid** (canonical claim owned, matching staffed Work or physical Rest, not selected/overridden/safety-interrupted, and not a terminal route failure). It never renews every rendered frame: after one renewal, expiration is 15 world minutes away. An invalid/rejected renewal triggers ordinary safe activity cancellation and canonical claim release, not a forged lease.
- Existing WorldClock still owns all fatigue, repair progress, survival Need evolution, claim expiry and offscreen work. No secondary scheduler or phantom products. SaveCodec already writes the same `CrewInteractionClaim.ExpiresAt` value: renewed leases roundtrip without any new save field.

## Test evidence

- 4 new G1 `CrewDutySlotLeaseTests`: WorldClock travel through 10→20→35 minutes and multiple renewals; exact `ExpiresAt`, one slot, zero extra time; SaveCodec roundtrip; early/duplicate/non-owner/food/invalid TTL/expired/released rejection without revision churn.
- 2 new **authored** Unity G2 `ShipCrewDutyAutonomyEditModeTests`: stationed worker renews past 22 world minutes, no per-frame churn; selection releases rather than renews a duty lease. These **have NOT run inside Unity 6000.6.0f1 yet**.
- Exact substantive Core code commit `00b794e4ef07f8c55b69326581a10659bb388e8e`: automatic [GitHub Core CI #39714589137](https://github.com/MohamedXIV/Frostark/actions/runs/39714589137) **PASS, 26/26 G0 + 312/312 G1 NUnit**, logs inspected. The first CI attempt on the prior SHA exposed a missing `CrewInteractionVerb` namespace import, now fixed. Parent #121 local QA 7/7 duty G2, 80/80 full G2, 27/27 full G3 refers to the parent code **only**, not this child.

## Remaining and explicit limitations

- **No reliable batched fast-forward lease extension yet:** `WorldSession.Advance(20+)` can pass expiry within one call without a Unity frame. This opt-in presenter update does not authorize silent renewal after losing the claim. Long-duration authoritative attendance must be hooked at world-minute boundaries or resolved as an offscreen handover before actual long-run productive duty gating.
- **Canonical productive work presence** remains separate: `CrewState.Staff(WorldMinute)` still counts staffing even while a scene-local worker is walking to its actual station. This PR fixes the exclusive *physical lease*, not job authority.
- **Scene transfer** and ship-to-shore offscreen handover, paused/destroyed scene claim release, #120 order persistence reconciliation, mid-edge new player-order queue, real OS input and standalone/device G4 tests remain separate.
- All PRs stay Draft. Avoid modifying #120 while its Unity QA agent is operating.

## Suggested Unity QA

Pull this branch at its exact latest head. Compile, run the 2 new targeted G2 tests plus all previously green 7 duty G2, full EditMode and PlayMode. In Editor simulate actual `WorldSession.Advance(11)` while already stationed; one owner should renew, no slot duplication or inventory usage; select actor and confirm claim release, then advance 20 minutes to verify no implicit renewal after cancelling. Test Save/Restore of an extended `ExpiresAt` via existing Core test. Record final SHA and only claim pass for tests actually executed.


## 2026-10-22 — real-frame shipboard Drink acceptance fixture

After owner-only lease renewal landed, we also filled a separate #112 evidence gap: `Assets/FrostArk/Tests/PlayMode/ShipCrewProvisionPlayModeTests.cs` now contains one **authored-in-test** real Unity-frame `ThirstyCrewOnlyConsumesRealWaterAfterReachingAuthoredGalleySlot` PlayMode scenario.

It creates a proper authored ship-room layout with a **placed galley module** and its `Drink` slot at a reachable neighboring cell, one crew member with genuine `Thirst=800`, two actual `ShipHold` water items, `ShipGridWalker`, `TacticalCommandController` and `ShipCrewAgencyCoordinator`. It asserts the actual slot is claimed while beginning to walk, **zero stock/Need change before arrival**, real frame-driven movement to the authored physical clearance cell, exactly **one** WorldSession Use at arrival (water **2 → 1**, thirst **800 → 200**), released claim, no pending movement or traffic reservation, and no duplicate consumption after additional frames. No fake simulated world-minute step. The fixture does **not** imply production scene content contains a galley yet, or establish OS-level pointer/HUD input.

Its `.meta` GUID and G0 static import/wiring guard are committed. **This new G3 Unity test is authored, NOT RUN**. Expected additions on top of parent #121 are **2 new duty G2 + 1 new provision G3**; these and all earlier tests must run in Unity 6000.6.0f1. Treat parent 80/80 and 27/27 as historical, NOT #122 results.

**Exact current substantive source HEAD `d77bf3c701e157b36a509023e25e1ad2592521f2`:** automatic [GitHub CI #39715959671](https://github.com/MohamedXIV/Frostark/actions/runs/39715959671) **SUCCESS 26/26 G0, 312/312 G1** (job logs reviewed). G0/G1 do **not** compile Unity PlayMode scenes.


## 2026-10-22 — canonical provision fallback

Further review of the #112 provision activity found that Core discovery correctly returned multiple deterministic stocked Eat/Drink offers, but the ship Unity adapter attempted only the first one. That could strand crew beside a viable second galley when the first source was unplaced, unreachable or physically blocked.

`ShipCrewAgencyCoordinator.Tick` now iterates `CrewProvisionInteractionDiscovery.Find` in its canonical severity/slot/item order and attempts `TryBeginProvision` until an authored, placed, reachable slot actually accepts Claim+Move. It still preserves player-order priority and safe emergency handover. There is **no randomly selected provider, fabricated item or blind arrival**. If every candidate fails for a critical need, `UnmetCriticalNeed` stays visible.

One **new Unity G2** regression in `ShipCrewAgencyEditModeTests.UnplacedFirstDrinkOfferFallsBackToReachableGalleyWithoutPhantomClaim` creates two valid Core offers: nominally first resolves to an unplaced module; second is a placed reachable galley. It asserts only the real second slot is claimed, crew walks/arrives, drinks exactly one stocked item, and no leftover claims. This test is authored but NOT run inside Unity.

**Updated substantive source HEAD `55f9ad4c53582f745e880aefd9a1f1b0f67c148c`:** automatic [GitHub Core CI #39716318601](https://github.com/MohamedXIV/Frostark/actions/runs/39716318601) **PASS 26/26 G0, 312/312 G1** on the exact code (job logs checked). Expected newly added test cases relative to actual parent Unity suite: **3 G2 EditMode** (two lease + one fallback), **one G3 PlayMode** (authored-in-test galley). **New tests NOT RUN**; the parent's 80/80 and 27/27 should not be misreported as the child's results.
