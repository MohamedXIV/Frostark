using System;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WeaponReloadWorldTests
    {
        private static readonly EntityId Pistol = EntityId.Parse("item:pistol");
        private static readonly EntityId Rounds = EntityId.Parse("item:pistol-ammo");
        private static readonly EntityId Musket = EntityId.Parse("item:musket");
        private static readonly EntityId PistolPickup = EntityId.Parse("pickup:pistol");
        private static readonly EntityId MusketPickup = EntityId.Parse("pickup:musket");
        private static readonly EntityId AmmoPickup = EntityId.Parse("pickup:ammo");

        private static WorldState CreateState(long packAmmo = 10)
        {
            var pickups = new System.Collections.Generic.List<SalvagePickup>
            {
                new SalvagePickup(PistolPickup, Pistol, 1),
                new SalvagePickup(MusketPickup, Musket, 1)
            };
            if (packAmmo > 0)
            {
                pickups.Add(new SalvagePickup(AmmoPickup, Rounds, packAmmo));
            }

            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Pistol, 1, 1000, 1),
                new ItemDefinition(Musket, 1, 3000, 1),
                new ItemDefinition(Rounds, 1, 20, 100)
            }), pickups, 20000, 5, 50000, 10);

            inventory = inventory.TryCollect(PistolPickup).State;
            inventory = inventory.TryCollect(MusketPickup).State;
            if (packAmmo > 0)
            {
                inventory = inventory.TryCollect(AmmoPickup).State;
            }

            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(Pistol, Rounds, ammoPerShot: 1, cooldownTicks: 2,
                    magazineCapacity: 3, reloadTicks: 10),
                new WeaponDefinition(Musket, Rounds, ammoPerShot: 1, cooldownTicks: 5,
                    magazineCapacity: 0, reloadTicks: 0)
            });

            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: inventory, combat: combat);
        }

        [Test]
        public void MagazineWeaponInitializesWithFullMagazine()
        {
            var state = CreateState();
            var weapon = state.Combat.Get(Pistol);
            Assert.That(weapon, Is.Not.Null);
            Assert.That(weapon.Definition.MagazineCapacity, Is.EqualTo(3));
            Assert.That(weapon.Definition.ReloadTicks, Is.EqualTo(10));
            Assert.That(weapon.MagazineRounds, Is.EqualTo(3));
            Assert.That(weapon.ShotCount, Is.EqualTo(0));
            Assert.That(weapon.NextReadyTick, Is.EqualTo(0));
        }

        [Test]
        public void ShotsDepleteMagazineAndRejectWhenEmpty()
        {
            var state = CreateState();
            var bus = new WorldCommandBus();

            // Shot 1
            var shot1 = bus.Execute(state, new FireWeaponCommand(EntityId.Parse("cmd:s1"), Pistol, 0));
            Assert.That(shot1.Success, Is.True);
            Assert.That(shot1.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(2));
            Assert.That(shot1.State.Combat.Get(Pistol).ShotCount, Is.EqualTo(1));
            // Carried inventory in Pack is unaffected during magazine discharge
            Assert.That(shot1.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(10));

            // Advance tick past cooldown
            var tick2 = shot1.State.AtCombatTick(2);

            // Shot 2
            var shot2 = bus.Execute(tick2, new FireWeaponCommand(EntityId.Parse("cmd:s2"), Pistol, 1));
            Assert.That(shot2.Success, Is.True);
            Assert.That(shot2.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(1));

            var tick4 = shot2.State.AtCombatTick(4);

            // Shot 3 (empties magazine)
            var shot3 = bus.Execute(tick4, new FireWeaponCommand(EntityId.Parse("cmd:s3"), Pistol, 2));
            Assert.That(shot3.Success, Is.True);
            Assert.That(shot3.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(0));

            var tick6 = shot3.State.AtCombatTick(6);

            // Shot 4: magazine is empty!
            var shot4 = bus.Execute(tick6, new FireWeaponCommand(EntityId.Parse("cmd:s4"), Pistol, 3));
            Assert.That(shot4.Success, Is.False);
            Assert.That(shot4.Failure, Is.EqualTo(CommandFailure.MagazineEmpty));
            Assert.That(shot4.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(0));
        }

        [Test]
        public void ReloadConsumesPackAmmoAndEnforcesReloadCooldown()
        {
            var state = CreateState(packAmmo: 5);
            var bus = new WorldCommandBus();

            // Fire 2 shots out of 3
            var s1 = bus.Execute(state, new FireWeaponCommand(EntityId.Parse("cmd:s1"), Pistol, 0));
            var s2 = bus.Execute(s1.State.AtCombatTick(2), new FireWeaponCommand(EntityId.Parse("cmd:s2"), Pistol, 1));
            Assert.That(s2.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(1));
            Assert.That(s2.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(5));

            // Reload weapon
            var tick4 = s2.State.AtCombatTick(4);
            var reloadCmd = new ReloadWeaponCommand(EntityId.Parse("cmd:reload"), Pistol, 2);
            var reloadResult = bus.Execute(tick4, reloadCmd);

            Assert.That(reloadResult.Success, Is.True);
            Assert.That(reloadResult.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(3)); // 2 loaded
            Assert.That(reloadResult.State.Inventory.GetAmount(InventoryContainer.Pack, Rounds), Is.EqualTo(3)); // 5 - 2 = 3
            Assert.That(reloadResult.State.Combat.Get(Pistol).NextReadyTick, Is.EqualTo(4 + 10)); // Tick + ReloadTicks
            Assert.That(reloadResult.Events.Count, Is.EqualTo(1));
            Assert.That(reloadResult.Events[0].Kind, Is.EqualTo(WorldEventKind.WeaponReloaded));
            Assert.That(reloadResult.Events[0].Quantity, Is.EqualTo(2));

            // Cannot fire while reloading cooldown is active
            var fireDuringReload = bus.Execute(reloadResult.State.AtCombatTick(10),
                new FireWeaponCommand(EntityId.Parse("cmd:early"), Pistol, 3));
            Assert.That(fireDuringReload.Failure, Is.EqualTo(CommandFailure.WeaponCoolingDown));

            // Can fire once reload cooldown expires
            var fireAfterReload = bus.Execute(reloadResult.State.AtCombatTick(14),
                new FireWeaponCommand(EntityId.Parse("cmd:ready"), Pistol, 3));
            Assert.That(fireAfterReload.Success, Is.True);
            Assert.That(fireAfterReload.State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(2));
        }

        [Test]
        public void ReloadValidationsRejectFullMagEmptyPackAndNonMagazineWeapon()
        {
            var state = CreateState(packAmmo: 0);
            var bus = new WorldCommandBus();

            // 1. Reloading full magazine rejected
            var fullReload = bus.Execute(state, new ReloadWeaponCommand(EntityId.Parse("cmd:r:full"), Pistol, 0));
            Assert.That(fullReload.Failure, Is.EqualTo(CommandFailure.MagazineAlreadyFull));

            // 2. Reloading non-magazine weapon rejected
            var musketReload = bus.Execute(state, new ReloadWeaponCommand(EntityId.Parse("cmd:r:musket"), Musket, 0));
            Assert.That(musketReload.Failure, Is.EqualTo(CommandFailure.WeaponCannotReload));

            // Fire to make room
            var fired = bus.Execute(state, new FireWeaponCommand(EntityId.Parse("cmd:f"), Pistol, 0));
            var tick2 = fired.State.AtCombatTick(2);

            // 3. Reloading when pack has no ammo rejected
            var emptyPackReload = bus.Execute(tick2, new ReloadWeaponCommand(EntityId.Parse("cmd:r:empty"), Pistol, 1));
            Assert.That(emptyPackReload.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
        }
    }
}
