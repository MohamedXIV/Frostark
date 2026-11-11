using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
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

        [Test]
        public void PendingMoveMustMatchSavedShipCheckpoint()
        {
            using (var session = Session())
            {
                session.AcceptShipCrewMove(A, C(4, 2));
                var snapshot = session.Capture();
                var move = snapshot.PendingCrewMoves.Single();
                var foreign = new PendingCrewMove(A,
                    EntityId.Parse("scene:order:foreign"), C(4, 2));

                Assert.Throws<ArgumentException>(() => SaveGameDto.Capture(
                    snapshot.State,
                    snapshot.CreateScheduler(Array.Empty<IWorldMinuteSystem>()),
                    snapshot.ProceduralOverrides, snapshot.SceneCheckpoint,
                    new[] { foreign }));
                Assert.Throws<ArgumentException>(() => SaveGameDto.Capture(
                    snapshot.State,
                    snapshot.CreateScheduler(Array.Empty<IWorldMinuteSystem>()),
                    snapshot.ProceduralOverrides, null, new[] { move }));
            }
        }

        [Test]
        public void OnboardCrewMoveSurvivesShoreExpeditionAndSaveWhileDepartingCrewMoveIsCleared()
        {
            using (var session = Session())
            {
                session.AcceptShipCrewMove(A, C(4, 2));
                session.AcceptShipCrewMove(B, C(2, 4));
                Assert.That(session.PendingCrewMoves.Count, Is.EqualTo(2));

                var shoreScene = EntityId.Parse("scene:order:shore");
                var site = EntityId.Parse("island:order:floe");
                var shoreAnchor = EntityId.Parse("anchor:order:shore");
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { B }, true,
                    EntityId.Parse("transition:order:depart")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    shoreScene, site, shoreAnchor, new[] { B }));

                Assert.That(session.ResidentShipSceneId, Is.EqualTo(Scene));
                Assert.That(session.GetPendingCrewMove(B), Is.Null,
                    "Departing crew member must not retain a shipboard move goal.");
                Assert.That(session.GetPendingCrewMove(A), Is.Not.Null,
                    "Onboard crew member retains their accepted shipboard move goal.");
                Assert.That(session.GetPendingCrewMove(A).ShipGoal, Is.EqualTo(C(4, 2)));

                // Onboard crew can also accept a new ship move while the shore party is away.
                session.AcceptShipCrewMove(A, C(5, 1));
                Assert.That(session.GetPendingCrewMove(A).ShipGoal, Is.EqualTo(C(5, 1)));
                Assert.Throws<InvalidOperationException>(() =>
                    session.AcceptShipCrewMove(B, C(3, 3)));

                var decoded = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
                Assert.That(decoded.Success, Is.True);
                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    Assert.That(restored.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                    Assert.That(restored.ResidentShipSceneId, Is.EqualTo(Scene));
                    Assert.That(restored.PendingCrewMoves.Count, Is.EqualTo(1));
                    Assert.That(restored.GetPendingCrewMove(A).ShipGoal, Is.EqualTo(C(5, 1)));
                }
            }
        }
    }
}
