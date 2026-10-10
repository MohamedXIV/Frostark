# C01b — Authoritative shipboard medical care contract

**Date:** 2026-10-10. **Issue:** #67, **PR:** #68. **Parent:** #27 still blocked on Unity locomotion/aim #21.

## One authoritative wound model

FrostArk already persists `CrewMember.Wounds` as an integer in [0,1000], influencing `CrewMember.SkillFor`, staffing, heat efficiency and repair capacity. Ordinary `CrewState.Tick` fatigue/rest deliberately does **not** heal wounds; a long sleep is not free recovery. This change reuses that exact source of health truth and does **not** add a separate injury balance, cooldown or scene manager.

MedicalTreatmentProtocol is a versioned authored capability (real inventory item ID, positive heal-per-action [1,1000], required effective medical skill [0,1000]). The protocol catalog belongs to immutable CrewState and is sorted by item ID. WorldState construction and restore require every policy item ID to be present in authoritative InventoryState.ItemCatalog, and reject duplicates/invalid potency/version. The action itself supplies only an **ID**, never arbitrary healing strength.

`TreatCrewWoundCommand` executes only through the existing WorldCommandBus with validated command ID / exact expected revision / replay ledger. The patient must be a known onboard living crew entry, not away, and actually wounded. At least one authored Medical-role crew member must exist and actual on-shift effective Medical skill must meet the selected policy threshold. Treatment consumes exactly one medicine item from **ShipHold** (not an invented health currency or invisible pack). A single world revision commits both the canonical inventory consumption and CrewState wound reduction; the CrewTreated event records actual reduced wound units and the kit ID. Insufficient kit, missing/away medic, unknown/away/healthy patient, invalid kit and stale/duplicate commands reject and return the original WorldState identity with no event.

Healing clamps at 0 and cannot raise health above authored maxima. The patient's existing skill changes naturally as Wounds diminishes; it does not grant level perks or erase fatigue. The pure CrewState.Treat implementation only resolves the canonical protocol ID, preventing arbitrary potency substitution.

## Save schema and compatibility

FARKSAVE v11 preserves previous v10 crew/wounds and combat payload exactly, appending the **ordered medical protocol catalog** after the complete combat record. Each entry stores actual item ID, protocol version, heal-per-action, and minimum required medical skill. Any v1–v10 file migrates to empty treatment catalog without creating pretend medicine or altering existing wounds. Checksum-recomputed malformed policy inputs fail structural validation. Save/load round trip matches exactly and retains treatment effects, command revision and inventory conservation. SHA-256 remains an accidental-corruption checksum, **not cryptographic proof of authenticity**.

## Scope and tests

G0 Python structural, G1 hosted real .NET NUnit cover consumable medicine, staffing thresholds, away crew restrictions, wound/skill progression, no passive sleep healing, duplicates/stale actions, saved treatment catalog, v10→v11 old saves and forged checksum-recomputed invalid data.

This is care after already authored injuries. **Actual projectile hit/wound creation, enemy/armor combat, shore field medicine, player hitboxes, physical animations, Unity input/raycasts, PlayMode and platform builds remain unverified** in #27/#21. G2 Unity Editor, G3 PlayMode and G4 device gates NOT RUN. These tests prove pure C# simulation contracts only. No ShellWarden/local user machine changes.
