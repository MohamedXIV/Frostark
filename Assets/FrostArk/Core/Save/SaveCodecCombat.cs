using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Combat;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        // V10 appends combat after the complete V9 economy payload. Weapon
        // cooldown uses fixed combat ticks, never world minute timestamps.
        private static void WriteCombat(BinaryWriter writer, CombatState state)
        {
            writer.Write(state.Tick);
            WriteCount(writer, state.Weapons.Count);
            foreach (var weapon in state.Weapons)
            {
                WriteId(writer, weapon.Definition.Id);
                WriteId(writer, weapon.Definition.AmmoItemId);
                writer.Write(weapon.Definition.AmmoPerShot);
                writer.Write(weapon.Definition.CooldownTicks);
                writer.Write(weapon.NextReadyTick);
                writer.Write(weapon.ShotCount);
            }
        }

        // V14 append-only extension. The V10 combat body remains byte-identical
        // for genuine V10-V13 migration; this extension supplies the magazine
        // definition and ammo state that V13 could not persist.
        private static void WriteCombatMagazineExtension(BinaryWriter writer, CombatState state)
        {
            WriteCount(writer, state.Weapons.Count);
            foreach (var weapon in state.Weapons)
            {
                writer.Write(weapon.Definition.MagazineCapacity);
                writer.Write(weapon.Definition.ReloadTicks);
                writer.Write(weapon.MagazineRounds);
            }
        }

        private static CombatState ReadCombatMagazineExtension(BinaryReader reader, CombatState legacy)
        {
            var count = ReadCount(reader);
            if (count != legacy.Weapons.Count)
                throw new InvalidDataException("Combat magazine extension weapon count mismatch.");

            var restored = new List<WeaponRuntimeState>(count);
            foreach (var old in legacy.Weapons)
            {
                long capacity = reader.ReadInt64();
                long reloadTicks = reader.ReadInt64();
                long rounds = reader.ReadInt64();
                if (capacity < 0 || rounds < 0 ||
                    (capacity == 0 && (reloadTicks != 0 || rounds != 0)) ||
                    (capacity > 0 && (reloadTicks <= 0 || rounds > capacity)))
                    throw new InvalidDataException("Invalid persisted weapon magazine parameters.");

                var original = old.Definition;
                var definition = new WeaponDefinition(original.Id, original.AmmoItemId,
                    original.AmmoPerShot, original.CooldownTicks, capacity, reloadTicks);
                restored.Add(new WeaponRuntimeState(definition, old.NextReadyTick,
                    old.ShotCount, rounds));
            }
            return CombatState.Restore(legacy.Tick, restored);
        }

        private static CombatState ReadCombat(BinaryReader reader)
        {
            long tick = reader.ReadInt64();
            var weapons = new List<WeaponRuntimeState>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var weaponId = ReadId(reader);
                var ammoId = ReadId(reader);
                long ammoPerShot = reader.ReadInt64();
                long cooldown = reader.ReadInt64();
                long nextReadyTick = reader.ReadInt64();
                long shotCount = reader.ReadInt64();
                var def = new WeaponDefinition(weaponId, ammoId, ammoPerShot, cooldown);
                weapons.Add(new WeaponRuntimeState(def, nextReadyTick, shotCount));
            }
            return CombatState.Restore(tick, weapons);
        }
    }
}
