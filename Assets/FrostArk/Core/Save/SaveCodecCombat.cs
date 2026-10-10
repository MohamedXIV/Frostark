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
