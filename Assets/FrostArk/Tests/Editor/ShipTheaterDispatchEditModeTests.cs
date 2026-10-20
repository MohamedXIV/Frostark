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
                    var executors = new ICrewTheaterMoveExecutor[]
                    {
                        new ShipGridCrewExecutor(A, ShipScene, one),
                        new ShipGridCrewExecutor(B, ShipScene, two)
                    };
                    var goal = Cell(Engine, 2, 2);
                    var intent = TacticalOrderIntent.MoveTo(new[] { B, A }, 8.5, 2.5);
                    var originalRevision = session.Revision;
                    var result = CrewTheaterMoveDispatcher.Dispatch(session, intent,
                        CrewMoveGoal.ShipCellGoal(goal), executors);

                    Assert.That(result.Select(x => x.ActorId), Is.EqualTo(new[] { A, B }));
                    Assert.That(result.All(x => x.Accepted), Is.True);
                    Assert.That(one.IsMoving, Is.True);
                    Assert.That(two.IsMoving, Is.True);
                    root.transform.position = new Vector3(42f, 0f, -19f);
                    for (int i = 0; i < 100 && (one.IsMoving || two.IsMoving); i++)
                    {
                        one.Tick(0.1f);
                        two.Tick(0.1f);
                    }
                    Assert.That(one.LastReachedCell, Is.EqualTo(goal));
                    Assert.That(two.LastReachedCell, Is.EqualTo(goal));
                    Assert.That(one.transform.localPosition.x, Is.EqualTo(8.5f).Within(0.01f));
                    Assert.That(two.transform.localPosition.x, Is.EqualTo(8.5f).Within(0.01f));
                    Assert.That(session.Revision, Is.EqualTo(originalRevision),
                        "Physical move presentation must not mutate canonical resources.");
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
    }
}
