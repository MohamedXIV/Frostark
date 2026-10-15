using System;
using System.Linq;
using System.Security.Cryptography;
using FrostArk.Core.Commands;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class NavigationSaveTests
    {
        private static readonly EntityId A = EntityId.Parse("port:start");
        private static readonly EntityId B = EntityId.Parse("port:finish");
        private static readonly EntityId Leg = EntityId.Parse("sea:leg");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");

        static WorldState Start()
        {
            var nav = ShipNavigationState.Create(
                new[] { new SeaWaypoint(A, 0, 0), new SeaWaypoint(B, 100, 0) },
                new[] { new SeaLeg(Leg, A, B, 100, 500, 10, 800) },
                A, new NavigationTuning(10, 1, 300, 0, 200, 5, 3), 1000);
            var world = WorldState.Create(WorldMinute.Zero, new[] { new SupplyStock(Fuel, 500) },
                Array.Empty<EntityId>(), navigation: nav);
            return new WorldCommandBus().Execute(world, new PlanShipRouteCommand(
                EntityId.Parse("cmd:plan"), new[] { Leg }, world.Revision)).State;
        }

        static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> write) =>
            new SimulationScheduler(new WorldClock(read().Minute), new IWorldMinuteSystem[]
            {
                new ShipThermalMinuteSystem(read, write, _ => 20_000),
                new ShipTravelMinuteSystem(read, write)
            }, seed: 37);

        static void Rehash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32),
                    0, bytes, bytes.Length - 32, 32);
        }

        [Test]
        public void ActiveVoyageRoundTripByteIdentical()
        {
            var state = Start();
            var scheduler = Bind(() => state, s => state = s);
            scheduler.Advance(3);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler));
            Assert.That(bytes[8], Is.EqualTo(14));
            var loaded = SaveCodec.TryDecode(bytes);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(state.Fingerprint()));
            Assert.That(loaded.Value.State.Navigation.ProgressUnits,
                Is.EqualTo(state.Navigation.ProgressUnits));
            Assert.That(SaveCodec.Encode(loaded.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void InterruptedSaveResumeMatchesContinuousWorld()
        {
            var entire = Start();
            var whole = Bind(() => entire, s => entire = s);
            whole.Advance(14);

            var partial = Start();
            var first = Bind(() => partial, s => partial = s);
            first.Advance(4);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(partial, first));
            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            var resumed = decoded.Value.State;
            var systems = new IWorldMinuteSystem[]
            {
                new ShipThermalMinuteSystem(() => resumed, s => resumed = s, _ => 20_000),
                new ShipTravelMinuteSystem(() => resumed, s => resumed = s)
            };
            var second = decoded.Value.CreateScheduler(systems);
            second.Advance(10);
            Assert.That(resumed.Fingerprint(), Is.EqualTo(entire.Fingerprint()));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(resumed, second)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(entire, whole))));
        }

        [Test]
        public void V6SaveLoadsWithNoInventedNavigation()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            var now = SaveCodec.Encode(SaveGameDto.Capture(state, new SimulationScheduler(
                new WorldClock(state.Minute), Array.Empty<IWorldMinuteSystem>())));
            Assert.That(now[now.Length - 78], Is.Zero); // Empty navigation presence.
            var old = new byte[now.Length - 46];
            Array.Copy(now, old, old.Length - 32);
            old[8] = 6;
            Array.Copy(BitConverter.GetBytes(old.Length - 46), 0, old, 10, 4);
            Rehash(old);
            var result = SaveCodec.TryDecode(old);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(result.Value.State.Navigation.IsActive, Is.False);
            Assert.That(result.Value.State.Navigation.CurrentWaypoint.IsValid, Is.False);
            Assert.That(result.Value.State.Navigation.HullIntegrityPermille, Is.Zero);
            Assert.That(result.Value.State.Fingerprint(), Is.EqualTo(state.Fingerprint()));
        }

        [Test]
        public void ChecksummedInvalidNavigationRejected()
        {
            var state = Start();
            var timer = Bind(() => state, s => state = s);
            timer.Advance(1);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, timer));
            // Last Int32 in active v7 payload is progress on the current leg.
            Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, bytes,
                bytes.Length - 32 - 49, 4);
            Rehash(bytes);
            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void BadLengthAndTruncationReject()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, new SimulationScheduler(
                new WorldClock(state.Minute), Array.Empty<IWorldMinuteSystem>())));
            Assert.That(SaveCodec.TryDecode(bytes.Take(bytes.Length - 3).ToArray()).Success,
                Is.False);
            bytes[bytes.Length - 37] = 2; // Invalid v13 physical-checkpoint presence flag.
            Rehash(bytes);
            Assert.That(SaveCodec.TryDecode(bytes).Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }
    }
}
