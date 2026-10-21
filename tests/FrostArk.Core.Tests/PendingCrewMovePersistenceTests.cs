using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class PendingCrewMovePersistenceTests
    {
        private static readonly EntityId A = EntityId.Parse("crew:order:a");
        private static readonly EntityId B = EntityId.Parse("crew:order:b");
        private static readonly EntityId Room = EntityId.Parse("room:order:bridge");
        private static readonly EntityId Scene = EntityId.Parse("scene:order:ship");
        private static readonly EntityId Anchor = EntityId.Parse("anchor:order:start");
        private static ShipGridCell C(int x, int y) => new ShipGridCell(Room, x, y);

        private static WorldSession Session()
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(A, DutyRole.Engineering, 900, 0, 0, 1440),
                new CrewMember(B, DutyRole.Watch, 900, 0, 0, 1440)
            });
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Room, 6, 6)
            }, Array.Empty<ShipModuleBlueprint>(), Array.Empty<ShipModule>());
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { A, B },
                crewDuty: crew, layout: layout);
            var session = new WorldSession(world);
            session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(Scene, Anchor));
            return session;
        }

        [Test]
        public void AcceptedMoveSurvivesBinarySaveAndReentryWithoutMouseSelection()
        {
            using (var source = Session())
            {
                source.AcceptShipCrewMove(A, C(4, 2));
                source.AcceptShipCrewMove(B, C(2, 4));
                var save = source.Capture();
                Assert.That(save.PendingCrewMoves.Select(x => x.CrewId),
                    Is.EquivalentTo(new[] { A, B }));
                var decoded = SaveCodec.TryDecode(SaveCodec.Encode(save));
                Assert.That(decoded.Success, Is.True);
                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    Assert.That(restored.Revision, Is.Zero,
                        "A physical accepted Move is not a consumed item/world command.");
                    Assert.That(restored.PendingCrewMoves.Count, Is.EqualTo(2));
                    Assert.That(restored.SceneCheckpoint.SceneId, Is.EqualTo(Scene));
                    Assert.That(restored.GetPendingCrewMove(A).ShipGoal, Is.EqualTo(C(4, 2)));
                    Assert.That(restored.GetPendingCrewMove(B).ShipGoal, Is.EqualTo(C(2, 4)));
                    Assert.That(restored.CompleteCrewMove(A), Is.True);
                    Assert.That(restored.CompleteCrewMove(A), Is.False);
                    Assert.That(restored.GetPendingCrewMove(B), Is.Not.Null);
                    Assert.That(restored.Capture().PendingCrewMoves.Count, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void NewAcceptedMoveOverridesExistingGoalAndDifferentSceneClearsIt()
        {
            using (var session = Session())
            {
                session.AcceptShipCrewMove(A, C(4, 2));
                session.AcceptShipCrewMove(A, C(3, 1));
                Assert.That(session.PendingCrewMoves.Count, Is.EqualTo(1));
                Assert.That(session.GetPendingCrewMove(A).ShipGoal, Is.EqualTo(C(3, 1)));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:order:another"), Anchor));
                Assert.That(session.PendingCrewMoves, Is.Empty,
                    "Old-scene physical path must not execute in a different theater.");
            }
        }

        [Test]
        public void UnauthoredCrewAndRoomCannotBePersisted()
        {
            using (var session = Session())
            {
                Assert.Throws<InvalidOperationException>(() => session.AcceptShipCrewMove(
                    EntityId.Parse("crew:unknown"), C(2, 2)));
                Assert.Throws<InvalidOperationException>(() => session.AcceptShipCrewMove(
                    A, new ShipGridCell(EntityId.Parse("room:invalid"), 2, 2)));
                Assert.That(session.PendingCrewMoves, Is.Empty);
            }
        }
    }
}
