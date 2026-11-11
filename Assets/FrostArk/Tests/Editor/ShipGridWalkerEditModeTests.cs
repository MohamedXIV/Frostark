using System;
using System.Collections.Generic;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    public sealed class ShipGridWalkerEditModeTests
    {
        private static readonly EntityId Bridge = EntityId.Parse("room:bridge");
        private static readonly EntityId Engine = EntityId.Parse("room:engine");
        private static readonly EntityId Door = EntityId.Parse("portal:bridge-engine");

        private static ShipGridCell Cell(EntityId room, int x, int y) =>
            new ShipGridCell(room, x, y);

        private static ShipSpatialMap Map(bool doorOpen)
        {
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Bridge, 6, 6),
                new ShipCompartmentSlot(Engine, 6, 6)
            }, Array.Empty<ShipModuleBlueprint>(), Array.Empty<ShipModule>());
            return ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(),
                new[]
                {
                    new ShipRoomPortal(Door, Cell(Bridge, 5, 2),
                        Cell(Engine, 0, 2), doorOpen)
                });
        }

        private static Dictionary<EntityId, Vector3> Origins() =>
            new Dictionary<EntityId, Vector3>
            {
                [Bridge] = Vector3.zero,
                [Engine] = new Vector3(6f, 0f, 0f)
            };

        [Test]
        public void CrossRoomMoveUsesPhysicalLocalCellsAndFollowsMovingShipRoot()
        {
            var ship = new GameObject("G2-ShipRoot");
            var crew = new GameObject("G2-Crew");
            try
            {
                var start = Cell(Bridge, 1, 2);
                crew.transform.position = new Vector3(1.5f, 0f, 2.5f);
                var walker = crew.AddComponent<ShipGridWalker>();
                walker.Configure(ship.transform, Map(true), Origins(), start, 3f);
                Assert.That(walker.TryMoveTo(Cell(Engine, 2, 2)), Is.True);
                for (int i = 0; i < 8; i++) walker.Tick(0.1f);
                Assert.That(crew.transform.position.x, Is.GreaterThan(1.5f));
                Assert.That(walker.IsMoving, Is.True);

                var localBefore = ship.transform.InverseTransformPoint(crew.transform.position);
                ship.transform.position = new Vector3(25f, 0f, 14f);
                walker.Tick(0f);
                Assert.That(Vector3.Distance(
                    ship.transform.InverseTransformPoint(crew.transform.position), localBefore),
                    Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(
                    crew.transform.position, ship.transform.TransformPoint(localBefore)),
                    Is.LessThan(0.0001f));

                for (int i = 0; i < 100 && walker.IsMoving; i++) walker.Tick(0.1f);
                Assert.That(walker.IsMoving, Is.False);
                Assert.That(walker.LastReachedCell, Is.EqualTo(Cell(Engine, 2, 2)));
                Assert.That(ship.transform.InverseTransformPoint(crew.transform.position).x,
                    Is.EqualTo(8.5f).Within(0.002f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crew);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void OpenRoomSmoothsDiagonalAndRampsSpeedWithoutDriftingFromShip()
        {
            var ship = new GameObject("G2-DiagonalShip");
            var crew = new GameObject("G2-DiagonalCrew");
            try
            {
                crew.transform.position = new Vector3(1.5f, 0f, 1.5f);
                var walker = crew.AddComponent<ShipGridWalker>();
                walker.Configure(ship.transform, Map(true), Origins(),
                    Cell(Bridge, 1, 1), 3f);
                Assert.That(walker.TryMoveTo(Cell(Bridge, 4, 4)), Is.True);
                walker.Tick(0.05f);
                var local = ship.transform.InverseTransformPoint(crew.transform.position);
                Assert.That(local.x, Is.GreaterThan(1.5f));
                Assert.That(local.z, Is.GreaterThan(1.5f),
                    "Crew must move diagonally, not always along an L-shaped axis.");
                Assert.That(Mathf.Abs(local.x - local.z), Is.LessThan(0.01f));
                Assert.That(walker.CurrentSpeed, Is.GreaterThan(0f).And.LessThan(3f),
                    "Departure must accelerate rather than instantly snap to full speed.");
                for (int i = 0; i < 100 && walker.IsMoving; i++) walker.Tick(0.05f);
                Assert.That(walker.IsMoving, Is.False);
                Assert.That(walker.LastReachedCell, Is.EqualTo(Cell(Bridge, 4, 4)));
                Assert.That(ship.transform.InverseTransformPoint(crew.transform.position).x,
                    Is.EqualTo(4.5f).Within(0.002f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crew);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void StationaryCrewBlocksNextCellUntilItPhysicallyVacates()
        {
            var ship = new GameObject("G2-TrafficShip");
            try
            {
                var firstGo = new GameObject("G2-WaitingCrew");
                firstGo.transform.SetParent(ship.transform, false);
                firstGo.transform.localPosition = new Vector3(1.5f, 0f, 2.5f);
                var nextGo = new GameObject("G2-BlockingCrew");
                nextGo.transform.SetParent(ship.transform, false);
                nextGo.transform.localPosition = new Vector3(2.5f, 0f, 2.5f);
                var map = Map(true);
                var first = firstGo.AddComponent<ShipGridWalker>();
                first.Configure(ship.transform, map, Origins(),
                    Cell(Bridge, 1, 2), 2.5f);
                var blocker = nextGo.AddComponent<ShipGridWalker>();
                blocker.Configure(ship.transform, map, Origins(),
                    Cell(Bridge, 2, 2), 2.5f);

                Assert.That(first.TryMoveTo(Cell(Bridge, 2, 2)), Is.False,
                    "A stationary peer's real position must not be an arrival target.");
                Assert.That(first.LastError, Is.EqualTo("DestinationOccupied"));
                Assert.That(first.TryMoveTo(Cell(Bridge, 3, 2)), Is.True);
                first.Tick(0.25f);
                Assert.That(first.LastError, Is.EqualTo("WaitingForTraffic"));
                Assert.That(first.LastReachedCell, Is.EqualTo(Cell(Bridge, 1, 2)));
                Assert.That(firstGo.transform.localPosition.x, Is.EqualTo(1.5f).Within(0.001f));

                Assert.That(blocker.TryMoveTo(Cell(Bridge, 4, 2)), Is.True);
                blocker.Tick(0.5f); // Vacates (2,2) and occupies (3,2).
                Assert.That(blocker.LastReachedCell, Is.EqualTo(Cell(Bridge, 3, 2)));
                first.Tick(0.5f);
                Assert.That(first.LastReachedCell, Is.EqualTo(Cell(Bridge, 2, 2)),
                    "Waiting crew may enter only after the blocker physically leaves.");
                Assert.That(first.LastReachedCell, Is.Not.EqualTo(blocker.LastReachedCell));
                Assert.That(first.TryMoveTo(Cell(Bridge, 4, 2)), Is.False,
                    "Another moving actor's reserved destination cannot be stolen.");
                Assert.That(first.LastError, Is.EqualTo("DestinationOccupied"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void TrafficReplanFindsOpenDetourAfterBoundedWaitNotEveryFrame()
        {
            var ship = new GameObject("G2-DetourShip");
            try
            {
                var firstGo = new GameObject("G2-DetourCrew");
                firstGo.transform.SetParent(ship.transform, false);
                firstGo.transform.localPosition = new Vector3(0.5f, 0f, 2.5f);
                var blockingGo = new GameObject("G2-StaticBlocker");
                blockingGo.transform.SetParent(ship.transform, false);
                blockingGo.transform.localPosition = new Vector3(1.5f, 0f, 2.5f);
                var map = Map(true);
                var mover = firstGo.AddComponent<ShipGridWalker>();
                mover.Configure(ship.transform, map, Origins(), Cell(Bridge, 0, 2));
                var blocker = blockingGo.AddComponent<ShipGridWalker>();
                blocker.Configure(ship.transform, map, Origins(), Cell(Bridge, 1, 2));
                Assert.That(mover.TryMoveTo(Cell(Bridge, 3, 2)), Is.True);
                mover.Tick(0.25f);
                Assert.That(mover.LastError, Is.EqualTo("WaitingForTraffic"));
                Assert.That(firstGo.transform.localPosition.x, Is.EqualTo(0.5f).Within(0.001f));
                mover.Tick(0.50f); // Bounded timeout recomputes around the occupied cell.
                mover.Tick(0.40f); // Executes new detour; it must not use (1,2).
                Assert.That(mover.LastError, Is.Null);
                Assert.That(firstGo.transform.localPosition.z, Is.Not.EqualTo(2.5f).Within(0.01f));
                Assert.That(blocker.LastReachedCell, Is.EqualTo(Cell(Bridge, 1, 2)));
                Assert.That(map.Walkable(Cell(Bridge, 1, 2)), Is.True,
                    "Transient crew occupation never changes immutable authored floor.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void ClosedDoorRejectsGroupTargetWithoutTeleporting()
        {
            var ship = new GameObject("G2-Ship");
            var crew = new GameObject("G2-Crew");
            try
            {
                crew.transform.position = new Vector3(1.5f, 0f, 2.5f);
                var walker = crew.AddComponent<ShipGridWalker>();
                walker.Configure(ship.transform, Map(false), Origins(),
                    Cell(Bridge, 1, 2));
                Assert.That(walker.TryMoveTo(Cell(Engine, 3, 2)), Is.False);
                Assert.That(walker.LastError, Is.EqualTo("NoRoute"));
                walker.Tick(1f);
                Assert.That(crew.transform.position, Is.EqualTo(new Vector3(1.5f, 0, 2.5f)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crew);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void ClosingDoorInvalidatesActiveRouteAndRetreatsToSafeCellBeforeNewOrder()
        {
            var ship = new GameObject("G2-Ship");
            var crew = new GameObject("G2-Crew");
            try
            {
                crew.transform.position = new Vector3(1.5f, 0f, 2.5f);
                var walker = crew.AddComponent<ShipGridWalker>();
                walker.Configure(ship.transform, Map(true), Origins(),
                    Cell(Bridge, 1, 2));
                Assert.That(walker.TryMoveTo(Cell(Engine, 2, 2)), Is.True);
                walker.Tick(0.2f);
                var before = crew.transform.position;
                Assert.That(walker.RebindMap(Map(false)), Is.False);
                Assert.That(walker.LastError, Is.EqualTo("RouteInvalidated"));
                Assert.That(walker.IsMoving, Is.False);
                Assert.That(walker.IsRecovering, Is.True);
                Assert.That(walker.HasIncomingReservation, Is.True,
                    "The next cell stays reserved until the actor is safely back.");
                Assert.That(walker.TryMoveTo(Cell(Bridge, 4, 2)), Is.False);
                Assert.That(walker.LastError, Is.EqualTo("RecoveringToSafeCell"));

                for (int i = 0; i < 20 && walker.IsRecovering; i++) walker.Tick(0.1f);
                Assert.That(walker.IsRecovering, Is.False);
                Assert.That(walker.HasIncomingReservation, Is.False);
                Assert.That(crew.transform.position, Is.Not.EqualTo(before),
                    "Recovery walks back to the reached cell, never teleports.");
                Assert.That(crew.transform.position,
                    Is.EqualTo(new Vector3(1.5f, 0f, 2.5f)).Within(0.0001f));

                Assert.That(walker.RebindMap(Map(true)), Is.True);
                Assert.That(walker.TryMoveTo(Cell(Engine, 2, 2)), Is.True);
                for (int i = 0; i < 100 && walker.IsMoving; i++) walker.Tick(0.1f);
                Assert.That(walker.LastReachedCell, Is.EqualTo(Cell(Engine, 2, 2)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crew);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }

        [Test]
        public void InvalidNewGoalDoesNotInterruptPreviouslyAcceptedMove()
        {
            var ship = new GameObject("G2-Ship");
            var crew = new GameObject("G2-Crew");
            try
            {
                crew.transform.position = new Vector3(1.5f, 0f, 2.5f);
                var walker = crew.AddComponent<ShipGridWalker>();
                walker.Configure(ship.transform, Map(false), Origins(),
                    Cell(Bridge, 1, 2));
                Assert.That(walker.TryMoveTo(Cell(Bridge, 4, 2)), Is.True);
                Assert.That(walker.TryMoveTo(Cell(Engine, 4, 2)), Is.False);
                Assert.That(walker.IsMoving, Is.True);
                Assert.That(walker.OrderedCell, Is.EqualTo(Cell(Bridge, 4, 2)));
                for (int i = 0; i < 40 && walker.IsMoving; i++) walker.Tick(0.1f);
                Assert.That(walker.LastReachedCell, Is.EqualTo(Cell(Bridge, 4, 2)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(crew);
                UnityEngine.Object.DestroyImmediate(ship);
            }
        }
    }
}
