using System;
using System.Linq;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipSpatialMapTests
    {
        private static readonly EntityId A = EntityId.Parse("room:bridge");
        private static readonly EntityId B = EntityId.Parse("room:engine");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:machine");
        private static readonly EntityId Part = EntityId.Parse("item:part");
        private static readonly EntityId Engine = EntityId.Parse("module:engine");
        private static readonly EntityId Door = EntityId.Parse("portal:bulkhead");

        private static ShipLayoutState Layout(params ShipModule[] modules) =>
            ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(A, 6, 6),
                new ShipCompartmentSlot(B, 6, 6)
            }, new[] { new ShipModuleBlueprint(Blueprint, ShipModuleKind.Engine,
                2, 2, Part, 1, 1) }, modules);

        private static ShipGridCell C(EntityId room, int x, int y) =>
            new ShipGridCell(room, x, y);

        private static ShipModuleSpatialProfile Machine() =>
            new ShipModuleSpatialProfile(Blueprint,
                new[] { new ShipLocalOffset(0, 0), new ShipLocalOffset(1, 0) },
                new[] { new ShipLocalOffset(0, -1), new ShipLocalOffset(1, -1) });

        private static ShipRoomPortal Portal(bool open = true) =>
            new ShipRoomPortal(Door, C(A, 5, 3), C(B, 0, 3), open);

        [Test]
        public void ClearRoomUsesDirectDiagonalRatherThanOrthogonalLRoute()
        {
            var map = ShipSpatialMap.Derive(Layout(),
                Array.Empty<ShipModuleSpatialProfile>(), Array.Empty<ShipRoomPortal>());
            var path = map.FindPath(C(B, 0, 0), C(B, 4, 4));
            Assert.That(path.Count, Is.EqualTo(5));
            for (int n = 1; n < path.Count; n++)
            {
                Assert.That(path[n].X - path[n - 1].X, Is.EqualTo(1));
                Assert.That(path[n].Y - path[n - 1].Y, Is.EqualTo(1));
            }
            Assert.That(map.CanTraverseStraight(C(B, 0, 0), C(B, 4, 4)), Is.True);
            Assert.That(map.CanTraverseStraight(C(A, 0, 0), C(B, 0, 0)), Is.False,
                "Smoothing must never skip authored room portals.");
        }

        [Test]
        public void DiagonalCornerCutAndStraightLineThroughMachineAreForbidden()
        {
            var map = ShipSpatialMap.Derive(
                Layout(new ShipModule(Engine, Blueprint, B, 1, 0)),
                Array.Empty<ShipModuleSpatialProfile>(),
                Array.Empty<ShipRoomPortal>());
            // From (0,1) to (1,2) the diagonal squeezes past solid (1,1).
            Assert.That(map.Walkable(C(B, 0, 1)), Is.True);
            Assert.That(map.Walkable(C(B, 1, 2)), Is.True);
            Assert.That(map.CanTraverseStraight(C(B, 0, 1), C(B, 1, 2)), Is.False);
            var path = map.FindPath(C(B, 0, 1), C(B, 1, 2));
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(path[1], Is.Not.EqualTo(C(B, 1, 2)));
            Assert.That(map.CanTraverseStraight(C(B, 0, 0), C(B, 4, 2)), Is.False,
                "Path smoothing may not tunnel through machine footprint.");
        }

        [Test]
        public void DefaultBlueprintBlocksEntirePhysicalFootprint()
        {
            var map = ShipSpatialMap.Derive(Layout(new ShipModule(Engine, Blueprint, B, 1, 1)),
                Array.Empty<ShipModuleSpatialProfile>(), Array.Empty<ShipRoomPortal>());
            Assert.That(map.Walkable(C(B, 1, 1)), Is.False);
            Assert.That(map.Walkable(C(B, 2, 2)), Is.False);
            Assert.That(map.Walkable(C(B, 0, 1)), Is.True);
            Assert.That(map.BuildReserved(C(B, 1, 1)), Is.True);
            Assert.That(map.ModuleClearanceAvailable(Engine), Is.True);
        }

        [Test]
        public void CustomSolidFootprintCanContainWalkableCellsButReservesThemForBuild()
        {
            var map = ShipSpatialMap.Derive(Layout(new ShipModule(Engine, Blueprint, B, 1, 2)),
                new[] { Machine() }, Array.Empty<ShipRoomPortal>());
            Assert.That(map.Walkable(C(B, 1, 2)), Is.False);
            Assert.That(map.Walkable(C(B, 2, 2)), Is.False);
            Assert.That(map.Walkable(C(B, 1, 3)), Is.True);
            Assert.That(map.BuildReserved(C(B, 1, 3)), Is.True);
            Assert.That(map.Walkable(C(B, 1, 1)), Is.True);
            Assert.That(map.BuildReserved(C(B, 1, 1)), Is.True,
                "Worker clearance reserves construction but stays walkable.");
            Assert.That(map.ModuleClearanceAvailable(Engine), Is.True);
        }

        [Test]
        public void TwoRoomsConnectOnlyThroughAuthoredOpenDoor()
        {
            var layout = Layout();
            var closed = ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(), new[] { Portal(false) });
            Assert.That(closed.FindPath(C(A, 1, 3), C(B, 4, 3)), Is.Empty);
            var open = ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(), new[] { Portal(true) });
            var path = open.FindPath(C(A, 1, 3), C(B, 4, 3));
            Assert.That(path, Is.Not.Empty);
            Assert.That(path.Any(c => c == C(A, 5, 3)), Is.True);
            Assert.That(path.Any(c => c == C(B, 0, 3)), Is.True);
            Assert.That(path.Count, Is.EqualTo(10));
        }

        [Test]
        public void SolidStationOrDoorEndpointMakesRouteUnreachableWithoutTeleport()
        {
            var module = new ShipModule(Engine, Blueprint, B, 0, 3);
            var map = ShipSpatialMap.Derive(Layout(module),
                Array.Empty<ShipModuleSpatialProfile>(), new[] { Portal(true) });
            Assert.That(map.Walkable(C(B, 0, 3)), Is.False);
            Assert.That(map.FindPath(C(A, 1, 3), C(B, 4, 3)), Is.Empty);
        }

        [Test]
        public void MapRebuildFromNewCanonicalLayoutImmediatelyChangesWalkability()
        {
            var layout = Layout();
            var first = ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(), Array.Empty<ShipRoomPortal>());
            Assert.That(first.Walkable(C(B, 1, 1)), Is.True);
            var placed = layout.Place(Engine, Blueprint, B, 1, 1);
            var after = ShipSpatialMap.Derive(placed,
                Array.Empty<ShipModuleSpatialProfile>(), Array.Empty<ShipRoomPortal>());
            Assert.That(after.Walkable(C(B, 1, 1)), Is.False);
            Assert.That(first.Walkable(C(B, 1, 1)), Is.True,
                "Derived snapshots never mutate the previous canonical layout.");
        }

        [Test]
        public void InvalidProfilesAndPortalsRejectAuthoredGeometry()
        {
            Assert.Throws<ArgumentException>(() => ShipSpatialMap.Derive(Layout(),
                Array.Empty<ShipModuleSpatialProfile>(), new[]
                {
                    new ShipRoomPortal(Door, C(A, 10, 1), C(B, 0, 1))
                }));
            Assert.Throws<ArgumentException>(() => ShipSpatialMap.Derive(Layout(),
                new[]
                {
                    new ShipModuleSpatialProfile(EntityId.Parse("blueprint:unknown"),
                        Array.Empty<ShipLocalOffset>(), Array.Empty<ShipLocalOffset>())
                }, Array.Empty<ShipRoomPortal>()));
            Assert.Throws<ArgumentException>(() => ShipSpatialMap.Derive(
                Layout(new ShipModule(Engine, Blueprint, B, 1, 1)),
                new[] { new ShipModuleSpatialProfile(Blueprint,
                    new[] { new ShipLocalOffset(3, 0) },
                    Array.Empty<ShipLocalOffset>()) }, Array.Empty<ShipRoomPortal>()));
            Assert.Throws<ArgumentException>(() => ShipSpatialMap.Derive(
                Layout(new ShipModule(Engine, Blueprint, B, 1, 0)),
                new[] { Machine() }, Array.Empty<ShipRoomPortal>()),
                "A required interaction clearance must not disappear beyond a room wall.");
        }

        [Test]
        public void ModuleClearanceBlockedByAnotherPhysicalFootprintIsExplicitlyInvalid()
        {
            var other = EntityId.Parse("module:other");
            var layout = Layout(
                new ShipModule(Engine, Blueprint, B, 1, 3),
                new ShipModule(other, Blueprint, B, 1, 1));
            var map = ShipSpatialMap.Derive(layout, new[] { Machine() },
                Array.Empty<ShipRoomPortal>());
            Assert.That(map.ModuleClearanceAvailable(Engine), Is.False,
                "The old construction validator does not yet prevent this placement.");
            Assert.That(map.BuildReserved(C(B, 1, 2)), Is.True);
        }

        [Test]
        public void DeterministicRouteIndependentOfPortalSourceOrder()
        {
            var another = new ShipRoomPortal(EntityId.Parse("portal:aux"),
                C(A, 5, 4), C(B, 0, 4));
            var forward = ShipSpatialMap.Derive(Layout(),
                Array.Empty<ShipModuleSpatialProfile>(), new[] { Portal(), another });
            var reverse = ShipSpatialMap.Derive(Layout(),
                Array.Empty<ShipModuleSpatialProfile>(), new[] { another, Portal() });
            var a = forward.FindPath(C(A, 1, 2), C(B, 5, 2));
            var b = reverse.FindPath(C(A, 1, 2), C(B, 5, 2));
            Assert.That(a, Is.EqualTo(b));
        }
    }
}
