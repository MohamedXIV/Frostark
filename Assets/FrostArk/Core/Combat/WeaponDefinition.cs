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
        public long MagazineCapacity { get; }
        public long ReloadTicks { get; }

        public WeaponDefinition(EntityId id, EntityId ammoItemId,
            long ammoPerShot, long cooldownTicks, long magazineCapacity = 0, long reloadTicks = 0)
        {
            if (!id.IsValid) throw new ArgumentException("Weapon requires a stable item ID.", nameof(id));
            if (!ammoItemId.IsValid)
                throw new ArgumentException("Ammo requires a stable item ID.", nameof(ammoItemId));
            if (id == ammoItemId)
                throw new ArgumentException("Weapon item and ammunition must be distinct.");
            if (ammoPerShot <= 0) throw new ArgumentOutOfRangeException(nameof(ammoPerShot));
            if (cooldownTicks <= 0) throw new ArgumentOutOfRangeException(nameof(cooldownTicks));
            if (magazineCapacity < 0) throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            if (magazineCapacity > 0 && reloadTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(reloadTicks), "Magazine weapon requires positive reload ticks.");
            Id = id;
            AmmoItemId = ammoItemId;
            AmmoPerShot = ammoPerShot;
            CooldownTicks = cooldownTicks;
            MagazineCapacity = magazineCapacity;
            ReloadTicks = reloadTicks;
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
        public long MagazineRounds { get; }

        internal WeaponRuntimeState(WeaponDefinition definition, long nextReadyTick, long shotCount, long magazineRounds = 0)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (nextReadyTick < 0) throw new ArgumentOutOfRangeException(nameof(nextReadyTick));
            if (shotCount < 0) throw new ArgumentOutOfRangeException(nameof(shotCount));
            if (magazineRounds < 0) throw new ArgumentOutOfRangeException(nameof(magazineRounds));
            if (definition.MagazineCapacity > 0 && magazineRounds > definition.MagazineCapacity)
                throw new ArgumentOutOfRangeException(nameof(magazineRounds), "Magazine rounds cannot exceed capacity.");
            if (shotCount == 0 && nextReadyTick != 0 && definition.MagazineCapacity <= 0)
                throw new ArgumentException("Unfired weapon cannot have a cooldown.");
            if (shotCount > 0 && nextReadyTick == 0)
                throw new ArgumentException("Fired weapon requires a cooldown checkpoint.");
            NextReadyTick = nextReadyTick;
            ShotCount = shotCount;
            MagazineRounds = magazineRounds;
        }

        internal WeaponRuntimeState AfterFire(long tick)
        {
            long newMag = Definition.MagazineCapacity > 0
                ? Math.Max(0, MagazineRounds - Definition.AmmoPerShot)
                : MagazineRounds;
            return new WeaponRuntimeState(Definition,
                checked(tick + Definition.CooldownTicks), checked(ShotCount + 1), newMag);
        }

        internal WeaponRuntimeState AfterReload(long tick, long roundsLoaded)
        {
            if (Definition.MagazineCapacity <= 0)
                throw new InvalidOperationException("Cannot reload a non-magazine weapon.");
            long newMag = checked(MagazineRounds + roundsLoaded);
            if (newMag > Definition.MagazineCapacity)
                throw new ArgumentException("Rounds loaded exceeds magazine capacity.");
            return new WeaponRuntimeState(Definition,
                checked(tick + Definition.ReloadTicks), ShotCount, newMag);
        }
    }
}
