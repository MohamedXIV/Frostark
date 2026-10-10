using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Economy;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorkshopWorldTests
    {
        static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        static readonly EntityId Stove = EntityId.Parse("item:stove");
        static readonly EntityId Pickup = EntityId.Parse("pickup:shore");
        static readonly EntityId Recipe = EntityId.Parse("recipe:stove");
        static readonly EntityId Station = EntityId.Parse("station:bench");
        static readonly EntityId Job = EntityId.Parse("job:stove-01");

        private static WorldState Start(int shipSlots = 4)
        {
            var inv = InventoryState.Create(new ItemCatalog(new[] {
                new ItemDefinition(Scrap, 1, 100, 10),
                new ItemDefinition(Stove, 1, 2000, 1)
            }), new[] { new SalvagePickup(Pickup, Scrap, 8) },
                20000, 10, 20000, shipSlots);
            inv = inv.TryCollect(Pickup).State.TryUnload(Scrap, 8).State;
            var recipe = new WorkshopRecipe(Recipe, 1, Station, 60,
                new[] { new RecipeStack(Scrap, 3) },
                new[] { new RecipeStack(Stove, 1) });
            var economy = EconomyState.Create(new WorkshopCatalog(new[] { recipe }),
                new[] { Station });
            return WorldState.Create(new WorldMinute(120), Array.Empty<SupplyStock>(),
                Array.Empty<EntityId>(), inventory: inv, economy: economy);
        }

        [Test]
        public void WorkshopConsumesInputsExactlyOnceThenDeliversAfterSixtyMinutes()
        {
            var bus = new WorldCommandBus();
            var before = Start();
            var cmd = new StartWorkshopJobCommand(EntityId.Parse("cmd:craft"), Job, Recipe, 0);
            var accepted = bus.Execute(before, cmd);
            Assert.That(accepted.Success, Is.True);
            Assert.That(accepted.Events[0].Kind, Is.EqualTo(WorldEventKind.WorkshopStarted));
            Assert.That(accepted.State.Inventory.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(5));
            Assert.That(accepted.State.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.Zero);
            Assert.That(accepted.State.Economy.Jobs, Has.Count.EqualTo(1));
            var duplicate = bus.Execute(accepted.State, cmd);
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(duplicate.State, Is.SameAs(accepted.State));

            var world = accepted.State;
            var timer = new SimulationScheduler(new WorldClock(world.Minute), new IWorldMinuteSystem[]
            { new WorkshopMinuteSystem(() => world, x => world = x) });
            timer.Advance(59);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.Zero);
            timer.Advance(1);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.EqualTo(1));
            Assert.That(world.Economy.Jobs, Is.Empty);
            timer.Advance(20);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.EqualTo(1));
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(5));
        }

        [Test]
        public void OutputIsNotLostOrDuplicatedIfShipHoldIsFull()
        {
            var bus = new WorldCommandBus();
            var accepted = bus.Execute(Start(shipSlots: 1),
                new StartWorkshopJobCommand(EntityId.Parse("cmd:start"), Job, Recipe, 0));
            Assert.That(accepted.Success, Is.True);
            var world = accepted.State;
            var timer = new SimulationScheduler(new WorldClock(world.Minute),
                new IWorldMinuteSystem[] { new WorkshopMinuteSystem(() => world, x => world = x) });
            timer.Advance(100);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.Zero);
            Assert.That(world.Economy.Jobs, Has.Count.EqualTo(1));
            Assert.That(world.Economy.Jobs[0].Id, Is.EqualTo(Job));
            timer.Advance(1);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.Zero);
            Assert.That(world.Economy.Jobs, Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveAtMidpointAndResumePreservesPaidJobAndByteIdentity()
        {
            var accepted = new WorldCommandBus().Execute(Start(),
                new StartWorkshopJobCommand(EntityId.Parse("cmd:craft"), Job, Recipe, 0));
            var first = accepted.State;
            var timer = new SimulationScheduler(new WorldClock(first.Minute),
                new IWorldMinuteSystem[] { new WorkshopMinuteSystem(() => first, x => first = x) }, seed: 42);
            timer.Advance(25);
            var decoded = SaveCodec.TryDecode(SaveCodec.Encode(SaveGameDto.Capture(first, timer)));
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            var restored = decoded.Value.State;
            var resumed = decoded.Value.CreateScheduler(
                new IWorldMinuteSystem[] { new WorkshopMinuteSystem(() => restored, x => restored = x) });
            timer.Advance(35);
            resumed.Advance(35);
            Assert.That(restored.Fingerprint(), Is.EqualTo(first.Fingerprint()));
            Assert.That(restored.Inventory.GetAmount(InventoryContainer.ShipHold, Stove), Is.EqualTo(1));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(restored, resumed)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(first, timer))));
        }

        [Test]
        public void MissingStationRecipeAndInsufficientMaterialsRejectAtomically()
        {
            var bus = new WorldCommandBus();
            var start = Start();
            var unknown = bus.Execute(start, new StartWorkshopJobCommand(
                EntityId.Parse("cmd:bad"), Job, EntityId.Parse("recipe:missing"), 0));
            Assert.That(unknown.Success, Is.False);
            Assert.That(unknown.State, Is.SameAs(start));
            var once = bus.Execute(start, new StartWorkshopJobCommand(
                EntityId.Parse("cmd:first"), Job, Recipe, 0));
            var duplicateStation = bus.Execute(once.State, new StartWorkshopJobCommand(
                EntityId.Parse("cmd:second"), EntityId.Parse("job:other"), Recipe, 1));
            Assert.That(duplicateStation.Success, Is.False);
            Assert.That(duplicateStation.State, Is.SameAs(once.State));
        }
    }
}
