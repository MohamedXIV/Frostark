using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Economy;
using FrostArk.Core.Inventory;
using FrostArk.Core.Ship;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorldSessionTests
    {
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Crew = EntityId.Parse("crew:01");

        private static WorldSession MakeSession() => new WorldSession(
            WorldState.Create(new WorldMinute(10),
                new[] { new SupplyStock(Fuel, 10) }, new[] { Crew }), seed: 23);

        [Test]
        public void NewSessionStartsWithOneCanonicalWorldAndClock()
        {
            using (var session = MakeSession())
            {
                Assert.That(session.Revision, Is.EqualTo(0));
                Assert.That(session.Minute.Value, Is.EqualTo(10));
                Assert.That(session.Read().GetSupply(Fuel), Is.EqualTo(10));
                Assert.That(session.Read().Minute, Is.EqualTo(session.Minute));
            }
        }

        [Test]
        public void CommandCommitsBeforeNotificationAndRejectionPublishesNothing()
        {
            using (var session = MakeSession())
            {
                var observed = new List<string>();
                session.Changed += change =>
                {
                    observed.Add(change.Kind + ":" + change.Revision + ":" +
                                 change.View.GetSupply(Fuel));
                    Assert.That(session.Read().GetSupply(Fuel),
                        Is.EqualTo(change.View.GetSupply(Fuel)));
                };
                var committed = session.Submit(new WithdrawSupplyCommand(
                    EntityId.Parse("command:1"), Fuel, 4, 0));
                var rejected = session.Submit(new WithdrawSupplyCommand(
                    EntityId.Parse("command:2"), Fuel, 100, 1));

                Assert.That(committed.Success, Is.True);
                Assert.That(rejected.Success, Is.False);
                Assert.That(session.Revision, Is.EqualTo(1));
                Assert.That(session.Read().GetSupply(Fuel), Is.EqualTo(6));
                Assert.That(observed, Is.EqualTo(new[] { "Command:1:6" }));
                Assert.That(committed.Events, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void TimeAdvanceUsesSameSchedulerAndUpdatesWorldMinuteWithoutChangingRevision()
        {
            using (var session = MakeSession())
            {
                var changes = new List<string>();
                session.Changed += change => changes.Add(change.Kind + ":" + change.View.Minute.Value);
                var first = session.Advance(5);
                var none = session.Advance(0);

                Assert.That(first.EndedAt.Value, Is.EqualTo(15));
                Assert.That(none.AdvancedMinutes, Is.Zero);
                Assert.That(session.Minute.Value, Is.EqualTo(15));
                Assert.That(session.Revision, Is.Zero);
                Assert.That(changes, Is.EqualTo(new[] { "Time:15" }));
            }
        }

        [Test]
        public void SaveAndRestorePreserveCommandsSeedAndPendingHazard()
        {
            using (var session = MakeSession())
            {
                session.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                    new WorldMinute(25), "raid", interruptsSleep: true));
                var command = new WithdrawSupplyCommand(EntityId.Parse("command:once"),
                    Fuel, 3, 0);
                Assert.That(session.Submit(command).Success, Is.True);
                session.Advance(4);
                var bytes = SaveCodec.Encode(session.Capture());
                var loaded = SaveCodec.TryDecode(bytes);
                Assert.That(loaded.Success, Is.True);

                using (var restored = WorldSession.Restore(loaded.Value))
                {
                    Assert.That(restored.Minute.Value, Is.EqualTo(14));
                    Assert.That(restored.Read().GetSupply(Fuel), Is.EqualTo(7));
                    Assert.That(restored.Submit(new WithdrawSupplyCommand(command.Id,
                        Fuel, 1, 1)).Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
                    var result = restored.AdvanceUntil(new WorldMinute(30),
                        new SleepInterruptionPolicy());
                    Assert.That(result.WasInterrupted, Is.True);
                    Assert.That(result.EndedAt.Value, Is.EqualTo(25));
                    Assert.That(restored.Minute, Is.EqualTo(result.EndedAt));
                }
            }
        }

        [Test]
        public void RestoredSessionPreservesProceduralOverridesOnSubsequentSave()
        {
            var initial = WorldState.Create(new WorldMinute(14),
                new[] { new SupplyStock(Fuel, 5) }, new[] { Crew });
            var scheduler = new SimulationScheduler(new WorldClock(initial.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 291);
            var snapshot = SaveGameDto.Capture(initial, scheduler,
                new[] { new ProceduralOverride(EntityId.Parse("site:wreck-17"), "salvaged") });

            using (var session = WorldSession.Restore(snapshot))
            {
                session.Advance(3);
                var again = session.Capture();
                var decoded = SaveCodec.TryDecode(SaveCodec.Encode(again));

                Assert.That(decoded.Success, Is.True);
                Assert.That(decoded.Value.ProceduralOverrides, Has.Count.EqualTo(1));
                Assert.That(decoded.Value.ProceduralOverrides[0].Id,
                    Is.EqualTo(EntityId.Parse("site:wreck-17")));
                Assert.That(decoded.Value.ProceduralOverrides[0].Value, Is.EqualTo("salvaged"));
                Assert.That(decoded.Value.State.Minute.Value, Is.EqualTo(17));
            }
        }

        [Test]
        public void SessionThermalAdvanceConsumesRealFuelAndReplaysAfterRestore()
        {
            var boiler = EntityId.Parse("room:boiler");
            var cabin = EntityId.Parse("room:cabin");
            var thermal = FrostArk.Core.Ship.ShipThermalState.Create(new[]
            {
                new FrostArk.Core.Ship.ThermalCompartment(boiler, 1000, 20_000_000, 2000, 900, 1000),
                new FrostArk.Core.Ship.ThermalCompartment(cabin, 1000, 20_000_000, 7000, 400, 800)
            }, new[] { new FrostArk.Core.Ship.ThermalConnection(boiler, cabin, 200) },
                new FrostArk.Core.Ship.BoilerConfiguration(boiler, 1, 3000));
            var world = WorldState.Create(new WorldMinute(1320),
                new[] { new SupplyStock(Fuel, 400) }, Array.Empty<EntityId>(),
                thermal: thermal);

            byte[] halfway;
            using (var session = new WorldSession(world, ambientMilliC: _ => -30_000))
            {
                session.Advance(5);
                Assert.That(session.Capture().State.GetSupply(Fuel), Is.EqualTo(395),
                    "Time must not advance without burning real canonical ship fuel.");
                halfway = SaveCodec.Encode(session.Capture());
            }

            var decoded = SaveCodec.TryDecode(halfway);
            Assert.That(decoded.Success, Is.True);
            using (var resumed = WorldSession.Restore(decoded.Value,
                ambientMilliC: _ => -30_000))
            using (var continuous = new WorldSession(world, ambientMilliC: _ => -30_000))
            {
                resumed.Advance(5);
                continuous.Advance(10);
                Assert.That(resumed.Capture().State.Fingerprint(),
                    Is.EqualTo(continuous.Capture().State.Fingerprint()));
                Assert.That(resumed.Capture().State.GetSupply(Fuel), Is.EqualTo(390));
            }
        }


        [Test]
        public void SessionWorkshopAfterSleepInterruptionAndRestoreDeliversPaidOutputOnlyOnce()
        {
            var scrap = EntityId.Parse("item:scrap");
            var stove = EntityId.Parse("item:stove");
            var pickup = EntityId.Parse("pickup:shore");
            var recipeId = EntityId.Parse("recipe:stove");
            var station = EntityId.Parse("station:bench");
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(scrap, 1, 100, 10),
                new ItemDefinition(stove, 1, 2000, 1)
            }), new[] { new SalvagePickup(pickup, scrap, 8) },
                20000, 10, 20000, 4);
            inventory = inventory.TryCollect(pickup).State.TryUnload(scrap, 8).State;
            var recipe = new WorkshopRecipe(recipeId, 1, station, 60,
                new[] { new RecipeStack(scrap, 3) },
                new[] { new RecipeStack(stove, 1) });
            var economy = EconomyState.Create(new WorkshopCatalog(new[] { recipe }),
                new[] { station });
            var initial = WorldState.Create(new WorldMinute(120),
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: inventory, economy: economy);
            var jobId = EntityId.Parse("job:stove-01");
            var commandId = EntityId.Parse("cmd:craft");

            using (var session = new WorldSession(initial, seed: 42))
            {
                var accepted = session.Submit(new StartWorkshopJobCommand(
                    commandId, jobId, recipeId, session.Revision));
                Assert.That(accepted.Success, Is.True);
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, scrap), Is.EqualTo(5),
                    "Materials must be paid when the job starts, not again on resume.");

                session.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                    new WorldMinute(160), "raiders", interruptsSleep: true));
                var interrupted = session.AdvanceUntil(new WorldMinute(220),
                    new SleepInterruptionPolicy());
                Assert.That(interrupted.WasInterrupted, Is.True);
                Assert.That(session.Minute.Value, Is.EqualTo(160));
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, stove), Is.Zero);

                var bytes = SaveCodec.Encode(session.Capture());
                var loaded = SaveCodec.TryDecode(bytes);
                Assert.That(loaded.Success, Is.True, loaded.Error.ToString());

                using (var restored = WorldSession.Restore(loaded.Value))
                {
                    var repeated = restored.Submit(new StartWorkshopJobCommand(
                        commandId, jobId, recipeId, restored.Revision));
                    Assert.That(repeated.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
                    restored.Advance(20);
                    session.Advance(20);
                    var finished = restored.Capture().State;
                    Assert.That(finished.Economy.Jobs, Is.Empty);
                    Assert.That(finished.Inventory.GetAmount(
                        InventoryContainer.ShipHold, stove), Is.EqualTo(1));
                    Assert.That(finished.Inventory.GetAmount(
                        InventoryContainer.ShipHold, scrap), Is.EqualTo(5));
                    Assert.That(SaveCodec.Encode(restored.Capture()),
                        Is.EqualTo(SaveCodec.Encode(session.Capture())),
                        "Interrupted resume must match uninterrupted session bytes.");
                    restored.Advance(10);
                    Assert.That(restored.Capture().State.Inventory.GetAmount(
                        InventoryContainer.ShipHold, stove), Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void SessionHeatRunsBeforeShipTravelAndPreventsMovementWithoutFuel()
        {
            var boiler = EntityId.Parse("room:boiler");
            var portA = EntityId.Parse("port:a");
            var portB = EntityId.Parse("port:b");
            var leg = EntityId.Parse("route:open");
            var thermal = ShipThermalState.Create(
                new[] { new ThermalCompartment(boiler, 1000, 20_000_000, 0, 1000, 1000) },
                Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(boiler, 1, 10000));
            var navigation = ShipNavigationState.Create(
                new[] { new SeaWaypoint(portA, 0, 0), new SeaWaypoint(portB, 40, 0) },
                new[] { new SeaLeg(leg, portA, portB, 40, 0, 0, 1000) },
                portA, new NavigationTuning(10, 1, 700, 0, 400, 10, 0), 1000);
            var world = WorldState.Create(WorldMinute.Zero,
                new[] { new SupplyStock(Fuel, 1) }, Array.Empty<EntityId>(),
                thermal: thermal, navigation: navigation);

            Assert.Throws<ArgumentException>(() => new WorldSession(world),
                "A real thermal ship must not silently assume zero-degree weather.");
            using (var session = new WorldSession(world, ambientMilliC: _ => 20_000))
            {
                Assert.That(session.Submit(new PlanShipRouteCommand(
                    EntityId.Parse("cmd:route"), new[] { leg }, session.Revision)).Success,
                    Is.True);
                session.Advance(1);
                Assert.That(session.Minute.Value, Is.EqualTo(1));
                Assert.That(session.Capture().State.GetSupply(Fuel), Is.Zero);
                Assert.That(session.Capture().State.Navigation.ProgressUnits, Is.Zero);
                Assert.That(session.Capture().State.Navigation.CurrentWaypoint, Is.EqualTo(portA));
                var loaded = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
                Assert.That(loaded.Success, Is.True);
                Assert.Throws<ArgumentException>(() => WorldSession.Restore(loaded.Value),
                    "Restoring an authored thermal ship needs a weather dependency too.");
                using (var resumed = WorldSession.Restore(loaded.Value,
                    ambientMilliC: _ => 20_000))
                {
                    resumed.Advance(5);
                    Assert.That(resumed.Capture().State.Navigation.ProgressUnits, Is.Zero);
                    Assert.That(resumed.Capture().State.GetSupply(Fuel), Is.Zero);
                }
            }
        }

        [Test]
        public void DisposeRejectsUseAndReleasesSubscriptions()
        {
            var session = MakeSession();
            session.Dispose();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.Read());
            Assert.Throws<ObjectDisposedException>(() => session.Advance(1));
            Assert.Throws<ObjectDisposedException>(() => session.Submit(
                new WithdrawSupplyCommand(EntityId.Parse("cmd:late"), Fuel, 1, 0)));
        }
    }
}
