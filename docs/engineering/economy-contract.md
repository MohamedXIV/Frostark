# FrostArk I02 — Workshop crafting, commerce and environmental gear (Core-first)

**Date:** 2026-10-10. **Implementation:** [PR #63](https://github.com/MohamedXIV/Frostark/pull/63), issue #38. **Status:** Implemented in PR branch, not considered completed until the exact-head G0/G1 suite passes and merge is reviewed.

## Authority and commands

`WorldState.Inventory` remains the **only authoritative player item inventory**, with an immutable-on-commit Pack and ShipHold, integer mass, capacity and stack slots. `WorldState.Economy` is the single persistent recipe registry, authored onboard station registry, pending paid jobs and finite merchant catalog. `WorldState` supply `supply:fuel` is the actual shared fuel consumed by workshop heat and ship use; `supply:credits` is the player's wallet.

Production and trading are **WorldCommandBus operations**, never GUI writes: `StartWorkshopJobCommand`, `BuyMarketItemCommand`, `SellMarketItemCommand`. They validate command ID, expected revision, recipe or market identity, real balances and inventory quantities; accepted commands produce exactly one processed ID and WorldEvent with subject/related IDs and new revision. Rejections return exactly the original WorldState and zero events. All quantities and price multiplication are integer and checked/bounded against `long` overflow; neither rank nor player level participates.

## Crafting (prepaid, time-bound)

A `WorkshopRecipe` has stable/versioned identity, required registered station ID, positive minutes per batch, distinct positive input/output item stacks, and nonnegative `FuelUnitsPerBatch`. One authored station can run only one pending job at a time. Starting a job validates recipe, station availability and known output item definitions **before** atomically spending all ShipHold input stacks plus world `supply:fuel`. Failed capacity/material/fuel preconditions spend nothing.

Successful start creates a `WorkshopJob` tied to one persistent ID, exact recipe version, station, and world-minute due time. Job constructors are internal to Core, so normal callers cannot directly create unpaid jobs. The scheduled `WorkshopMinuteSystem` runs in existing `SimulationPhase.Wellbeing` (after Heat), using the caller-provided authoritative WorldState read/write closure. It **does not create fake player commands each minute**. At or after the due minute it tries to grant all outputs once; if ship-hold mass/slots are insufficient, the paid job remains pending without creating items. Once delivered, the job is removed so it cannot deliver again. An interrupting sleep event means no future minutes, fuel usage or output delivery occur beyond that interruption.

Station IDs are an authored Core-level availability contract, **not yet a verified Unity workbench scene/prefab or physical proximity check**. The Unity interaction/visual station adapter is deferred separately; the headless station registry does not claim in-world workbench PlayMode evidence.

## Commerce

Each persistent market has distinct item offers, real finite stock, positive ask/bid prices (bid no greater than ask), and real merchant credits. Buying transfers `quantity * ask` from player's wallet to merchant, consumes merchant stock and grants the exact items into ShipHold only when capacity allows. Selling consumes real held ShipHold items first, transfers `quantity * bid` from merchant credits to player credits and increments the merchant's actual stock. Command replay, stale revision, wallet shortages, insufficient merchant funds, stock exhaustion, invalid IDs, zero/negative quantities, integer overflow and full cargo all reject atomically.

`InventoryState.TryExchangeShip` is a **pure preparation reducer**, not authorization to mutate the canonical WorldState; it can only become authoritative through validated `WorldCommandBus` world commits. Credited/minted amounts from successful purchases or delivered crafting outputs are monotonic item-inflow provenance for the inventory validator, never a second live stock balance. This provenance allows legitimate output to survive Save/Load without asserting every item originated as scavenged pickup.

These prices and stock quantities are illustrative Core authored values. Dynamic NPC merchants, price negotiation, long-distance trade, replenishment, UI and faction discounts belong to later gameplay/content iterations, not this PR.

## Gear and environment

`GearProtection` stores fixed 0–1000 cold and contamination protection, `EnvironmentalHazard` stores the matching zone requirements. `GearAccess.CanEnterCarrying` checks **real possessed item ID and selected inventory container** before applying protection; without equipment, only zero-requirement areas pass. These are threshold decisions, not simulated Unity gear visuals, equipping animations, suit durability, breathing apparatus or actual exposure injury ticks. No arbitrary level gate.

## Persistence and safe migration

`FARKSAVE` schema v8 added a trailing monotonically credited-item inflow collection after the former v7 navigation section; schema **v9 appends the economy** after those credits. V9 serializes sorted authored recipes (ID/version/station/time/fuel/inputs/outputs), sorted station IDs, pending prepaid jobs (ID/recipe version/station/due minute) and markets (ID, finite cash, item IDs/stock/ask/bid). The decoder validates every count, ID, duplicate, recipe/job correspondence, scalar sign/limits, credits and inventory capacity and performs canonical reconstructed state fingerprinting. All v1–v8 states migrate to **empty** economy and never invent pending work or markets. Legacy inventory and navigation payload layouts are preserved byte-for-byte in their respective versions; v8 credits precede v9 economy. The SHA-256 envelope detects accidental corruption, **not intentional forgery or save-file authentication**.

## Validation gates and limits

G0 is structural Python validation, G1 is actual hosted .NET 8 NUnit. They cover recipe catalog, gear/possession thresholds, atomic paid start, correct time/delivery, blocked output and sleep interruption, buy/sell conservation, hostile merchant/credit payloads, and canonical save-reload with backward compatibility. They are not Unity import/Editor compilation, physics, scene transitions or playable shop UI.

**G2 Unity import/EditMode: NOT RUN. G3 PlayMode/scene screenshots: NOT RUN. G4 desktop/mobile/WebGL/IL2CPP builds: NOT RUN.** PR #52/#56 remain unrelated Drafts awaiting Unity evidence. No ShellWarden or user machine was touched.
