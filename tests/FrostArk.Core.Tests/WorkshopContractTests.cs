using System;
using FrostArk.Core.Economy;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorkshopContractTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        private static readonly EntityId Fuel = EntityId.Parse("item:fuel");
        private static readonly EntityId Heater = EntityId.Parse("item:heater");

        [Test]
        public void AuthoredRecipeKeepsVersionedIdentityAndMaterialsForOneBatch()
        {
            var recipe = new WorkshopRecipe(
                EntityId.Parse("recipe:heater"), version: 2,
                stationId: EntityId.Parse("station:workbench"), minutesPerBatch: 60,
                inputs: new[] { new RecipeStack(Scrap, 3), new RecipeStack(Fuel, 1) },
                outputs: new[] { new RecipeStack(Heater, 1) });
            var catalog = new WorkshopCatalog(new[] { recipe });
            Assert.That(catalog.Get(recipe.Id), Is.SameAs(recipe));
            Assert.That(recipe.MinutesPerBatch, Is.EqualTo(60));
            Assert.That(recipe.Inputs, Has.Count.EqualTo(2));
            Assert.That(recipe.Inputs[0].Quantity, Is.EqualTo(1)); // deterministic ID ordering
            Assert.That(recipe.Inputs[1].Quantity, Is.EqualTo(3));
            Assert.That(recipe.Outputs[0].ItemId, Is.EqualTo(Heater));
            Assert.That(recipe.Version, Is.EqualTo(2));
        }

        [Test]
        public void RejectDuplicateRecipesAndInvalidStackAndStationContracts()
        {
            var valid = new WorkshopRecipe(EntityId.Parse("recipe:a"), 1,
                EntityId.Parse("station:bench"), 4,
                new[] { new RecipeStack(Scrap, 1) },
                new[] { new RecipeStack(Heater, 1) });
            Assert.Throws<ArgumentException>(() => new WorkshopCatalog(new[] { valid, valid }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecipeStack(Scrap, 0));
            Assert.Throws<ArgumentException>(() => new RecipeStack(default, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorkshopRecipe(
                EntityId.Parse("recipe:bad"), 0,
                EntityId.Parse("station:bench"), 1,
                new[] { new RecipeStack(Scrap, 1) },
                new[] { new RecipeStack(Heater, 1) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorkshopRecipe(
                EntityId.Parse("recipe:bad"), 1,
                EntityId.Parse("station:bench"), 0,
                new[] { new RecipeStack(Scrap, 1) },
                new[] { new RecipeStack(Heater, 1) }));
            Assert.Throws<ArgumentException>(() => new WorkshopRecipe(
                EntityId.Parse("recipe:bad"), 1, default, 1,
                new[] { new RecipeStack(Scrap, 1) },
                new[] { new RecipeStack(Heater, 1) }));
            Assert.Throws<ArgumentException>(() => new WorkshopRecipe(
                EntityId.Parse("recipe:bad"), 1,
                EntityId.Parse("station:bench"), 1,
                new[] { new RecipeStack(Scrap, 1), new RecipeStack(Scrap, 2) },
                new[] { new RecipeStack(Heater, 1) }));
        }

        [Test]
        public void GearThresholdsCheckActualProtectionNotArbitraryPlayerLevels()
        {
            var suit = new GearProtection(
                EntityId.Parse("gear:sealed-parka"),
                coldProtection: 700, contaminationProtection: 350);
            var icy = new EnvironmentalHazard(
                EntityId.Parse("zone:ice"), requiredColdProtection: 600,
                requiredContaminationProtection: 0);
            var fog = new EnvironmentalHazard(
                EntityId.Parse("zone:fog"), requiredColdProtection: 400,
                requiredContaminationProtection: 500);

            Assert.That(GearAccess.CanEnter(suit, icy), Is.True);
            Assert.That(GearAccess.CanEnter(suit, fog), Is.False);
            Assert.That(GearAccess.CanEnter(null, icy), Is.False);
            Assert.That(GearAccess.CanEnter(null,
                new EnvironmentalHazard(EntityId.Parse("zone:mild"), 0, 0)), Is.True);
        }

        [Test]
        public void InvalidHazardAndGearValuesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GearProtection(
                EntityId.Parse("gear:bad"), 1001, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new EnvironmentalHazard(
                EntityId.Parse("zone:bad"), 0, -1));
            Assert.Throws<ArgumentException>(() => new GearProtection(default, 1, 1));
            Assert.Throws<ArgumentException>(() => new EnvironmentalHazard(default, 0, 0));
        }
    }
}
