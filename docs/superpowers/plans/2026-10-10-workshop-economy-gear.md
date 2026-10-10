# FrostArk #38 — Workshop, trade, and gear (R5–R6 Core-first)

Started 2026-10-10, baseline main `8bb6845dc3b8f2f3496eb2be8a71874f5bed8012`. Native dependencies #23/#25/#26 CLOSED. PR #52 Unity Bridge and PR #56 visual benchmark remain separate Draft G2-pending; do not edit those branches. Use Superpowers TDD.

## Product contract
- **No dual item authority.** ItemCatalog/InventoryState remain canonical for held stock. Workshop/trade logic produces atomic validated commands against WorldState, never a Unity-owned balance. All items have authored stable EntityId definitions and deterministic integer quantities.
- **Recipe** has stable ID/content version, station kind, positive production minutes, optionally nonzero fuel, 1..N positive input ingredients, 1..N positive output stacks, all IDs unique within each side; item definitions must exist in ItemCatalog. All quantity/price math checked.
- **Crafting** must consume inputs and fuel once at job start, reserve its output delivery, advance with WorldClock, and deliver once to ShipHold only if capacity allows. Pending ready output cannot be silently lost/duplicated if hold full. Stalled jobs survive save/reload. No instant free items. Actual station ownership/crew duty validated by canonical WorldState.
- **Trading** uses persistent settlement/counterparty ID, real wallet supply currency and canonical inventory. Quote / buy/sell validate integer price, finite stock, hold weight/slots, and wallet; committed operations debit and credit atomically. No untracked minted stock. Track traded/crafted inflows in InventoryState with a bounded provenance ledger so save-validation doesn't assume every item originated from salvage.
- **Gear/environment** uses authored equipment capabilities and hazard thresholds (cold/contamination) and actual carried equipment/ship capacities, not player rank/level.
- SaveCodec append v8 with recipe registry or versioned job contract/stock ledger and trade state; v1–v7 migrate to empty economy with no fabricated production. Preserve zero-provenance save byte identity and all previous fixtures.

## Safe staged TDD
1. RED for RecipeDefinition/WorkshopCatalog and GearAccess: duplicate/invalid item+station IDs, versioning, unit scaling overflow, cold/contamination threshold pass/fail. GREEN pure authored contracts.
2. RED/GREEN for persistent craft job reservation, actual ShipHold materials/fuel consumption, FIFO WorldMinute progression and blocked output; integrate WorldState and InventoryState atomic exchange.
3. RED/GREEN for trade quotes/exchange, stock/currency conservation, idempotence, unknown settlement and insufficient capacity/stock.
4. RED/GREEN for v8 persistence, v1–v7 compatibility, 120×1 vs 1×120, interruptible sleep, mid-work save/replay binary equality.
5. Exact-head CI real NUnit + G0 logs, code review, ADR and merge only after issue outcomes genuinely pass.

## Boundaries
No scene-ready workbench UI, merchant NPC GameObjects, device performance or Unity Editor G2/PlayMode G3/targets G4 evidence. Do not claim the full Issue #38 complete when only recipe contracts have been verified. If further design/validation cannot safely finish, leave Draft, tracked In progress and specify exact remaining work.
