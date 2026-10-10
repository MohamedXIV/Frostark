using System;
using FrostArk.Core.Inventory;
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
