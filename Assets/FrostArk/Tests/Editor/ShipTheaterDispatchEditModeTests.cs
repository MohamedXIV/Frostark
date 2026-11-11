using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Real Unity GameObject + production ShipGridWalker routing exercises.
    /// G2 requires Unity Editor; standalone Core CI does not run these tests.
    /// </summary>
    public sealed class ShipTheaterDispatchEditModeTests
    {
        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship-interior");
        private static readonly EntityId Spawn = EntityId.Parse("anchor:bridge");
        private static readonly EntityId A = EntityId.Parse("crew:one");
        private static readonly EntityId B = EntityId.Parse("crew:two");
        private static readonly EntityId Bridge = EntityId.Parse("room:bridge");
        private static readonly EntityId Engine = EntityId.Parse("room:engine");

        private static ShipGridCell Cell(EntityId room, int x, int y) =>
            new ShipGridCell(room, x, y);

        private static WorldSession NewSession()
        {
            var duty = CrewState.Create(new[]
            {
                new CrewMember(A, DutyRole.Engineering, 800, 200, 0, 1440),
                new CrewMember(B, DutyRole.Watch, 780, 200, 0, 1440)
            });
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { A, B }, crewDuty: duty);
            var session = new WorldSession(world);
            session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(ShipScene, Spawn));
            return session;
        }

        private static ShipSpatialMap Map(bool open)
        {
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Bridge, 6, 6),
                new ShipCompartmentSlot(Engine, 6, 6)
            }, Array.Empty<ShipModuleBlueprint>(), Array.Empty<ShipModule>());
            return ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(),
                new[] { new ShipRoomPortal(EntityId.Parse("portal:bridge-engine"),
                    Cell(Bridge, 5, 2), Cell(Engine, 0, 2), open) });
        }

        private static ShipGridWalker Walker(GameObject root, string name,
            ShipSpatialMap map, ShipGridCell start)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var origins = new System.Collections.Generic.Dictionary<EntityId, Vector3>
            {
                [Bridge] = Vector3.zero,
                [Engine] = new Vector3(6, 0, 0)
            };
            go.transform.localPosition = origins[start.RoomId] +
                new Vector3(start.X + 0.5f, 0f, start.Y + 0.5f);
            var walker = go.AddComponent<ShipGridWalker>();
            walker.Configure(root.transform, map, origins, start, 3f);
            return walker;
        }

        [Test]
        public void TwoRealShipWalkersAcceptOneGroupOrderAndCrossDoorInLocalSpace()
        {
            using (var session = NewSession())
            {
                var root = new GameObject("G2-Ship");
                try
                {
                    var map = Map(true);
                    var one = Walker(root, "Crew-A", map, Cell(Bridge, 1, 1));
                    var two = Walker(root, "Crew-B", map, Cell(Bridge, 1, 3));
                    var oneProxy = one.gameObject.AddComponent<ShipCrewSelectionProxy>();
                    oneProxy.SetActorId(A);
                    var twoProxy = two.gameObject.AddComponent<ShipCrewSelectionProxy>();
                    twoProxy.SetActorId(B);
                    var executors = new ICrewTheaterMoveExecutor[]
                    {
                        new ShipGridCrewExecutor(A, ShipScene, one),
                        new ShipGridCrewExecutor(B, ShipScene, two)
                    };
                    var goal = Cell(Engine, 2, 2);
                    var selection = new TacticalSelectionController();
                    selection.SelectMultiple(new[] { A, B });
                    var goals = TacticalGroupDestinationAdapters.PlanShipGoals(
                        map, selection.SelectedActorIds, new[] { oneProxy, twoProxy }, goal);
                    var originalRevision = session.Revision;
                    var result = TacticalTheaterSelectionCommands.MoveSelectedAssigned(
                        session, selection, new Vector3(8.5f, 0f, 2.5f),
                        PhysicalTheater.Ship, goals, executors);

                    Assert.That(result.Select(x => x.ActorId), Is.EqualTo(new[] { A, B }));
                    Assert.That(result.All(x => x.Accepted), Is.True);
                    Assert.That(goals.Count, Is.EqualTo(2));
                    Assert.That(goals[A].ShipCell, Is.Not.EqualTo(goals[B].ShipCell),
                        "A group order must allocate distinct parking cells.");
                    Assert.That(one.IsMoving, Is.True);
                    Assert.That(two.IsMoving, Is.True);
                    root.transform.position = new Vector3(42f, 0f, -19f);
                    for (int i = 0; i < 150 && (one.IsMoving || two.IsMoving); i++)
                    {
                        one.Tick(0.1f);
                        two.Tick(0.1f);
                    }
                    Assert.That(one.LastReachedCell.RoomId, Is.EqualTo(Engine));
                    Assert.That(two.LastReachedCell.RoomId, Is.EqualTo(Engine));
                    Assert.That(one.LastReachedCell, Is.Not.EqualTo(two.LastReachedCell));
                    Assert.That(one.transform.localPosition.x, Is.InRange(6.5f, 11.5f));
                    Assert.That(two.transform.localPosition.x, Is.InRange(6.5f, 11.5f));
                    Assert.That(session.Revision, Is.EqualTo(originalRevision),
                        "Physical move presentation must not mutate canonical resources.");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void GroupGoalsNeverParkCrewOnDoorPortalEndpoints()
        {
            var root = new GameObject("G2-Ship");
            try
            {
                var map = Map(true);
                var one = Walker(root, "Crew-A", map, Cell(Bridge, 1, 2));
                var two = Walker(root, "Crew-B", map, Cell(Bridge, 2, 2));
                var oneProxy = one.gameObject.AddComponent<ShipCrewSelectionProxy>();
                oneProxy.SetActorId(A);
                var twoProxy = two.gameObject.AddComponent<ShipCrewSelectionProxy>();
                twoProxy.SetActorId(B);
                var selection = new TacticalSelectionController();
                selection.SelectMultiple(new[] { A, B });
                var goals = TacticalGroupDestinationAdapters.PlanShipGoals(
                    map, selection.SelectedActorIds, new[] { oneProxy, twoProxy },
                    Cell(Engine, 0, 2));

                Assert.That(goals.Count, Is.EqualTo(2));
                Assert.That(goals[A].ShipCell, Is.Not.EqualTo(goals[B].ShipCell));
                Assert.That(goals.Values.Any(x => map.IsPortalEndpoint(x.ShipCell)), Is.False,
                    "Stopping on a single-cell door would block followers indefinitely.");
                Assert.That(goals[A].ShipCell.RoomId, Is.EqualTo(Engine));
                Assert.That(goals[B].ShipCell.RoomId, Is.EqualTo(Engine));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void GroupGoalsNeverTargetAnotherSelectedWalkersOccupiedCell()
        {
            using (var session = NewSession())
            {
                var root = new GameObject("G2-Ship");
                try
                {
                    var map = Map(true);
                    var one = Walker(root, "Crew-A", map, Cell(Bridge, 1, 1));
                    var two = Walker(root, "Crew-B", map, Cell(Bridge, 2, 1));
                    var oneProxy = one.gameObject.AddComponent<ShipCrewSelectionProxy>();
                    oneProxy.SetActorId(A);
                    var twoProxy = two.gameObject.AddComponent<ShipCrewSelectionProxy>();
                    twoProxy.SetActorId(B);
                    var selection = new TacticalSelectionController();
                    selection.SelectMultiple(new[] { A, B });
                    var center = Cell(Bridge, 0, 1);
                    var goals = TacticalGroupDestinationAdapters.PlanShipGoals(
                        map, selection.SelectedActorIds, new[] { oneProxy, twoProxy }, center);

                    Assert.That(goals.Count, Is.EqualTo(2));
                    Assert.That(goals[B].ShipCell, Is.Not.EqualTo(one.LastReachedCell),
                        "A group goal must not send one actor into another selected actor's cell.");
                    Assert.That(goals[A].ShipCell, Is.Not.EqualTo(two.LastReachedCell),
                        "A group goal must not send one actor into another selected actor's cell.");
                    var result = TacticalTheaterSelectionCommands.MoveSelectedAssigned(
                        session, selection, new Vector3(0.5f, 0f, 1.5f),
                        PhysicalTheater.Ship, goals,
                        new ICrewTheaterMoveExecutor[]
                        {
                            new ShipGridCrewExecutor(A, ShipScene, one),
                            new ShipGridCrewExecutor(B, ShipScene, two)
                        });
                    Assert.That(result.All(x => x.Accepted), Is.True,
                        "Every selected actor needs a non-conflicting parking goal.");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void ClosedDoorRejectsOneCrewWhileSecondAcceptsAndNoActorTeleports()
        {
            using (var session = NewSession())
            {
                var root = new GameObject("G2-Ship");
                try
                {
                    var map = Map(false);
                    var one = Walker(root, "Crew-A", map, Cell(Bridge, 1, 2));
                    var two = Walker(root, "Crew-B", map, Cell(Engine, 1, 2));
                    var before = one.transform.position;
                    var intent = TacticalOrderIntent.MoveTo(new[] { A, B }, 8.5, 2.5);
                    var result = CrewTheaterMoveDispatcher.Dispatch(session, intent,
                        CrewMoveGoal.ShipCellGoal(Cell(Engine, 2, 2)),
                        new ICrewTheaterMoveExecutor[]
                        {
                            new ShipGridCrewExecutor(A, ShipScene, one),
                            new ShipGridCrewExecutor(B, ShipScene, two)
                        });
                    Assert.That(result[0].Status,
                        Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
                    Assert.That(result[1].Status,
                        Is.EqualTo(CrewMoveDispatchStatus.Accepted));
                    Assert.That(one.IsMoving, Is.False);
                    Assert.That(two.IsMoving, Is.True);
                    one.Tick(1f);
                    Assert.That(one.transform.position, Is.EqualTo(before));
                    Assert.That(session.Revision, Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void StaleSceneExecutorAndDisabledWalkerRejectWithoutMovement()
        {
            using (var session = NewSession())
            {
                var root = new GameObject("G2-Ship");
                try
                {
                    var walker = Walker(root, "Crew-A", Map(true), Cell(Bridge, 1, 2));
                    var intent = TacticalOrderIntent.MoveTo(new[] { A }, 8, 2);
                    var goal = CrewMoveGoal.ShipCellGoal(Cell(Engine, 2, 2));
                    var stale = CrewTheaterMoveDispatcher.Dispatch(session, intent, goal,
                        new ICrewTheaterMoveExecutor[] {
                            new ShipGridCrewExecutor(A, EntityId.Parse("scene:stale"), walker)
                        });
                    Assert.That(stale.Single().Status,
                        Is.EqualTo(CrewMoveDispatchStatus.StaleSceneExecutor));
                    Assert.That(walker.IsMoving, Is.False);
                    walker.enabled = false;
                    var disabled = CrewTheaterMoveDispatcher.Dispatch(session, intent, goal,
                        new ICrewTheaterMoveExecutor[] {
                            new ShipGridCrewExecutor(A, ShipScene, walker)
                        });
                    Assert.That(disabled.Single().Status,
                        Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
                    Assert.That(walker.IsMoving, Is.False);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        [Test]
        public void SharedSelectionDispatchesShipThenEmptyClickDoesNotCancelAcceptedMove()
        {
            using (var session = NewSession())
            {
                var root = new GameObject("G2-Ship");
                try
                {
                    var walker = Walker(root, "Crew-A", Map(true), Cell(Bridge, 1, 2));
                    var selection = new TacticalSelectionController();
                    selection.Select(A);
                    var executors = new ICrewTheaterMoveExecutor[] {
                        new ShipGridCrewExecutor(A, ShipScene, walker)
                    };
                    var targetCell = Cell(Engine, 2, 2);
                    var results = TacticalTheaterSelectionCommands.MoveSelectedOnShip(
                        session, selection, targetCell, new Vector3(8.5f, 0f, 2.5f),
                        executors);
                    Assert.That(results.Single().Accepted, Is.True);
                    Assert.That(walker.IsMoving, Is.True);
                    selection.Clear(); // blank left-click, not a command cancellation.
                    var empty = TacticalTheaterSelectionCommands.MoveSelectedOnShip(
                        session, selection, Cell(Bridge, 3, 3),
                        new Vector3(3.5f, 0f, 3.5f), executors);
                    Assert.That(empty, Is.Empty);
                    Assert.That(walker.IsMoving, Is.True);
                    for (int i = 0; i < 100 && walker.IsMoving; i++) walker.Tick(0.1f);
                    Assert.That(walker.LastReachedCell, Is.EqualTo(targetCell));
                    Assert.That(session.Revision, Is.Zero);
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }

        private sealed class RecordingShoreExecutor : ICrewTheaterMoveExecutor
        {
            public EntityId ActorId { get; set; }
            public EntityId SceneId { get; set; }
            public PhysicalTheater Theater => PhysicalTheater.Shore;
            public int Calls { get; private set; }
            public Vector3 LastWorldPoint { get; private set; }
            public bool TryMove(CrewMoveGoal goal)
            {
                Calls++;
                LastWorldPoint = goal.ShoreWorld;
                return goal.Theater == PhysicalTheater.Shore;
            }
        }

        [Test]
        public void SameSelectionControllerRoutesOnlyAshoreCrewToWorldExecutor()
        {
            using (var session = NewSession())
            {
                Assert.That(ShoreCrewTransfer.Transfer(session, new[] { B }, true,
                    EntityId.Parse("transition:selection-shore")).Success, Is.True);
                var shoreScene = EntityId.Parse("scene:coast");
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    shoreScene, EntityId.Parse("site:coast"), EntityId.Parse("anchor:landing"),
                    new[] { B }));
                var selection = new TacticalSelectionController();
                selection.SelectMultiple(new[] { A, B });
                var shore = new RecordingShoreExecutor
                {
                    ActorId = B, SceneId = shoreScene
                };
                var point = new Vector3(17f, 0f, 9f);
                var results = TacticalTheaterSelectionCommands.MoveSelectedOnShore(
                    session, selection, point, new ICrewTheaterMoveExecutor[] { shore });
                Assert.That(results.Select(x => x.ActorId), Is.EqualTo(new[] { A, B }));
                Assert.That(results[0].RouteStatus,
                    Is.EqualTo(CrewTheaterRouteStatus.WrongTheater));
                Assert.That(results[1].Accepted, Is.True);
                Assert.That(shore.Calls, Is.EqualTo(1));
                Assert.That(shore.LastWorldPoint, Is.EqualTo(point));
                Assert.That(selection.Count, Is.EqualTo(2),
                    "An order does not persist, remove or rewrite transient selection.");
                Assert.That(session.Capture().State.CrewDuty.Get(B).IsAway, Is.True);
            }
        }
    }
}
