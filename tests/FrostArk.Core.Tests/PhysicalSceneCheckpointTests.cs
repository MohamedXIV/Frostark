using System;
using System.Linq;
using System.Security.Cryptography;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class PhysicalSceneCheckpointTests
    {
        private static readonly EntityId Hero = EntityId.Parse("actor:hero");
        private static readonly EntityId Scout = EntityId.Parse("actor:scout");
        private static readonly EntityId ShoreScene = EntityId.Parse("scene:ice-landing");
        private static readonly EntityId Island = EntityId.Parse("island:13");
        private static readonly EntityId ShoreAnchor = EntityId.Parse("anchor:landing-south");
        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship-interior");
        private static readonly EntityId ShipAnchor = EntityId.Parse("anchor:bridge");

        private static WorldSession Begin()
        {
            var duty = CrewState.Create(new[]
            {
                new CrewMember(Hero, DutyRole.Watch, 900, 300, 0, 1440),
                new CrewMember(Scout, DutyRole.Repair, 850, 200, 0, 1440)
            });
            return new WorldSession(WorldState.Create(new WorldMinute(90),
                Array.Empty<SupplyStock>(), new[] { Hero, Scout }, crewDuty: duty), seed: 123);
        }

        [Test]
        public void ShoreCheckpointSurvivesSaveRestoreAndCanonicalizesPartyOrder()
        {
            using (var session = Begin())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Hero, Scout }, true,
                    EntityId.Parse("transition:depart")).Success, Is.True);
                var party = new[] { Scout, Hero };
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Island, ShoreAnchor, party));
                party[0] = Hero;

                var save = SaveCodec.Encode(session.Capture());
                var decoded = SaveCodec.TryDecode(save);
                Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
                Assert.That(decoded.Value.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(decoded.Value.SceneCheckpoint.SceneId, Is.EqualTo(ShoreScene));
                Assert.That(decoded.Value.SceneCheckpoint.SiteId, Is.EqualTo(Island));
                Assert.That(decoded.Value.SceneCheckpoint.SpawnAnchorId, Is.EqualTo(ShoreAnchor));
                Assert.That(decoded.Value.SceneCheckpoint.PartyIds, Is.EqualTo(new[] { Hero, Scout }));
                Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(save));

                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    Assert.That(restored.SceneCheckpoint.PartyIds, Is.EqualTo(new[] { Hero, Scout }));
                    Assert.That(restored.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                    Assert.That(restored.Capture().State.HasCrew(Scout), Is.True);
                    restored.Advance(2);
                    Assert.That(restored.Capture().SceneCheckpoint.SpawnAnchorId, Is.EqualTo(ShoreAnchor));
                }
            }
        }

        [Test]
        public void ShipCheckpointCanReplaceShoreAfterCrewReturns()
        {
            using (var session = Begin())
            {
                ShoreCrewTransfer.Transfer(session, new[] { Hero }, true,
                    EntityId.Parse("transition:out"));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Island, ShoreAnchor, new[] { Hero }));

                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Hero }, false,
                    EntityId.Parse("transition:back")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, ShipAnchor));

                var restored = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
                Assert.That(restored.Success, Is.True, restored.Error.ToString());
                Assert.That(restored.Value.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(restored.Value.SceneCheckpoint.SiteId.IsValid, Is.False);
                Assert.That(restored.Value.SceneCheckpoint.PartyIds, Is.Empty);
                Assert.That(restored.Value.State.CrewDuty.Get(Hero).IsAway, Is.False);
            }
        }

        [Test]
        public void ShoreCheckpointRejectsUnknownOrOnboardActorsWithoutMutatingWorld()
        {
            using (var session = Begin())
            {
                var before = session.Capture().State.Fingerprint();
                Assert.Throws<ArgumentException>(() => session.SetSceneCheckpoint(
                    PhysicalSceneCheckpoint.Shore(ShoreScene, Island, ShoreAnchor,
                        new[] { Hero })), "Onboard actors must not be recorded as ashore.");
                Assert.Throws<ArgumentException>(() => PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Island, ShoreAnchor, new[] { Hero, Hero }));
                Assert.Throws<ArgumentException>(() => PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Island, ShoreAnchor, Array.Empty<EntityId>()));
                Assert.Throws<ArgumentException>(() => PhysicalSceneCheckpoint.Ship(
                    default(EntityId), ShipAnchor));
                Assert.That(session.SceneCheckpoint, Is.Null);
                Assert.That(session.Capture().State.Fingerprint(), Is.EqualTo(before));
            }
        }

        [Test]
        public void LegacyV12SavesHaveUnspecifiedPhysicalLocationNotInventedShipScene()
        {
            using (var session = Begin())
            {
                var current = SaveCodec.Encode(session.Capture());
                Assert.That(BitConverter.ToUInt16(current, 8), Is.EqualTo(13));
                // Version 13 adds precisely one 0-byte marker when no physical
                // checkpoint exists. A genuine v12 ends immediately before it.
                int length = BitConverter.ToInt32(current, 10) - 1;
                var v12 = new byte[14 + length + 32];
                Array.Copy(current, v12, 14 + length);
                v12[8] = 12;
                v12[9] = 0;
                Array.Copy(BitConverter.GetBytes(length), 0, v12, 10, 4);
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(v12, 0, v12.Length - 32);
                    Array.Copy(hash, 0, v12, v12.Length - 32, 32);
                }

                var migrated = SaveCodec.TryDecode(v12);
                Assert.That(migrated.Success, Is.True, migrated.Error.ToString());
                Assert.That(migrated.Value.SceneCheckpoint, Is.Null);
                Assert.That(migrated.Value.State.Fingerprint(),
                    Is.EqualTo(session.Capture().State.Fingerprint()));
                Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(migrated.Value)).Success, Is.True);
            }
        }
    }
}
