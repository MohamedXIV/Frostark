using System;
using System.Collections.Generic;
using FrostArk.Core.Economy;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorkshopInventoryAuthorityTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        private static readonly EntityId Heater = EntityId.Parse("item:heater");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:scrap");

        private static InventoryState Held(int shipSlots = 4)
        {
            var inv = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Scrap, 1, 250, 10),
                new ItemDefinition(Heater, 1, 2000, 1)
            }), new[] { new SalvagePickup(Pickup, Scrap, 8) },
                packWeightGrams: 20_000, packSlots: 10, shipWeightGrams: 20_000,
                shipSlots: shipSlots);
            var pickup = inv.TryCollect(Pickup);
            Assert.That(pickup.Success, Is.True);
            var unload = pickup.State.TryUnload(Scrap, 8);
            Assert.That(unload.Success, Is.True);
            return unload.State;
        }

        [Test]
        public void AtomicWorkshopExchangeCannotConsumeMaterialsTwice()
        {
            var starting = Held();
            var output = starting.TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 3) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) });

            Assert.That(output.Success, Is.True);
            Assert.That(starting.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(8));
            Assert.That(output.State.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(5));
            Assert.That(output.State.GetAmount(InventoryContainer.ShipHold, Heater), Is.EqualTo(1));
            Assert.That(output.State.SnapshotCredited(), Has.Count.EqualTo(1));

            var over = output.State.TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 6) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) });
            Assert.That(over.Success, Is.False);
            Assert.That(over.Error, Is.EqualTo(InventoryError.InsufficientStock));
            Assert.That(over.State, Is.SameAs(output.State));
        }

        [Test]
        public void StockAndSlotCapacityAreCheckedBeforeAnyOutputIsGranted()
        {
            var starting = Held(shipSlots: 1);
            var rejected = starting.TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 3) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) });
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Error, Is.EqualTo(InventoryError.NotEnoughSlots));
            Assert.That(rejected.State, Is.SameAs(starting));
            Assert.That(rejected.State.GetAmount(InventoryContainer.ShipHold, Scrap),
                Is.EqualTo(8));
        }

        [Test]
        public void CraftedCreditsSurviveSaveRestoreInsteadOfFailingSalvageProvenance()
        {
            var output = Held().TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 3) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) });
            Assert.That(output.Success, Is.True);
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(), inventory: output.State);
            var scheduler = new SimulationScheduler(new WorldClock(world.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(world, scheduler));
            var restored = SaveCodec.TryDecode(bytes);
            Assert.That(restored.Success, Is.True, restored.Error.ToString());
            Assert.That(restored.Value.State.Inventory.GetAmount(
                InventoryContainer.ShipHold, Heater), Is.EqualTo(1));
            Assert.That(SaveCodec.Encode(restored.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void ChecksummedInvalidCreditEntryIsNotAValidSave()
        {
            var produced = Held().TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 3) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) }).State;
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(), inventory: produced);
            var timer = new SimulationScheduler(new WorldClock(world.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var blob = SaveCodec.Encode(SaveGameDto.Capture(world, timer));
            // v9 appends three empty Workshop collections (12 bytes) AFTER
            // the v8 credited heater quantity; target the actual credited row.
            Array.Copy(BitConverter.GetBytes(0L), 0, blob, blob.Length - 32 - 20, 8);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                Array.Copy(hash.ComputeHash(blob, 0, blob.Length - 32),
                    0, blob, blob.Length - 32, 32);

            var rejected = SaveCodec.TryDecode(blob);
            Assert.That(rejected.Success, Is.False);
            Assert.That(rejected.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void GearAccessRequiresPossessionOfActualItem()
        {
            var gear = new GearProtection(Heater, 900, 800);
            var hazard = new EnvironmentalHazard(EntityId.Parse("zone:storm"), 700, 300);
            var noGear = Held();
            var withGear = noGear.TryExchangeShip(
                new[] { new KeyValuePair<EntityId, long>(Scrap, 3) },
                new[] { new KeyValuePair<EntityId, long>(Heater, 1) }).State;

            Assert.That(GearAccess.CanEnterCarrying(noGear, InventoryContainer.ShipHold,
                gear, hazard), Is.False);
            Assert.That(GearAccess.CanEnterCarrying(withGear, InventoryContainer.ShipHold,
                gear, hazard), Is.True);
            Assert.That(GearAccess.CanEnterCarrying(withGear, InventoryContainer.Pack,
                gear, hazard), Is.False);
        }
    }
}
