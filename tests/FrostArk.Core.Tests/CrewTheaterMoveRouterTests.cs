using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewTheaterMoveRouterTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("actor:engineer");
        private static readonly EntityId Scout = EntityId.Parse("actor:scout");
        private static readonly EntityId Guest = EntityId.Parse("actor:guest");
        private static readonly EntityId Unknown = EntityId.Parse("actor:unknown");
        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship");
        private static readonly EntityId ShoreScene = EntityId.Parse("scene:shore");
        private static readonly EntityId Site = EntityId.Parse("island:icy");
        private static readonly EntityId Bridge = EntityId.Parse("anchor:bridge");
        private static readonly EntityId Landing = EntityId.Parse("anchor:shore");

        private static WorldSession NewSession(bool injured = false)
        {
            var roster = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 850, 200, 0, 1440),
                new CrewMember(Scout, DutyRole.Watch, 800, 150, 0, 1440),
                new CrewMember(Guest, DutyRole.Cooking, 550, 100, 0, 1440,
                    wounds: injured ? 1000 : 0)
            });
            return new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer, Scout, Guest },
                crewDuty: roster));
        }

        private static TacticalOrderIntent Move(params EntityId[] ids) =>
            TacticalOrderIntent.MoveTo(ids, 2, 5);

        [Test]
        public void ShipRoutingRejectsUnknownAndAwayActorsWithoutTouchingWorld()
        {
            using (var session = NewSession())
            {
                var departure = ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transition:out"));
                Assert.That(departure.Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var before = session.Capture();
                var plan = CrewTheaterMoveRouter.Plan(before.State,
                    before.SceneCheckpoint, Move(Scout, Unknown, Engineer),
                    PhysicalTheater.Ship);
                Assert.That(plan.Select(x => x.ActorId),
                    Is.EqualTo(new[] { Engineer, Scout, Unknown }));
                Assert.That(plan.Select(x => x.Status), Is.EqualTo(new[]
                {
                    CrewTheaterRouteStatus.Ready,
                    CrewTheaterRouteStatus.WrongTheater,
                    CrewTheaterRouteStatus.UnknownCrew
                }));
                Assert.That(session.Revision, Is.EqualTo(before.State.Revision));
                Assert.That(session.Capture().State.Fingerprint(),
                    Is.EqualTo(before.State.Fingerprint()));
            }
        }

        [Test]
        public void ShoreRequiresBothAwayCrewAndMembershipInThisPhysicalParty()
        {
            using (var session = NewSession())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session,
                    new[] { Scout, Guest }, true, EntityId.Parse("transition:leave")).Success,
                    Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Site, Landing, new[] { Scout }));
                var snapshot = session.Capture();
                var plan = CrewTheaterMoveRouter.Plan(snapshot.State,
                    snapshot.SceneCheckpoint, Move(Engineer, Guest, Scout),
                    PhysicalTheater.Shore);
                Assert.That(plan.Select(x => x.Status), Is.EqualTo(new[]
                {
                    CrewTheaterRouteStatus.WrongTheater,
                    CrewTheaterRouteStatus.NotInActiveShoreParty,
                    CrewTheaterRouteStatus.Ready
                }));
            }
        }

        [Test]
        public void OtherLoadedTheaterDoesNotAuthorizeAnAwayActorToMove()
        {
            using (var session = NewSession())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transition:out")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var snapshot = session.Capture();
                var plan = CrewTheaterMoveRouter.Plan(snapshot.State, snapshot.SceneCheckpoint,
                    Move(Scout), PhysicalTheater.Shore);
                Assert.That(plan.Single().Status,
                    Is.EqualTo(CrewTheaterRouteStatus.InactiveTheater));
            }
        }

        [Test]
        public void UnknownLegacyLocationFailsClosedWithoutInventingShipOrShore()
        {
            using (var session = NewSession())
            {
                var snapshot = session.Capture();
                Assert.That(snapshot.SceneCheckpoint, Is.Null);
                var plan = CrewTheaterMoveRouter.Plan(snapshot.State, null,
                    Move(Engineer), PhysicalTheater.Ship);
                Assert.That(plan.Single().Status,
                    Is.EqualTo(CrewTheaterRouteStatus.MissingPhysicalCheckpoint));
            }
        }

        [Test]
        public void IncapacitatedCrewCannotReceivePhysicalMove()
        {
            using (var session = NewSession(injured: true))
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var snapshot = session.Capture();
                var plan = CrewTheaterMoveRouter.Plan(snapshot.State, snapshot.SceneCheckpoint,
                    Move(Guest), PhysicalTheater.Ship);
                Assert.That(plan.Single().Status,
                    Is.EqualTo(CrewTheaterRouteStatus.UnavailableCrew));
            }
        }

        [Test]
        public void RestoredCheckpointAndAwayStateProduceSameRoutingDecision()
        {
            byte[] serialized;
            using (var session = NewSession())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transition:depart")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Site, Landing, new[] { Scout }));
                serialized = SaveCodec.Encode(session.Capture());
            }
            var load = SaveCodec.TryDecode(serialized);
            Assert.That(load.Success, Is.True, load.Error.ToString());
            using (var resumed = WorldSession.Restore(load.Value))
            {
                var restored = resumed.Capture();
                var route = CrewTheaterMoveRouter.Plan(restored.State,
                    restored.SceneCheckpoint, Move(Scout, Engineer), PhysicalTheater.Shore);
                Assert.That(route[0].Status, Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));
                Assert.That(route[1].Status, Is.EqualTo(CrewTheaterRouteStatus.Ready));
            }
        }

        [Test]
        public void NonMoveOrdersAndInvalidTheaterDoNotBecomeFakeMovement()
        {
            using (var session = NewSession())
            {
                var snapshot = session.Capture();
                Assert.Throws<ArgumentException>(() => CrewTheaterMoveRouter.Plan(
                    snapshot.State, null,
                    TacticalOrderIntent.InteractWith(new[] { Engineer },
                        EntityId.Parse("object:galley")), PhysicalTheater.Ship));
                Assert.Throws<ArgumentOutOfRangeException>(() => CrewTheaterMoveRouter.Plan(
                    snapshot.State, null, Move(Engineer), (PhysicalTheater)99));
                Assert.Throws<ArgumentNullException>(() => CrewTheaterMoveRouter.Plan(
                    null, null, Move(Engineer), PhysicalTheater.Ship));
                Assert.That(session.Revision, Is.Zero);
            }
        }

        [Test]
        public void ConcurrentShipAndShorePresenceRoutesOnboardAndAwayActorsIndependently()
        {
            using (var session = NewSession())
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transition:concurrent-shore")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Site, Landing, new[] { Scout }));

                var snapshot = session.Capture();
                var shipRoute = CrewTheaterMoveRouter.Plan(snapshot.State,
                    snapshot.SceneCheckpoint, Move(Engineer, Scout), PhysicalTheater.Ship);
                Assert.That(shipRoute[0].ActorId, Is.EqualTo(Engineer));
                Assert.That(shipRoute[0].Status, Is.EqualTo(CrewTheaterRouteStatus.Ready));
                Assert.That(shipRoute[1].ActorId, Is.EqualTo(Scout));
                Assert.That(shipRoute[1].Status, Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));

                var shoreRoute = CrewTheaterMoveRouter.Plan(snapshot.State,
                    snapshot.SceneCheckpoint, Move(Engineer, Scout), PhysicalTheater.Shore);
                Assert.That(shoreRoute[0].ActorId, Is.EqualTo(Engineer));
                Assert.That(shoreRoute[0].Status, Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));
                Assert.That(shoreRoute[1].ActorId, Is.EqualTo(Scout));
                Assert.That(shoreRoute[1].Status, Is.EqualTo(CrewTheaterRouteStatus.Ready));
            }
        }
    }
}
