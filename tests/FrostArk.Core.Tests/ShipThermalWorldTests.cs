using System;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipThermalWorldTests
    {
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Cabin = EntityId.Parse("room:cabin");
        private static readonly EntityId BoilerRoom = EntityId.Parse("room:boiler");

        private static ShipThermalState MakeShip() =>
            ShipThermalState.Create(new[]
            {
                new ThermalCompartment(BoilerRoom, 1000, 20_000_000, 2000, 900, 1000),
                new ThermalCompartment(Cabin, 1000, 20_000_000, 7000, 400, 800)
            }, new[] { new ThermalConnection(BoilerRoom, Cabin, 200) },
                new BoilerConfiguration(BoilerRoom, 1, 3000));

        private static WorldState MakeWorld(long fuel = 400) =>
            WorldState.Create(new WorldMinute(1320),
                new[] { new SupplyStock(Fuel, fuel) }, Array.Empty<EntityId>(),
                thermal: MakeShip());

        private static SimulationScheduler Bind(Func<WorldState> get, Action<WorldState> commit)
        {
            var thermal = new ShipThermalMinuteSystem(get, commit, _ => -30_000);
            return new SimulationScheduler(new WorldClock(get().Minute),
                new IWorldMinuteSystem[] { thermal }, seed: 84);
        }

        [Test]
        public void OneHundredTwentyReferenceTicksAndOneBatchAreByteIdentical()
        {
            var a = MakeWorld();
            var b = MakeWorld();
            var steps = Bind(() => a, next => a = next);
            var batch = Bind(() => b, next => b = next);
            for (int i = 0; i < 120; i++) steps.Advance(1);
            batch.Advance(120);

            Assert.That(a.GetSupply(Fuel), Is.EqualTo(280));
            Assert.That(a.Fingerprint(), Is.EqualTo(b.Fingerprint()));
            Assert.That(a.Thermal.Get(Cabin).Energy,
                Is.EqualTo(b.Thermal.Get(Cabin).Energy));
            Assert.That(a.Revision, Is.EqualTo(120));
            Assert.That(a.Minute.Value, Is.EqualTo(1440));
        }

        [Test]
        public void SleepingToSixAmStopsOnRaidAt0315AndBurnsOnlyElapsedFuel()
        {
            var state = MakeWorld();
            var scheduler = Bind(() => state, next => state = next);
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                new WorldMinute(1635), "raiders", interruptsSleep: true));
            var result = scheduler.AdvanceUntil(new WorldMinute(1800),
                new SleepInterruptionPolicy());

            Assert.That(result.WasInterrupted, Is.True);
            Assert.That(result.EndedAt.Value, Is.EqualTo(1635));
            Assert.That(state.Minute, Is.EqualTo(result.EndedAt));
            Assert.That(state.GetSupply(Fuel), Is.EqualTo(85));
            Assert.That(state.Revision, Is.EqualTo(315));
            Assert.That(state.Thermal.Get(Cabin).ColdExposureMinutes, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void SaveHalfwayAndResumeProducesSameColdStreakFuelAndStateDigest()
        {
            var full = MakeWorld();
            var uninterrupted = Bind(() => full, next => full = next);
            uninterrupted.Advance(480);

            var partial = MakeWorld();
            var first = Bind(() => partial, next => partial = next);
            first.Advance(160);
            var blob = SaveCodec.Encode(SaveGameDto.Capture(partial, first));
            var loaded = SaveCodec.TryDecode(blob);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            var resumed = loaded.Value.State;
            var second = loaded.Value.CreateScheduler(new IWorldMinuteSystem[]
            {
                new ShipThermalMinuteSystem(() => resumed, next => resumed = next,
                    _ => -30_000)
            });
            second.Advance(320);

            Assert.That(resumed.Fingerprint(), Is.EqualTo(full.Fingerprint()));
            Assert.That(resumed.GetSupply(Fuel), Is.EqualTo(0));
            Assert.That(resumed.Thermal.Get(Cabin).Warning,
                Is.EqualTo(full.Thermal.Get(Cabin).Warning));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(resumed, second)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(full, uninterrupted))));
        }

        [Test]
        public void MissingFuelNeverProducesFreeBoilerEnergy()
        {
            var state = MakeWorld(0);
            var originalEnergy = state.Thermal.TotalEnergy;
            var scheduler = Bind(() => state, next => state = next);
            scheduler.Advance(1);

            Assert.That(state.GetSupply(Fuel), Is.Zero);
            Assert.That(state.Thermal.TotalEnergy, Is.LessThan(originalEnergy));
            Assert.That(state.Revision, Is.EqualTo(1));
        }
    }
}
