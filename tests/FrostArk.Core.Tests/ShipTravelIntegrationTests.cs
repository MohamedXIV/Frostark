using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipTravelIntegrationTests
    {
        private static readonly EntityId A = EntityId.Parse("port:a");
        private static readonly EntityId B = EntityId.Parse("port:b");
        private static readonly EntityId C = EntityId.Parse("port:c");
        private static readonly EntityId Ice = EntityId.Parse("route:ice");
        private static readonly EntityId Open1 = EntityId.Parse("route:open1");
        private static readonly EntityId Open2 = EntityId.Parse("route:open2");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Engine = EntityId.Parse("module:engine");

        private static WorldState Begin(bool ice, long fuel = 500,
            ShipLayoutState? layout = null, ShipThermalState? thermal = null)
        {
            var nav = ShipNavigationState.Create(
                new[] { new SeaWaypoint(A, 0, 0), new SeaWaypoint(B, 40, 0),
                        new SeaWaypoint(C, 30, 30) },
                new[] { new SeaLeg(Ice, A, B, 40, 900, 0, 700),
                        new SeaLeg(Open1, A, C, 30, 0, 0, 1000),
                        new SeaLeg(Open2, C, B, 40, 0, 0, 1000) },
                A, new NavigationTuning(10, 1, 700, 0, 400, 10, 0), 1000);
            var state = WorldState.Create(WorldMinute.Zero,
                new[] { new SupplyStock(Fuel, fuel) }, Array.Empty<EntityId>(),
                navigation: nav, layout: layout, thermal: thermal);
            var selected = ice ? new[] { Ice } : new[] { Open1, Open2 };
            var result = new WorldCommandBus().Execute(state,
                new PlanShipRouteCommand(EntityId.Parse("cmd:plan"), selected, state.Revision));
            Assert.That(result.Success, Is.True, result.Failure.ToString());
            return result.State;
        }

        private static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> write) =>
            new SimulationScheduler(new WorldClock(read().Minute), new IWorldMinuteSystem[]
            {
                new ShipThermalMinuteSystem(read, write, _ => 20_000),
                new ShipTravelMinuteSystem(read, write)
            }, seed: 777);

        [Test]
        public void ShortIceCutVsDetourDifferentTimeFuelAndHull()
        {
            var shortcut = Begin(true);
            var detour = Begin(false);
            var shortScheduler = Bind(() => shortcut, s => shortcut = s);
            var longScheduler = Bind(() => detour, s => detour = s);
            for (int i = 0; i < 100 && shortcut.Navigation.IsActive; i++)
                shortScheduler.Advance(1);
            for (int i = 0; i < 100 && detour.Navigation.IsActive; i++)
                longScheduler.Advance(1);
            Assert.That(shortcut.Navigation.CurrentWaypoint, Is.EqualTo(B));
            Assert.That(detour.Navigation.CurrentWaypoint, Is.EqualTo(B));
            Assert.That(shortcut.Minute.Value, Is.GreaterThan(detour.Minute.Value));
            Assert.That(shortcut.GetSupply(Fuel), Is.LessThan(detour.GetSupply(Fuel)));
            Assert.That(shortcut.Navigation.HullIntegrityPermille,
                Is.LessThan(detour.Navigation.HullIntegrityPermille));
        }

        [Test]
        public void TravelStopsIfHeatConsumedRequiredFuel()
        {
            var room = EntityId.Parse("room:boiler");
            var thermal = ShipThermalState.Create(
                new[] { new ThermalCompartment(room, 1000, 20_000_000, 0, 1000, 1000) },
                Array.Empty<ThermalConnection>(), new BoilerConfiguration(room, 1, 10000));
            var state = Begin(false, fuel: 1, thermal: thermal);
            Bind(() => state, s => state = s).Advance(1);
            Assert.That(state.GetSupply(Fuel), Is.Zero);
            Assert.That(state.Navigation.ProgressUnits, Is.Zero);
            Assert.That(state.Navigation.CurrentWaypoint, Is.EqualTo(A));
        }

        [Test]
        public void TinyEnginePowerNeverCreatesFreeTravel()
        {
            var room = EntityId.Parse("room:engine");
            var layout = ShipLayoutState.Create(
                new[] { new ShipCompartmentSlot(room, 2, 2) },
                new[] { new ShipModuleBlueprint(EntityId.Parse("blueprint:engine"),
                    ShipModuleKind.Engine, 2, 2, EntityId.Parse("item:parts"), 1, 1) },
                new[] { new ShipModule(Engine, EntityId.Parse("blueprint:engine"), room, 0, 0, 1) });
            var state = Begin(false, layout: layout);
            Bind(() => state, s => state = s).Advance(20);
            Assert.That(state.Navigation.ProgressUnits, Is.Zero);
            Assert.That(state.Navigation.CurrentWaypoint, Is.EqualTo(A));
            Assert.That(state.GetSupply(Fuel), Is.EqualTo(500));
        }

        [Test]
        public void TenOneMinuteStepsMatchSingleTenMinuteAdvance()
        {
            var one = Begin(true);
            var ten = Begin(true);
            var first = Bind(() => one, s => one = s);
            var batch = Bind(() => ten, s => ten = s);
            for (int i = 0; i < 10; i++) first.Advance(1);
            batch.Advance(10);
            Assert.That(one.Fingerprint(), Is.EqualTo(ten.Fingerprint()));
            Assert.That(one.GetSupply(Fuel), Is.EqualTo(ten.GetSupply(Fuel)));
        }

        [Test]
        public void SleepInterruptionStopsTravelAtHazardMinute()
        {
            var state = Begin(false);
            var scheduler = Bind(() => state, s => state = s);
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:ice-alert"),
                new WorldMinute(5), "ice-warning", interruptsSleep: true));
            var result = scheduler.AdvanceUntil(new WorldMinute(20), new SleepInterruptionPolicy());
            Assert.That(result.WasInterrupted, Is.True);
            Assert.That(result.EndedAt.Value, Is.EqualTo(5));
            Assert.That(state.Minute.Value, Is.EqualTo(5));
        }

        [Test]
        public void TravelCannotAdvanceWorldClock()
        {
            var state = Begin(true);
            var travel = new ShipTravelMinuteSystem(() => state, s => state = s);
            Assert.Throws<InvalidOperationException>(() => travel.Tick(new WorldMinute(1)));
            Assert.That(state.Minute.Value, Is.Zero);
        }
    }
}
