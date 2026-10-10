using System;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WeaponFireWorldTests
    {
        private static readonly EntityId Rifle = EntityId.Parse("item:rifle");
        private static readonly EntityId Rounds = EntityId.Parse("item:rifle-ammo");
        private static readonly EntityId RiflePickup = EntityId.Parse("pickup:rifle");
        private static readonly EntityId AmmoPickup = EntityId.Parse("pickup:ammo");

        private static WorldState Start(long ammo = 10)
        {
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Rifle, 1, 3000, 1),
                new ItemDefinition(Rounds, 1, 20, 100)
            }), new[]
            {
                new SalvagePickup(RiflePickup, Rifle, 1),
                new SalvagePickup(AmmoPickup, Rounds, ammo)
            }, 20000, 5, 50000, 10);
            inventory = inventory.TryCollect(RiflePickup).State.TryCollect(AmmoPickup).State;
            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(Rifle, Rounds, ammoPerShot: 2, cooldownTicks: 3)
            });
            return WorldState.Create(new WorldMinute(480),
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: inventory, combat: combat);
        }

        [Test]
        public void FireConsumesOnlyCarriedAmmoAndRecordsOneRevisionedShot()
        {
            var before = Start();
            var fire = new FireWeaponCommand(EntityId.Parse("command:first-shot"), Rifle, 0);
            var result = new WorldCommandBus().Execute(before, fire);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(8));
            Assert.That(result.State.Inventory.GetAmount(InventoryContainer.Pack, Rifle), Is.EqualTo(1));
            Assert.That(before.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(10));
            Assert.That(result.State.Combat.Get(Rifle).ShotCount, Is.EqualTo(1));
            Assert.That(result.State.Combat.Get(Rifle).NextReadyTick, Is.EqualTo(3));
            Assert.That(result.State.Revision, Is.EqualTo(1));
            Assert.That(result.Events[0].Kind, Is.EqualTo(WorldEventKind.WeaponFired));
            Assert.That(result.Events[0].Quantity, Is.EqualTo(2));
            Assert.That(result.Events[0].RelatedId, Is.EqualTo(Rounds));
        }

        [Test]
        public void SameTickCannotBypassCooldownAndReplayCannotFireTwice()
        {
            var bus = new WorldCommandBus();
            var shot = new FireWeaponCommand(EntityId.Parse("command:shot"), Rifle, 0);
            var first = bus.Execute(Start(), shot);
            var replay = bus.Execute(first.State, shot);
            var tooSoon = bus.Execute(first.State, new FireWeaponCommand(
                EntityId.Parse("command:soon"), Rifle, 1));
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(tooSoon.Failure, Is.EqualTo(CommandFailure.WeaponCoolingDown));
            Assert.That(tooSoon.State, Is.SameAs(first.State));
            Assert.That(tooSoon.Events, Is.Empty);

            var tick2 = first.State.AtCombatTick(2);
            var rejected = bus.Execute(tick2, new FireWeaponCommand(
                EntityId.Parse("command:still-cold"), Rifle, 1));
            Assert.That(rejected.Failure, Is.EqualTo(CommandFailure.WeaponCoolingDown));
            var tick3 = tick2.AtCombatTick(3);
            var second = bus.Execute(tick3, new FireWeaponCommand(
                EntityId.Parse("command:second"), Rifle, 1));
            Assert.That(second.Success, Is.True);
            Assert.That(second.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(6));
            Assert.That(second.State.Combat.Get(Rifle).ShotCount, Is.EqualTo(2));
            Assert.That(second.State.Minute, Is.EqualTo(first.State.Minute));
            Assert.That(second.State.Revision, Is.EqualTo(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => tick3.AtCombatTick(2));
        }

        [Test]
        public void EmptyPackAndNotCarriedWeaponRejectWithoutFreeFire()
        {
            var bus = new WorldCommandBus();
            var emptyAmmo = Start(ammo: 1);
            var denied = bus.Execute(emptyAmmo, new FireWeaponCommand(
                EntityId.Parse("command:no-ammo"), Rifle, 0));
            Assert.That(denied.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
            Assert.That(denied.State, Is.SameAs(emptyAmmo));
            Assert.That(denied.Events, Is.Empty);

            var world = Start();
            var disarmed = world.Inventory.TryConsumePack(Rifle, 1);
            Assert.That(disarmed.Success, Is.True);
            var altered = WorldState.Create(world.Minute,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: disarmed.State, combat: world.Combat);
            var withoutGun = bus.Execute(altered, new FireWeaponCommand(
                EntityId.Parse("command:unarmed"), Rifle, 0));
            Assert.That(withoutGun.Failure, Is.EqualTo(CommandFailure.WeaponNotCarried));
            Assert.That(withoutGun.State, Is.SameAs(altered));
        }

        [Test]
        public void SaveReloadPreservesAmmoCooldownCommandLedgerAndRapidShotCadence()
        {
            var first = new WorldCommandBus().Execute(Start(),
                new FireWeaponCommand(EntityId.Parse("command:first"), Rifle, 0));
            var mid = first.State.AtCombatTick(2);
            var clock = new SimulationScheduler(new WorldClock(mid.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 777);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(mid, clock));
            var load = SaveCodec.TryDecode(bytes);
            Assert.That(load.Success, Is.True, load.Error.ToString());
            Assert.That(SaveCodec.Encode(load.Value), Is.EqualTo(bytes));
            Assert.That(load.Value.State.Fingerprint(), Is.EqualTo(mid.Fingerprint()));

            var tooSoon = new WorldCommandBus().Execute(load.Value.State,
                new FireWeaponCommand(EntityId.Parse("command:soon"), Rifle, 1));
            Assert.That(tooSoon.Failure, Is.EqualTo(CommandFailure.WeaponCoolingDown));
            var after = load.Value.State.AtCombatTick(3);
            var allowed = new WorldCommandBus().Execute(after,
                new FireWeaponCommand(EntityId.Parse("command:second"), Rifle, 1));
            Assert.That(allowed.Success, Is.True);
            Assert.That(allowed.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds),
                Is.EqualTo(6));
        }

        [Test]
        public void InvalidDefinitionsAndDuplicateWeaponIdsCannotSpawnAuthoritativeWeapons()
        {
            Assert.Throws<ArgumentException>(() => new WeaponDefinition(default, Rounds, 1, 2));
            Assert.Throws<ArgumentException>(() => new WeaponDefinition(Rifle, default, 1, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDefinition(Rifle, Rounds, 0, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WeaponDefinition(Rifle, Rounds, 1, 0));
            var rifle = new WeaponDefinition(Rifle, Rounds, 1, 2);
            Assert.Throws<ArgumentException>(() => CombatState.Create(new[] { rifle, rifle }));
        }
    }
}
