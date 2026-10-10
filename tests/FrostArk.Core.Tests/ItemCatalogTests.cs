using System;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ItemCatalogTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");

        [Test]
        public void CatalogStoresVersionedCaseSensitiveDefinitions()
        {
            var item = new ItemDefinition(Scrap, contentVersion: 2,
                unitWeightGrams: 250, maxStackSize: 10);
            var catalog = new ItemCatalog(new[] { item });

            Assert.That(catalog.Get(Scrap), Is.SameAs(item));
            Assert.That(catalog.Get(EntityId.Parse("Item:scrap")), Is.Null);
            Assert.That(item.ContentVersion, Is.EqualTo(2));
            Assert.That(item.RequiredSlots(21), Is.EqualTo(3));
            Assert.That(item.TotalWeightGrams(21), Is.EqualTo(5250));
            Assert.That(item.RequiredSlots(0), Is.Zero);
        }

        [Test]
        public void DuplicateDefinitionsAndInvalidWeightsStackLimitsOrVersionsAreRejected()
        {
            var item = new ItemDefinition(Scrap, 1, 250, 10);
            Assert.Throws<ArgumentException>(() => new ItemCatalog(new[] { item, item }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemDefinition(Scrap, 0, 250, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemDefinition(Scrap, 1, -1, 10));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ItemDefinition(Scrap, 1, 250, 0));
            Assert.Throws<ArgumentException>(() => new ItemDefinition(default, 1, 250, 10));
        }

        [Test]
        public void PickupRequiresStableIdentityAndKnownPositiveQuantity()
        {
            var pickup = new SalvagePickup(EntityId.Parse("pickup:shore-03"),
                Scrap, quantity: 4);
            Assert.That(pickup.Id.Value, Is.EqualTo("pickup:shore-03"));
            Assert.That(pickup.ItemId, Is.EqualTo(Scrap));
            Assert.That(pickup.Quantity, Is.EqualTo(4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SalvagePickup(
                EntityId.Parse("pickup:bad"), Scrap, 0));
            Assert.Throws<ArgumentException>(() => new SalvagePickup(default, Scrap, 1));
            Assert.Throws<ArgumentException>(() => new SalvagePickup(
                EntityId.Parse("pickup:bad"), default, 1));
        }

        [Test]
        public void LargeStockArithmeticRejectsOverflowInsteadOfWrapping()
        {
            var item = new ItemDefinition(Scrap, 1, long.MaxValue, 10);
            Assert.Throws<OverflowException>(() => item.TotalWeightGrams(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => item.RequiredSlots(-1));
            Assert.That(item.RequiredSlots(long.MaxValue),
                Is.EqualTo(long.MaxValue / 10 + 1));
        }
    }
}
