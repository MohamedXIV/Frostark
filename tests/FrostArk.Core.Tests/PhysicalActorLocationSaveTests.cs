using System;
using System.Linq;
using System.Security.Cryptography;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class PhysicalActorLocationSaveTests
    {
        private static readonly EntityId Captain = EntityId.Parse("crew:captain");
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship");
        private static readonly EntityId ShipAnchor = EntityId.Parse("anchor:ship");
        private static readonly EntityId ShoreScene = EntityId.Parse("scene:shore");
        private static readonly EntityId ShoreSite = EntityId.Parse("site:shore");
        private static readonly EntityId ShoreAnchor = EntityId.Parse("anchor:shore");
        private static readonly EntityId EngineRoom = EntityId.Parse("room:engine");

        [Test]
        public void ActorLocationsRoundTripAndReencodeDeterministically()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Captain, Engineer },
                crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(Captain, DutyRole.Watch, 900, 200,
                        0, 1440, isAway: true),
                    new CrewMember(Engineer, DutyRole.Repair, 900, 0, 0, 1440)
                }));
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 7);
            var snapshot = SaveGameDto.Capture(state, scheduler,
                sceneCheckpoint: PhysicalSceneCheckpoint.Shore(
                    ShoreScene, ShoreSite, ShoreAnchor, new[] { Captain }),
                actorLocations: new[]
                {
                    PhysicalActorLocation.Ashore(Captain, 24.5f, -3.25f),
                    PhysicalActorLocation.Aboard(Engineer,
                        new ShipGridCell(EngineRoom, 4, 2))
                });

            var bytes = SaveCodec.Encode(snapshot);
            Assert.That(BitConverter.ToUInt16(bytes, 8),
                Is.EqualTo(SaveCodec.CurrentVersion));
            var decoded = SaveCodec.TryDecode(bytes);

            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.ActorLocations.Count, Is.EqualTo(2));
            var captain = decoded.Value.ActorLocations.Single(x => x.CrewId == Captain);
            Assert.That(captain.Theater, Is.EqualTo(PhysicalTheater.Shore));
            Assert.That(captain.ShoreX, Is.EqualTo(24.5f));
            Assert.That(captain.ShoreZ, Is.EqualTo(-3.25f));
            var engineer = decoded.Value.ActorLocations.Single(x => x.CrewId == Engineer);
            Assert.That(engineer.Theater, Is.EqualTo(PhysicalTheater.Ship));
            Assert.That(engineer.ShipCell.RoomId, Is.EqualTo(EngineRoom));
            Assert.That(engineer.ShipCell.X, Is.EqualTo(4));
            Assert.That(engineer.ShipCell.Y, Is.EqualTo(2));
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void V18MoveOnlyPayloadMigratesToNoActorLocations()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer },
                crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(Engineer, DutyRole.Repair, 900, 0, 0, 1440)
                }),
                layout: ShipLayoutState.Create(
                    new[] { new ShipCompartmentSlot(EngineRoom, 6, 6) },
                    Array.Empty<ShipModuleBlueprint>(),
                    Array.Empty<ShipModule>()));
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 9);
            var current = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler,
                sceneCheckpoint: PhysicalSceneCheckpoint.Ship(ShipScene, ShipAnchor),
                crewMoves: new[]
                {
                    new PendingCrewMove(Engineer, ShipScene,
                        new ShipGridCell(EngineRoom, 3, 4))
                }));
            // V19 appends an empty actor-location count after the move list.
            var v18 = DowngradeMoveOnlyToV18(current);

            var decoded = SaveCodec.TryDecode(v18);

            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.PendingCrewMoves.Count, Is.EqualTo(1));
            Assert.That(decoded.Value.ActorLocations, Is.Empty);
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(current),
                "A v18 move-only payload must migrate to the same v19 bytes.");
        }

        [Test]
        public void ShoreLocationMustBelongToTheSavedParty()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Captain, Engineer },
                crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(Captain, DutyRole.Watch, 900, 200,
                        0, 1440, isAway: true),
                    new CrewMember(Engineer, DutyRole.Repair, 900, 0, 0, 1440)
                }));
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 3);

            Assert.Throws<ArgumentException>(() => SaveGameDto.Capture(
                state, scheduler,
                sceneCheckpoint: PhysicalSceneCheckpoint.Shore(
                    ShoreScene, ShoreSite, ShoreAnchor, new[] { Captain }),
                actorLocations: new[]
                {
                    PhysicalActorLocation.Ashore(Engineer, 20f, 3f)
                }));
        }

        [Test]
        public void RestoredSessionReportsItselfAsRestored()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 5);
            var snapshot = SaveGameDto.Capture(state, scheduler);

            using (var fresh = new WorldSession(state))
                Assert.That(fresh.WasRestored, Is.False);
            using (var restored = WorldSession.Restore(snapshot))
                Assert.That(restored.WasRestored, Is.True);
        }

        private static byte[] DowngradeMoveOnlyToV18(byte[] encoded)
        {
            const int header = 14;
            const int checksum = 32;
            // The writer always emits both trailer counts once a trailer
            // exists; the final four bytes are the empty location count.
            int payloadLength = BitConverter.ToInt32(encoded, 10) - 4;
            var legacy = new byte[header + payloadLength + checksum];
            Array.Copy(encoded, legacy, header + payloadLength);
            legacy[8] = 18;
            legacy[9] = 0;
            Array.Copy(BitConverter.GetBytes(payloadLength), 0, legacy, 10, 4);
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(legacy, 0, legacy.Length - checksum);
                Array.Copy(hash, 0, legacy, legacy.Length - checksum, checksum);
            }
            return legacy;
        }
    }
}
