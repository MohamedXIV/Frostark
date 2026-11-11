# E11 #112 — Observed single-minute Work/Rest lease boundary

**Status:** implementation under [stacked Draft PR #125](https://github.com/MohamedXIV/Frostark/pull/125), based on #124 Engineering attendance (and #123 Repair attendance / #122 lease commands / Unity-verified #121). **Not Unity validated.** Sibling #120 save/reentry remains untouched; no changes to main.

## Problem and contract

A genuine shipboard Work/Rest slot has an expiring **WorldClock-minute** claim. A physically stationed crew may stay at the same workbench or bed for a long shift. The #122 scene adapter originally renewed the lease during `Update/Tick` if it noticed the 5-minute threshold. But sleeping/fast-forwarding one or many WorldMinutes in a single `WorldSession.Advance` call happens *without an intervening Unity frame*, so the canonical `CrewState.Tick` can expire the physical claim; after that, #123/#124 refuse to authorize Repair/Engineering. Conversely, silently renewing at every minute of a batched skip using one old Unity pose would make one frame authorize hours of work.

**Contract:** only a **freshly observed, same-current-minute**, genuinely occupied and canonically owned Work/Rest slot can be extended, and **only when the session advances exactly one WorldMinute**. Any batched 2+ minute jump is fail-closed, not secretly treated as uninterrupted presence. Authoritative offline work continues after an explicit scene handover via `EndShipWorkObservation`; this PR does not silently change the scene's status.

## Changes

1. **One session source of truth:** `WorldSession.ReportShipDutyPresence(crewId, slotId)` accepts only actual placed, owned, unexpired, shipboard `Work` or `Rest` slots for the current observed ship scene. `Work` additionally requires the crew's canonical station assignment. `ReportShipWorkPresence` remains a backward-compatible Work-only wrapper, so a bed can never forge Repair/Engineering production. Both are transient observations, not save fields, gameplay revisions or resource changes.
2. **Single-minute boundary:** `WorldSession.Advance(1)` and `AdvanceUntil(current+1)` call `RenewFreshObservedDutyLeases` before the existing `SimulationScheduler` minute step. It uses fresh `SampledAt == current WorldMinute` reports and validates the live claim, available crew, physically placed source, role assignment and remaining time; when due (last five minutes), invokes the **existing** revisioned, replay-safe `RenewCrewDutySlotCommand`, which extends the single existing claim for 15 world minutes and emits its ordinary provenance. No additional clock, new SaveCodec field, duplicated claim, inventory change or repair/thermal production is introduced.
3. **Freshness / anti-fast-forward:** `Advance(20)` and `AdvanceUntil(current+N)` where N>1 **never perform automatic renewal**, even with a currently valid observed slot. Claim expiry, missed world minutes and productive Repair/Engineering gates remain canonical. The scene adapter may continue its #122 normal-frame due renewal; neither pathway can reissue an already-extended early lease because the original command validates the due window.
4. **Unity physical adapter:** `ShipCrewDutyAutonomy` reports stationary, safe-cell **Work or Rest** occupancy on real Unity updates; no report is made while moving, recovering, selected/overridden, waiting at an unsafe edge or invalid. Existing order selection/cancellation and scene unload revoke transient reports. `Rest` never creates productive `Work` proof.

## Tests

- **Four new Core G1 tests** in `CrewEngineeringAttendanceTests`: 22 consecutive observed one-minute steps renew same Work lease only at t10/t20, with no per-frame revision churn and canonical SaveCodec roundtrip; one frame + 20-minute Advance never renews, and claim expires while only the first minute earns actual heat; Rest has a valid owned bed lease and renews without passing `ReportShipWorkPresence`; `AdvanceUntil(current+1)` accepts fresh proof while a wider `AdvanceUntil` cannot reuse it.
- **Two new Unity G2 tests authored** in `ShipCrewDutyAutonomyEditModeTests`: placed Work and off-shift Rest occupants remain stationed for 22 actual one-minute `WorldSession.Advance(1)` steps, with fresh physical walker reports, exactly two canonical renewals and one owner/slot. **These are NOT YET RUN in Unity.**
- **G0 static regression guard** for the WorldSession minute gate, proper Rest/Work report and tests.
- **Core CI:** pending exact-head confirmation at drafting. A green GitHub G0/G1 result does not validate C# Unity compilation or G2/G3.

## Exclusions and future integration

- This does **not** simulate a 20-minute physical shift without Unity frames. A legitimate long sleep/offscreen fast-forward must explicitly transfer authority (via a scene unload/observation handover) before bulk advancement, and then reattach authored actor spawns and claims. That future flow needs scene/OS QA.
- The existing #122 `RenewCrewDutySlotCommand` and claims are still canonical. The usual Heartbeat/Render `Tick` renewal and WorldSession boundary renewal may both be invoked, but only one can pass the final-five-minute due window; no duplicate command effect.
- Sibling #120 pending player Move persistence is not integrated with this dependent stack. Real Save→Destroy→Restore, all G2/G3, G4 OS pointer/HUD, scene content and product builds remain open.
- No merging while tests and broader #112 acceptance remain incomplete.
