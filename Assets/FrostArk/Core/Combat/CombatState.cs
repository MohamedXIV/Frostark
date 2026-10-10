using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Combat
{
    /// <summary>
    /// Immutable authored weapon set and tactical fixed-step counter. Does not
    /// own inventory, calendar, hit detection or the Unity physics clock.
    /// Only WorldCommandBus may turn a shot into a world revision.
    /// </summary>
    public sealed class CombatState
    {
        private readonly WeaponRuntimeState[] _weapons;
        public long Tick { get; }
        public IReadOnlyList<WeaponRuntimeState> Weapons => Array.AsReadOnly(_weapons);

        private CombatState(long tick, WeaponRuntimeState[] weapons)
        {
            Tick = tick;
            _weapons = weapons;
        }

        public static CombatState Empty => Create(Array.Empty<WeaponDefinition>());

        public static CombatState Create(IEnumerable<WeaponDefinition> weapons)
        {
            if (weapons == null) throw new ArgumentNullException(nameof(weapons));
            return Restore(0, weapons.Select(w =>
            {
                if (w == null) throw new ArgumentException("Null weapon definition.", nameof(weapons));
                return new WeaponRuntimeState(w, 0, 0);
            }));
        }

        internal static CombatState Restore(long tick, IEnumerable<WeaponRuntimeState> weapons)
        {
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (weapons == null) throw new ArgumentNullException(nameof(weapons));
            var array = weapons.ToArray();
            if (array.Any(x => x == null)) throw new ArgumentException("Null weapon.");
            array = array.OrderBy(x => x.Definition.Id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < array.Length; i++)
                if (array[i].Definition.Id == array[i - 1].Definition.Id)
                    throw new ArgumentException("Duplicate weapon identity.");
            foreach (var weapon in array)
            {
                if (weapon.ShotCount > 0 &&
                    (weapon.NextReadyTick < weapon.Definition.CooldownTicks ||
                     weapon.NextReadyTick - weapon.Definition.CooldownTicks > tick))
                    throw new ArgumentException("Invalid cooldown checkpoint: last shot is in the future.");
            }
            return new CombatState(tick, array);
        }

        public WeaponRuntimeState Get(EntityId weaponId) =>
            weaponId.IsValid ? Array.Find(_weapons, w => w.Definition.Id == weaponId) : null;

        public CombatState AtTick(long tick)
        {
            if (tick < Tick) throw new ArgumentOutOfRangeException(nameof(tick), "Combat tick cannot rewind.");
            if (tick == Tick) return this;
            return new CombatState(tick, _weapons);
        }

        internal CombatState Fired(EntityId weaponId)
        {
            var current = Get(weaponId);
            if (current == null) throw new ArgumentException("Unknown weapon.", nameof(weaponId));
            if (Tick < current.NextReadyTick)
                throw new InvalidOperationException("Fire called while cooling down.");
            var next = (WeaponRuntimeState[])_weapons.Clone();
            for (int i = 0; i < next.Length; i++)
                if (next[i].Definition.Id == weaponId)
                {
                    next[i] = current.AfterFire(Tick);
                    break;
                }
            return new CombatState(Tick, next);
        }

        public uint Fingerprint()
        {
            var output = new StringBuilder();
            void Add(string value) => output.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(value);
            Add(Tick.ToString(CultureInfo.InvariantCulture));
            foreach (var weapon in _weapons)
            {
                Add(weapon.Definition.Id.Value);
                Add(weapon.Definition.AmmoItemId.Value);
                Add(weapon.Definition.AmmoPerShot.ToString(CultureInfo.InvariantCulture));
                Add(weapon.Definition.CooldownTicks.ToString(CultureInfo.InvariantCulture));
                Add(weapon.NextReadyTick.ToString(CultureInfo.InvariantCulture));
                Add(weapon.ShotCount.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(output.ToString());
        }
    }
}
