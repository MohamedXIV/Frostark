using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Combat
{
    /// <summary>
    /// Authored weapon physics-neutral contract. WeaponId names the actual
    /// Pack item; ammo is the preexisting InventoryState item and ONLY source
    /// of shot ammunition. Tactical cooldown uses fixed combat ticks, not
    /// world minutes or a Unity Update delta.
    /// </summary>
    public sealed class WeaponDefinition
    {
        public EntityId Id { get; }
        public EntityId AmmoItemId { get; }
        public long AmmoPerShot { get; }
        public long CooldownTicks { get; }

        public WeaponDefinition(EntityId id, EntityId ammoItemId,
            long ammoPerShot, long cooldownTicks)
        {
            if (!id.IsValid) throw new ArgumentException("Weapon requires a stable item ID.", nameof(id));
            if (!ammoItemId.IsValid)
                throw new ArgumentException("Ammo requires a stable item ID.", nameof(ammoItemId));
            if (id == ammoItemId)
                throw new ArgumentException("Weapon item and ammunition must be distinct.");
            if (ammoPerShot <= 0) throw new ArgumentOutOfRangeException(nameof(ammoPerShot));
            if (cooldownTicks <= 0) throw new ArgumentOutOfRangeException(nameof(cooldownTicks));
            Id = id;
            AmmoItemId = ammoItemId;
            AmmoPerShot = ammoPerShot;
            CooldownTicks = cooldownTicks;
        }
    }

    /// <summary>
    /// Persisted firing state, bound to one authored weapon definition.
    /// This is NOT a second ammo store and contains no unverified hit result.
    /// </summary>
    public sealed class WeaponRuntimeState
    {
        public WeaponDefinition Definition { get; }
        public long NextReadyTick { get; }
        public long ShotCount { get; }

        internal WeaponRuntimeState(WeaponDefinition definition, long nextReadyTick, long shotCount)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (nextReadyTick < 0) throw new ArgumentOutOfRangeException(nameof(nextReadyTick));
            if (shotCount < 0) throw new ArgumentOutOfRangeException(nameof(shotCount));
            if (shotCount == 0 && nextReadyTick != 0)
                throw new ArgumentException("Unfired weapon cannot have a cooldown.");
            if (shotCount > 0 && nextReadyTick == 0)
                throw new ArgumentException("Fired weapon requires a cooldown checkpoint.");
            NextReadyTick = nextReadyTick;
            ShotCount = shotCount;
        }

        internal WeaponRuntimeState AfterFire(long tick) =>
            new WeaponRuntimeState(Definition,
                checked(tick + Definition.CooldownTicks), checked(ShotCount + 1));
    }
}
