using System;
using System.Security.Cryptography;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewWorldIntegrationTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId Relief = EntityId.Parse("crew:relief");
        private static readonly EntityId Repairer = EntityId.Parse("crew:repair");
        private static readonly EntityId BoilerRoom = EntityId.Parse("room:boiler");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");

        private static WorldState Begin(long startingMinute = 1320)
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 100, 0, 1440),
                new CrewMember(Relief, DutyRole.Watch, 900, 400, 0, 1440),
                new CrewMember(Repairer, DutyRole.Repair, 800, 100, 0, 1440)
            }, new[] { new RepairJob(EntityId.Parse("repair:hull"), 6000) });
            var heat = ShipThermalState.Create(new[]
            {
                new ThermalCompartment(BoilerRoom, 1000, 20_000_000, 2000, 700, 1000)
            }, Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(BoilerRoom, 1, 10000));
            return WorldState.Create(new WorldMinute(startingMinute),
                new[] { new SupplyStock(Fuel, 480) }, new[] { Engineer, Relief, Repairer },
                thermal: heat, crewDuty: crew);
        }

        private static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> write)
        {
            return new SimulationScheduler(new WorldClock(read().Minute),
                new IWorldMinuteSystem[]
                {
                    new ShipCrewMinuteSystem(read, write),
                    new ShipThermalMinuteSystem(read, write, _ => -20000)
                }, seed: 42);
        }

        [Test]
        public void DispatchEngineerAshoreChangesBoilerOutputViaReplacementSkill()
        {
            var bus = new WorldCommandBus();
            var atHome = Begin();
            var ashore = bus.Execute(Begin(), new SetCrewAwayCommand(
                EntityId.Parse("cmd:shore"), Engineer, away: true, expectedRevision: 0));
            Assert.That(ashore.Success, Is.True);
            Assert.That(ashore.Events, Has.Count.EqualTo(1));
            Assert.That(ashore.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewDispatched));

            var shipHome = atHome;
            var shipAshore = ashore.State;
            Bind(() => shipHome, s => shipHome = s).Advance(30);
            Bind(() => shipAshore, s => shipAshore = s).Advance(30);

            Assert.That(shipHome.GetSupply(Fuel), Is.EqualTo(shipAshore.GetSupply(Fuel)));
            Assert.That(shipAshore.Thermal.TotalEnergy, Is.LessThan(shipHome.Thermal.TotalEnergy));
            Assert.That(shipAshore.CrewDuty.Get(Engineer).IsAway, Is.True);
            Assert.That(atHome.CrewDuty.Get(Engineer).IsAway, Is.False);
            Assert.That(ashore.State.Revision, Is.EqualTo(1));
        }

        [Test]
        public void InvalidOrDuplicateDispatchRejectsWithoutMutatingWorld()
        {
            var bus = new WorldCommandBus();
            var world = Begin();
            var dispatch = bus.Execute(world, new SetCrewAwayCommand(
                EntityId.Parse("cmd:one"), Engineer, true, 0));
            var repeated = bus.Execute(dispatch.State, new SetCrewAwayCommand(
                EntityId.Parse("cmd:two"), Engineer, true, 1));
            var duplicate = bus.Execute(dispatch.State, new SetCrewAwayCommand(
                EntityId.Parse("cmd:one"), Engineer, false, 1));
            var invalid = bus.Execute(world, new SetCrewAwayCommand(
                EntityId.Parse("cmd:unknown"), EntityId.Parse("crew:missing"), true, 0));
            Assert.That(repeated.Failure, Is.EqualTo(CommandFailure.CrewAvailabilityUnchanged));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(invalid.Failure, Is.EqualTo(CommandFailure.UnknownCrew));
            Assert.That(repeated.State, Is.SameAs(dispatch.State));
            Assert.That(invalid.State, Is.SameAs(world));
        }

        [Test]
        public void InterruptedSleepProgressesRestRepairsAndFuelOnlyToHazardMinute()
        {
            var world = Begin();
            var scheduler = Bind(() => world, s => world = s);
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                new WorldMinute(1635), "raid", interruptsSleep: true));
            var result = scheduler.AdvanceUntil(new WorldMinute(1800), new SleepInterruptionPolicy());

            Assert.That(result.WasInterrupted, Is.True);
            Assert.That(result.EndedAt.Value, Is.EqualTo(1635));
            Assert.That(world.Minute.Value, Is.EqualTo(1635));
            Assert.That(world.GetSupply(Fuel), Is.EqualTo(165));
            Assert.That(world.CrewDuty.GetRepair(EntityId.Parse("repair:hull")).RemainingWork,
                Is.LessThan(6000));
            Assert.That(world.CrewDuty.Get(Repairer).Fatigue, Is.GreaterThan(0));
            Assert.That(world.Revision, Is.Zero); // 315 ticks are not 315 commands
        }

        [Test]
        public void SaveAtMidnightAndResumeMatchesContinuousWorldFingerprintAndBytes()
        {
            var full = Begin();
            var continuous = Bind(() => full, x => full = x);
            continuous.Advance(480);

            var partial = Begin();
            var before = Bind(() => partial, x => partial = x);
            before.Advance(120);
            var loaded = SaveCodec.TryDecode(SaveCodec.Encode(SaveGameDto.Capture(partial, before)));
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            var restored = loaded.Value.State;
            var after = loaded.Value.CreateScheduler(new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(() => restored, x => restored = x),
                new ShipThermalMinuteSystem(() => restored, x => restored = x, _ => -20000)
            });
            after.Advance(360);

            Assert.That(restored.Fingerprint(), Is.EqualTo(full.Fingerprint()));
            Assert.That(restored.CrewDuty.Get(Engineer).Fatigue,
                Is.EqualTo(full.CrewDuty.Get(Engineer).Fatigue));
            Assert.That(restored.CrewDuty.GetRepair(EntityId.Parse("repair:hull")).RemainingWork,
                Is.EqualTo(full.CrewDuty.GetRepair(EntityId.Parse("repair:hull")).RemainingWork));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(restored, after)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(full, continuous))));
        }

        [Test]
        public void LegacyWorldWithoutAuthoredRosterStillBurnsPriorBoilerHeat()
        {
            var old = Begin();
            var legacy = WorldState.Create(old.Minute,
                new[] { new SupplyStock(Fuel, 480) }, Array.Empty<EntityId>(),
                thermal: old.Thermal);
            var initial = legacy.Thermal.TotalEnergy;
            var timer = new SimulationScheduler(new WorldClock(legacy.Minute),
                new IWorldMinuteSystem[] { new ShipThermalMinuteSystem(
                    () => legacy, s => legacy = s, _ => 20_000) });
            timer.Advance(1);

            Assert.That(legacy.GetSupply(Fuel), Is.EqualTo(479));
            Assert.That(legacy.Thermal.TotalEnergy - initial, Is.EqualTo(10000));
            Assert.That(legacy.CrewDuty.Members, Is.Empty);
        }
        [Test]
        public void V4SaveMigratesToEmptyCrewWithoutFabricatingDuty()
        {
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            var snapshot = SaveGameDto.Capture(world,
                new SimulationScheduler(new WorldClock(world.Minute),
                    Array.Empty<IWorldMinuteSystem>()));
            var current = SaveCodec.Encode(snapshot);
            int payloadLength = BitConverter.ToInt32(current, 10) - 61;
            var legacy = new byte[14 + payloadLength + 32];
            Array.Copy(current, legacy, 14 + payloadLength);
            legacy[8] = 4;
            Array.Copy(BitConverter.GetBytes(payloadLength), 0, legacy, 10, 4);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(legacy, 0, legacy.Length - 32);
                Array.Copy(hash, 0, legacy, legacy.Length - 32, 32);
            }
            var loaded = SaveCodec.TryDecode(legacy);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.CrewDuty.Members, Is.Empty);
            Assert.That(loaded.Value.State.CrewDuty.RepairJobs, Is.Empty);
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(world.Fingerprint()));
        }

    }
}
