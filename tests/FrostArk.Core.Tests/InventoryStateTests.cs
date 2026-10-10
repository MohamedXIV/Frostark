using System;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class InventoryStateTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        private static readonly EntityId Fuel = EntityId.Parse("item:fuel");

        private static InventoryState NewState(long packGrams = 2000, int packSlots = 2,
            long holdGrams = 20000, int holdSlots = 10)
        {
            return InventoryState.Create(
                new ItemCatalog(new[]
                {
                    new ItemDefinition(Scrap, 1, 250, 5),
                    new ItemDefinition(Fuel, 2, 1000, 1)
                }),
                new[]
                {
                    new SalvagePickup(EntityId.Parse("pickup:1"), Scrap, 3),
                    new SalvagePickup(EntityId.Parse("pickup:2"), Scrap, 4),
                    new SalvagePickup(EntityId.Parse("pickup:3"), Fuel, 2)
                },
                packGrams, packSlots, holdGrams, holdSlots);
        }

        [Test]
        public void PickupIsImmutableAndRetainsPersistentConsumedId()
        {
            var origin = NewState();
            var result = origin.TryCollect(EntityId.Parse("pickup:1"));

            Assert.That(result.Success, Is.True);
            Assert.That(origin.GetAmount(InventoryContainer.Pack, Scrap), Is.Zero);
            Assert.That(origin.IsCollected(EntityId.Parse("pickup:1")), Is.False);
            Assert.That(result.State.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(3));
            Assert.That(result.State.IsCollected(EntityId.Parse("pickup:1")), Is.True);
            Assert.That(result.State.GetUsedWeightGrams(InventoryContainer.Pack), Is.EqualTo(750));
            Assert.That(result.State.GetUsedSlots(InventoryContainer.Pack), Is.EqualTo(1));

            var repeated = result.State.TryCollect(EntityId.Parse("pickup:1"));
            Assert.That(repeated.Success, Is.False);
            Assert.That(repeated.Error, Is.EqualTo(InventoryError.AlreadyCollected));
            Assert.That(repeated.State, Is.SameAs(result.State));
        }

        [Test]
        public void QuantityAndSlotsMustFitBeforeAnEntirePickupCommits()
        {
            var tooHeavy = NewState(packGrams: 500).TryCollect(EntityId.Parse("pickup:1"));
            Assert.That(tooHeavy.Success, Is.False);
            Assert.That(tooHeavy.Error, Is.EqualTo(InventoryError.TooHeavy));
            Assert.That(tooHeavy.State.IsCollected(EntityId.Parse("pickup:1")), Is.False);

            var oneSlot = NewState(packSlots: 1);
            var first = oneSlot.TryCollect(EntityId.Parse("pickup:1"));
            Assert.That(first.Success, Is.True);
            // 3 + 4 scrap would require two stacks at limit 5.
            var second = first.State.TryCollect(EntityId.Parse("pickup:2"));
            Assert.That(second.Success, Is.False);
            Assert.That(second.Error, Is.EqualTo(InventoryError.NotEnoughSlots));
            Assert.That(second.State.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(3));
        }

        [Test]
        public void UnloadTransfersPackToPersistentHoldOnceAndTracksTwoCapacities()
        {
            var origin = NewState();
            var taken = origin.TryCollect(EntityId.Parse("pickup:1"));
            var unload = taken.State.TryUnload(Scrap, 2);

            Assert.That(unload.Success, Is.True);
            Assert.That(unload.State.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(1));
            Assert.That(unload.State.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(2));
            Assert.That(unload.State.GetUsedWeightGrams(InventoryContainer.ShipHold), Is.EqualTo(500));
            Assert.That(taken.State.GetAmount(InventoryContainer.ShipHold, Scrap), Is.Zero);

            var excess = unload.State.TryUnload(Scrap, 2);
            Assert.That(excess.Success, Is.False);
            Assert.That(excess.Error, Is.EqualTo(InventoryError.InsufficientStock));
            Assert.That(excess.State, Is.SameAs(unload.State));
            Assert.That(unload.State.IsCollected(EntityId.Parse("pickup:1")), Is.True);
        }

        [Test]
        public void InvalidTransfersAndUnknownPickupsNeverMutateState()
        {
            var origin = NewState();
            var unknown = origin.TryCollect(EntityId.Parse("pickup:unknown"));
            var invalid = origin.TryUnload(Scrap, -1);

            Assert.That(unknown.Error, Is.EqualTo(InventoryError.UnknownPickup));
            Assert.That(invalid.Error, Is.EqualTo(InventoryError.InvalidQuantity));
            Assert.That(unknown.State, Is.SameAs(origin));
            Assert.That(invalid.State, Is.SameAs(origin));
        }

        [Test]
        public void PackConsumeSpendsRealCarriedStockNotShipHold()
        {
            var collected = NewState().TryCollect(EntityId.Parse("pickup:1")).State;
            var unloaded = collected.TryUnload(Scrap, 2).State;
            var spent = unloaded.TryConsumePack(Scrap, 1);
            Assert.That(spent.Success, Is.True);
            Assert.That(spent.State.GetAmount(InventoryContainer.Pack, Scrap), Is.Zero);
            Assert.That(spent.State.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(2));
            Assert.That(unloaded.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(1));
            Assert.That(spent.State.IsCollected(EntityId.Parse("pickup:1")), Is.True);
            Assert.That(spent.State.GetUsedSlots(InventoryContainer.Pack), Is.Zero);
            Assert.That(spent.State.GetUsedWeightGrams(InventoryContainer.Pack), Is.Zero);
            Assert.That(spent.State.Fingerprint(), Is.Not.EqualTo(unloaded.Fingerprint()));
        }

        [Test]
        public void PackConsumeRejectsUnknownMissingAndOverspendAtomically()
        {
            var start = NewState();
            var taken = start.TryCollect(EntityId.Parse("pickup:1")).State;
            foreach (var invalid in new[] {
                taken.TryConsumePack(Scrap, 0),
                taken.TryConsumePack(Scrap, -1),
                taken.TryConsumePack(Scrap, 4),
                taken.TryConsumePack(Fuel, 1),
                taken.TryConsumePack(EntityId.Parse("item:unknown"), 1)
            })
            {
                Assert.That(invalid.Success, Is.False);
                Assert.That(invalid.State, Is.SameAs(taken));
                Assert.That(invalid.State.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(3));
            }
            var uncollected = start.TryConsumePack(Scrap, 1);
            Assert.That(uncollected.Error, Is.EqualTo(InventoryError.InsufficientStock));
            Assert.That(uncollected.State, Is.SameAs(start));
        }

        [Test]
        public void ConsumedPackItemSurvivesCanonicalSaveRoundTrip()
        {
            var pack = NewState().TryCollect(EntityId.Parse("pickup:1")).State;
            var spent = pack.TryConsumePack(Scrap, 2);
            Assert.That(spent.Success, Is.True);
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(), inventory: spent.State);
            var scheduler = new SimulationScheduler(new WorldClock(world.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(world, scheduler));
            var loaded = SaveCodec.TryDecode(bytes);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.Inventory.GetAmount(InventoryContainer.Pack, Scrap),
                Is.EqualTo(1));
            Assert.That(loaded.Value.State.Inventory.IsCollected(EntityId.Parse("pickup:1")), Is.True);
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(world.Fingerprint()));
            Assert.That(SaveCodec.Encode(loaded.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void InvalidAuthoredItemsOrDuplicateWorldPickupsAreRejected()
        {
            var catalog = new ItemCatalog(new[] { new ItemDefinition(Scrap, 1, 250, 5) });
            Assert.Throws<ArgumentException>(() => InventoryState.Create(catalog,
                new[] { new SalvagePickup(EntityId.Parse("pickup:missing"), Fuel, 1) },
                5000, 5, 10000, 10));
            var pickup = new SalvagePickup(EntityId.Parse("pickup:duplicate"), Scrap, 1);
            Assert.Throws<ArgumentException>(() => InventoryState.Create(catalog,
                new[] { pickup, pickup }, 5000, 5, 10000, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => InventoryState.Create(catalog,
                Array.Empty<SalvagePickup>(), -1, 5, 10000, 10));
        }
    }
}
