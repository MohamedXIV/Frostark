# Crew Smart Interaction — authoritative slot-claim Core v1 (#102)

**This is an engineering contract, not a new game-design authority.** Miro records [D02 spatial clearance/slots](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660185), [D03 Smart Interaction lifecycle](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687764660188), and [D09 explicit assignment vs runtime claim](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097).

## What this PR implements

- `CrewInteractionState` holds a **sorted immutable** catalog of authored `CrewInteractionSlot` offers and runtime `CrewInteractionClaim`s inside the existing canonical `CrewState`; no separate scheduler, Unity Update, or second world store.
- A slot has its own stable ID, source-object ID, action verb (Eat/Drink/Rest/Work), local grid offset and facing. **The offsets are not reachability proof** and do not block building footprints by themselves.
- Runtime claim holds a slot, crew owner and expiry WorldMinute. At most one claim per slot **and per crew member**. `ClaimCrewInteractionCommand` validates crew presence, injury incapacitation, stable known slot, free capacity, valid TTL (1–1440 world minutes), revision and duplicate provenance. `ReleaseCrewInteractionCommand` requires exact current owner. Each successful command emits one matching WorldEvent and commits one revision; rejections preserve original WorldState.
- Existing `CrewState.Tick` expires claims on the canonical world minute and drops claims whose owner is away or incapacitated. `SetAway` and incapacitating `Wound` drop the existing claim on the same immutable change. The first release after timeout is rejected; a new actor may claim on the next world tick.
- `SaveCodec v17` appends authored slot definitions and claimed slots after v16 Needs, and verifies no unknown slot references, duplicate claims, two slots owned by the same crew, absent/unavailable claim owners, malformed verb/facing/timing. Older schemas v1–v16 restore with an empty interaction registry; no invented workers/slots. The default empty extension is exactly two int32 counts (8 bytes). Existing checksum-valid old fixture tests are aligned to the real pre-v17 fields.
- `CrewInteractionClaimsTests` verifies claim/exclusivity, wrong-owner cancellation, timeout, dispatch/wounds, G1 fingerprint and save/replay invariants, corrupted expiry and migration. **Tests must actually run before claiming PASS.**

## Intentional boundaries

This foundation is **only Find → Validate → Claim / Release**; no pathfinding, animator, camera or physical collisions; no GOAP activation, no automatic eating, no command outcome from using a claimed slot, no bed ownership/preferences and no physical clearance enforcement yet. SourceId is an authored reference in this v1, **not** a fully verified link to `ShipLayoutState` (galley, bed and medical devices have not yet been added as ship module kinds). Do not market it as a working ship interior.

Remaining staged work: (1) author real module/prop interaction sources and validate them against ship layout and clearance, (2) expose slot candidates to Crew goal planner, (3) Unity ship-local movement to claimed coordinates, (4) validated `Use` command that calls existing food/rest/work domain actions and always releases/recovers claims on interruption, (5) explicit assignments vs mutable automatic bed/room preferences. Keep ordinary player AssignCrewCommand separate from ephemeral claim.

## Gates

- G0 source contract checks and G1 `dotnet test` for full Core at exact PR HEAD. Do not merge on source review alone.
- G2 Unity import, G3 PlayMode and G4 platforms **NOT RUN** per user directive to postpone engine checks.
- GOAP 3.1.2 PR #97 is still an **unproven Draft**; no dependence and no duplicate Needs/world state.

## Claim-bound provision resolution — follow-up issue #104 (G1 pending)

- `CrewProvisionInteractionDiscovery.Find(world, crewId)` is a **read-only candidate list**, ordered by actual Hunger/Thirst urgency and then stable slot/item IDs. It checks the authored Eat/Drink slot, current ship-hold stock, current Need, present/unincapacitated actor, and that no slot/crew claim is already active. It intentionally **does not claim a slot, prove a physical route or run Unity**.
- `UseCrewProvisionInteractionCommand` connects an already-owned, *unexpired* Eat/Drink claim to `CrewState.Consume` and `InventoryState.TryConsumeShip`; mismatched verb, no claim, wrong owner, expired claim, no item, full Need or stale revision are **atomic rejections**.
- A successful use **consumes one real item, reduces Needs, and releases the occupied slot in ONE immutable WorldState revision, emitting one `CrewProvisionConsumed` event**. No second 'release' command or duplicated Core inventory. Direct `ConsumeCrewProvisionCommand` still exists for the prior direct domain API; the new command enforces a claim for **interaction-backed** use.
- A claim is explicitly **not proof of arrival**. The future Unity adapter must prove path completion and proximity to the authored anchor before calling the use command. SourceId is still authoring metadata without canonical galley footprint/clearance validation, so this code should **not** be presented as a physically navigable galley.
- No change to persisted state structure: SaveCodec stays v17. `CrewProvisionInteractionTests` adds discovery, end-to-end meal/water, wrong-owner/verb checks, depleted inventory, exact-expiry check and use-after-load replay. G0/G1 must be confirmed from exact-head logs before merge.

**Playable test milestone:** wait for an actual Unity ship scene where actor navigates to a real galley slot, interrupts duty for Hunger/Thirst, consumes a real ration on successful arrival, correctly releases on cancellation and resumes/heeds staffing changes. A passing pure .NET suite is foundation, not yet that milestone.
