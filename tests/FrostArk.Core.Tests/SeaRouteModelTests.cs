using System;
using System.Linq;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class SeaRouteModelTests
    {
        static readonly EntityId A = EntityId.Parse("waypoint:a");
        static readonly EntityId B = EntityId.Parse("waypoint:b");
        static readonly EntityId C = EntityId.Parse("waypoint:c");
        static readonly EntityId AB = EntityId.Parse("leg:a-b");
        static readonly EntityId BC = EntityId.Parse("leg:b-c");

        private static NavigationTuning Tuning() =>
            new NavigationTuning(10, 2, 500, 250, 400, 20, 8);

        [Test]
        public void RejectsDuplicateWaypointsAndDanglingLegs()
        {
            var points = new[] { new SeaWaypoint(A, 0, 0), new SeaWaypoint(B, 10, 0) };
            Assert.Throws<ArgumentException>(() => ShipNavigationState.Create(
                new[] { points[0], points[0] }, Array.Empty<SeaLeg>(), A, Tuning(), 1000));
            Assert.Throws<ArgumentException>(() => ShipNavigationState.Create(points,
                new[] { new SeaLeg(BC, B, C, 7, 0, 0, 1000) }, A, Tuning(), 1000));
            Assert.Throws<ArgumentException>(() => ShipNavigationState.Create(points,
                new[] { new SeaLeg(AB, A, B, 7, 0, 0, 1000),
                        new SeaLeg(AB, A, B, 7, 0, 0, 1000) }, A, Tuning(), 1000));
        }

        [Test]
        public void RejectsOutOfRangeCoordinatesAndWeather()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeaWaypoint(A, int.MaxValue, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeaLeg(AB, A, B, 10, 1001, 0, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeaLeg(AB, A, B, 0, 0, 0, 1000));
            Assert.Throws<ArgumentException>(() => new SeaLeg(AB, A, A, 10, 0, 0, 1000));
        }

        [Test]
        public void EmptyMigrationDoesNotInventHull()
        {
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            Assert.That(world.Navigation.IsActive, Is.False);
            Assert.That(world.Navigation.CurrentWaypoint.IsValid, Is.False);
            Assert.That(world.Navigation.HullIntegrityPermille, Is.Zero);
        }

        [Test]
        public void CanonicalOrderHasEqualFingerprint()
        {
            var points = new[] { new SeaWaypoint(B, 10, 0), new SeaWaypoint(A, 0, 0) };
            var legs = new[] { new SeaLeg(AB, A, B, 10, 0, 0, 1000) };
            var left = ShipNavigationState.Create(points, legs, A, Tuning(), 800);
            var right = ShipNavigationState.Create(points.AsEnumerable().Reverse(), legs.AsEnumerable().Reverse(), A, Tuning(), 800);
            Assert.That(left.Fingerprint(), Is.EqualTo(right.Fingerprint()));
        }

        [Test]
        public void WorldCopyPathsRetainNavigation()
        {
            var nav = ShipNavigationState.Create(
                new[] { new SeaWaypoint(A, 0, 0), new SeaWaypoint(B, 10, 0) },
                new[] { new SeaLeg(AB, A, B, 10, 0, 0, 1000) }, A, Tuning(), 800);
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(), navigation: nav);
            var next = world.AtMinute(new WorldMinute(1));
            Assert.That(next.Navigation.Fingerprint(), Is.EqualTo(nav.Fingerprint()));
            Assert.That(world.Navigation.CurrentWaypoint, Is.EqualTo(A));
            Assert.That(world.Fingerprint(), Is.Not.EqualTo(WorldState.Create(
                WorldMinute.Zero, Array.Empty<SupplyStock>(), Array.Empty<EntityId>()).Fingerprint()));
        }
    }
}
