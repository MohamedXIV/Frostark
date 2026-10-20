using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    public sealed class CrewTheaterMoveDispatchEditModeTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("actor:engineer");
        private static readonly EntityId Scout = EntityId.Parse("actor:scout");
        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship");
        private static readonly EntityId ShoreScene = EntityId.Parse("scene:shore");
        private static readonly EntityId Bridge = EntityId.Parse("anchor:bridge");
        private static readonly EntityId Landing = EntityId.Parse("anchor:landing");
        private static readonly EntityId Island = EntityId.Parse("island:one");

        private sealed class FakeExecutor : ICrewTheaterMoveExecutor
        {
            public EntityId ActorId { get; set; }
            public PhysicalTheater Theater { get; set; }
            public bool Allow { get; set; } = true;
            public int Calls { get; private set; }
            public CrewMoveGoal LastDestination { get; private set; }
            public bool TryMove(CrewMoveGoal goal)
            {
                Calls++;
                LastDestination = goal;
                return Allow;
            }
        }

        private static WorldSession Begin()
        {
            var members = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 800, 100, 0, 1440),
                new CrewMember(Scout, DutyRole.Watch, 750, 200, 0, 1440)
            });
            return new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer, Scout },
                crewDuty: members));
        }

        [Test]
        public void OnboardCrewReceivesOnlyShipLocalCellAndAwayCrewIsRejected()
        {
            using (var session = Begin())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transfer:out")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var ship = new FakeExecutor { ActorId = Engineer, Theater = PhysicalTheater.Ship };
                var away = new FakeExecutor { ActorId = Scout, Theater = PhysicalTheater.Ship };
                var world = CrewMoveGoal.ShipCellGoal(new Vector2Int(7, 4));
                var intent = TacticalOrderIntent.MoveTo(new[] { Scout, Engineer }, 20, 20);
                var beforeRevision = session.Revision;
                var results = CrewTheaterMoveDispatcher.Dispatch(session, intent, world,
                    new ICrewTheaterMoveExecutor[] { ship, away });

                Assert.That(results.Select(x => x.ActorId),
                    Is.EqualTo(new[] { Engineer, Scout }));
                Assert.That(results[0].Status, Is.EqualTo(CrewMoveDispatchStatus.Accepted));
                Assert.That(results[1].RouteStatus,
                    Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));
                Assert.That(results[1].Status, Is.EqualTo(CrewMoveDispatchStatus.RouteRejected));
                Assert.That(ship.Calls, Is.EqualTo(1));
                Assert.That(ship.LastDestination.Theater, Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(ship.LastDestination.ShipCell, Is.EqualTo(new Vector2Int(7, 4)));
                Assert.That(away.Calls, Is.Zero);
                Assert.That(session.Revision, Is.EqualTo(beforeRevision));
            }
        }

        [Test]
        public void ShoreSceneDispatchesOnlyAuthoritativePartyThroughWorldGoal()
        {
            using (var session = Begin())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Scout }, true,
                    EntityId.Parse("transfer:shore")).Success, Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    ShoreScene, Island, Landing, new[] { Scout }));
                var ship = new FakeExecutor { ActorId = Engineer, Theater = PhysicalTheater.Ship };
                var shore = new FakeExecutor { ActorId = Scout, Theater = PhysicalTheater.Shore };
                var point = new Vector3(4f, 0f, 5f);
                var results = CrewTheaterMoveDispatcher.Dispatch(session,
                    TacticalOrderIntent.MoveTo(new[] { Scout, Engineer }, 4, 5),
                    CrewMoveGoal.ShorePoint(point),
                    new ICrewTheaterMoveExecutor[] { ship, shore });
                Assert.That(results[0].RouteStatus,
                    Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));
                Assert.That(results[1].Status, Is.EqualTo(CrewMoveDispatchStatus.Accepted));
                Assert.That(shore.LastDestination.ShoreWorld, Is.EqualTo(point));
                Assert.That(ship.Calls, Is.Zero);
            }
        }

        [Test]
        public void DuplicateOrMissingSceneProxyNeverMakesAFalseAcceptedResult()
        {
            using (var session = Begin())
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var a = new FakeExecutor { ActorId = Engineer, Theater = PhysicalTheater.Ship };
                var b = new FakeExecutor { ActorId = Engineer, Theater = PhysicalTheater.Ship };
                var intent = TacticalOrderIntent.MoveTo(new[] { Engineer, Scout }, 3, 4);
                var results = CrewTheaterMoveDispatcher.Dispatch(session, intent,
                    CrewMoveGoal.ShipCellGoal(new Vector2Int(3, 4)),
                    new ICrewTheaterMoveExecutor[] { a, b });
                Assert.That(results[0].Status,
                    Is.EqualTo(CrewMoveDispatchStatus.DuplicateSceneExecutor));
                Assert.That(results[1].Status,
                    Is.EqualTo(CrewMoveDispatchStatus.NoSceneExecutor));
                Assert.That(a.Calls + b.Calls, Is.Zero);
            }
        }

        [Test]
        public void UnreachableDestinationPreservesPerActorRejection()
        {
            using (var session = Begin())
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Bridge));
                var unreachable = new FakeExecutor
                {
                    ActorId = Engineer, Theater = PhysicalTheater.Ship, Allow = false
                };
                var result = CrewTheaterMoveDispatcher.Dispatch(session,
                    TacticalOrderIntent.MoveTo(new[] { Engineer }, 4, 2),
                    CrewMoveGoal.ShipCellGoal(new Vector2Int(4, 2)),
                    new ICrewTheaterMoveExecutor[] { unreachable });
                Assert.That(result.Single().RouteStatus,
                    Is.EqualTo(CrewTheaterRouteStatus.Ready));
                Assert.That(result.Single().Status,
                    Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
                Assert.That(unreachable.Calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void LegacyUnknownPhysicalSceneRefusesToInvokeEitherExecutor()
        {
            using (var session = Begin())
            {
                var shore = new FakeExecutor { ActorId = Engineer, Theater = PhysicalTheater.Shore };
                var result = CrewTheaterMoveDispatcher.Dispatch(session,
                    TacticalOrderIntent.MoveTo(new[] { Engineer }, 2, 2),
                    CrewMoveGoal.ShorePoint(new Vector3(2, 0, 2)),
                    new ICrewTheaterMoveExecutor[] { shore });
                Assert.That(result.Single().RouteStatus,
                    Is.EqualTo(CrewTheaterRouteStatus.MissingPhysicalCheckpoint));
                Assert.That(shore.Calls, Is.Zero);
            }
        }

        [Test]
        public void NonFiniteShoreDestinationRejectedBeforeDispatch()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CrewMoveGoal.ShorePoint(new Vector3(float.PositiveInfinity, 0, 0)));
        }

        private sealed class RecordingActor : TacticalActorProxy
        {
            public Vector3 LastPoint;
            public override bool MoveTo(Vector3 point)
            {
                LastPoint = point;
                return true;
            }
        }

        [Test]
        public void ShoreExecutorUsesExistingNavMeshProxyContractWithoutShadowActor()
        {
            var obj = new GameObject("G2-MovementProxy");
            try
            {
                var actor = obj.AddComponent<RecordingActor>();
                actor.SetActorId(Scout);
                var bridge = new ShoreNavMeshCrewExecutor(actor);
                var point = new Vector3(1.5f, 0, -2f);
                Assert.That(bridge.ActorId, Is.EqualTo(Scout));
                Assert.That(bridge.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(bridge.TryMove(CrewMoveGoal.ShorePoint(point)), Is.True);
                Assert.That(actor.LastPoint, Is.EqualTo(point));
                Assert.That(bridge.TryMove(CrewMoveGoal.ShipCellGoal(new Vector2Int(1, 2))),
                    Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
    }
}
