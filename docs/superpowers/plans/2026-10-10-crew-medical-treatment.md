# FrostArk C01b (#67) — Non-magical shipboard wound treatment

Date: 2026-10-10; base `main` at `84d89ae95b8e119e985d9aca7bee738c1c4e5317`. Pure-C# independent child of still Unity-blocked #27.

- Reuse *existing* `CrewMember.Wounds` (0..1000), `CrewState` roster/skills, `WorldState.Inventory` ShipHold, world command/idempotence engine.
- Author `MedicalTreatmentProtocol` (medicine item ID, positive 1..1000 heal-per-action, medical minimum 0..1000), persisted with CrewState; never accept heal amount from caller.
- `TreatCrewWoundCommand` requires known onboard wounded crew, on-shift medic with sufficient actual `EffectiveSkill(Medical, state.Minute)`, and actual ShipHold medicine. Its single validated WorldState revision commits both a one-unit ShipHold deduction and clamped wound reduction; emits event with actual healed amount. Rejections preserve exact original WorldState and no events.
- Avoid phantom medic jobs: explicitly require at least one authored `DutyRole.Medical` on roster, in addition to effective staffed skill. Healing does not occur passively while sleeping, and medical care of away crew on an island is out of scope.
- SaveCodec v11 appends versioned collection of treatment protocols after v10 combat; v1-v10 restore to empty catalog. Validate protocol item IDs against ItemCatalog and duplicates; no magic old-save medicines. NUnit G1 tests RED→GREEN for costs, staff, wounds/skill consequences, duplicate/stale commands, save round trip and old version fixtures. G0 static, real Actions logs.
- Boundaries: not a fabricated physical hit, not the parent combat/armor/wound source completion. Unity G2/PlayMode G3/hardware G4 NOT RUN; #27 still blocked by #21. No new parallel health/currency manager.
