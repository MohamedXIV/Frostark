# Crew hunger/thirst — Core-only engineering slice (#100)

**Game-design authority:** [Miro D09](https://miro.com/app/board/uXjVEYctyzg=/?moveToWidget=3458764687781875097). This is a technical contract and test handoff, **not** an alternative design source. The timing and threshold values below are provisional engineering tuning until actual playtesting.

## Implemented on this feature branch (not merged until proven)

- `CrewMember` owns immutable `Hunger` and `Thirst` severities in [0,1000], beside existing `Fatigue` and `Wounds`; 0 represents comfortable, 1000 maximum physiological urgency. Construction and v16 restore reject out-of-range values.
- Existing authoritative `ShipCrewMinuteSystem → CrewState.Tick` advances severity **on canonical WorldMinute only**: Hunger +1 each third world minute; Thirst +1 each second. Severity is capped, including while away on expeditions. Offship crew do not automatically consume magically available ship provisions. This iteration does **not** automatically model death, wounds or involuntary food fetching at full severity.
- `CrewNeedsAssessment` is a read-only, minimal signal with a legible reason. At engineering v1 severity >=850, staff selection and effective job skill exclude that person so an eligible existing backup may take over; absent backup leaves role truly unstaffed. Meeting the need through an actual valid provision restores staff eligibility. These are player-visible consequences, not fake progress.
- `CrewConsumable` versions one authored inventory item's effects (`nutrition`, `hydration`). The valid item must exist in the authoritative Inventory catalog. No hardcoded food IDs in production, extra inventory, arbitrary client potency, item spawning, or Unity-local hunger clock.
- `ConsumeCrewProvisionCommand` accepts crew and provision IDs only and validates command provenance/revision, present crew, authored effect, actual need, and real ShipHold availability. One actual item is consumed using `InventoryState.TryConsumeShip`; one immutable `WorldState` commit and `CrewProvisionConsumed` event reflect both inventory and crew changes. Rejections mutate nothing.
- `SaveCodec` **v16** adds a strictly checked tail after v15's recovery latch: sorted crew IDs + bounded Hunger/Thirst, then sorted versioned provision definitions. Earlier v1–v15 decode with zero unrecorded needs and no invented consumables. Invalid counts/IDs/values and unknown inventory item definitions must fail decode. Save/checkpoint hashes include nonzero needs and authored provisions. Historical byte-offset regression tests for older versions have been updated to respect the added v16 section.

## Honest boundaries

This is **not** an autonomous kitchen, ship-local pathfinding, world-object slot claim, multi-person interaction, GOAP import result, seasonal food scarcity balancing, vitamin system or real-time Unity animation. Nothing in this branch should force adoption of GOAP; the pending spike [PR #97](https://github.com/MohamedXIV/Frostark/pull/97) stays separate.

This domain creates an **actionable need and a valid domain command**. A future planner/Smart Interaction layer must check reachability, reserve galley slots and request the validated command; it must not directly subtract food/Needs. Unstaffed ship roles can affect downstream thermal/repair behavior already simulated.

## Verification requirements

- G0: static schema/source validations and C# project structure.
- G1: exact-head `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`: needs tick, staffing, stock conservation, revision/replay/rejection, away crew, v16 byte-identical roundtrip and save-resume, valid old v15 migration, corrupt data rejection, **plus the full older suite and schema fixtures**.
- G2/G3/G4: Unity Editor, PlayMode, supported builds **NOT RUN**, deliberately deferred.

Do not call G0/G1 passed until successful job logs identify the exact tested PR head. Record any failures and fixes, preserve this work on a focused PR, and avoid unnecessary Actions runs.
