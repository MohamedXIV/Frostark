using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipRouteCommandTests
    {
        private static readonly EntityId A = EntityId.Parse("port:a");
        private static readonly EntityId B = EntityId.Parse("port:b");
        private static readonly EntityId C = EntityId.Parse("port:c");
        private static readonly EntityId AB = EntityId.Parse("leg:ab");
        private static readonly EntityId BC = EntityId.Parse("leg:bc");
        private static readonly EntityId AC = EntityId.Parse("leg:ac");
        static WorldState Start()
        {
            var nav = ShipNavigationState.Create(
                new[] { new SeaWaypoint(A, 0, 0), new SeaWaypoint(B, 10, 0), new SeaWaypoint(C, 20, 0) },
                new[] { new SeaLeg(AB, A, B, 10, 100, 0, 1000),
                        new SeaLeg(BC, B, C, 10, 0, 0, 1000),
                        new SeaLeg(AC, A, C, 25, 0, 0, 1000) },
                A, new NavigationTuning(10, 1, 200, 100, 500, 5, 5), 1000);
            return WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                Array.Empty<EntityId>(), navigation: nav);
        }

        [Test]
        public void InvalidRouteRejectsAtomically()
        {
            var world = Start();
            var bus = new WorldCommandBus();
            foreach (var invalid in new[] {
                new[] { BC }, new[] { AB, AC }, new[] { AB, AB },
                new[] { EntityId.Parse("leg:missing") }
            })
            {
                var result = bus.Execute(world, new PlanShipRouteCommand(
                    EntityId.Parse("cmd:bad"), invalid, world.Revision));
                Assert.That(result.Success, Is.False);
                Assert.That(result.State, Is.SameAs(world));
                Assert.That(result.Events, Is.Empty);
                Assert.That(world.Revision, Is.Zero);
            }
        }

        [Test]
        public void StaleAndDuplicateReject()
        {
            var state = Start();
            var bus = new WorldCommandBus();
            var first = bus.Execute(state, new PlanShipRouteCommand(
                EntityId.Parse("cmd:route"), new[] { AB, BC }, state.Revision));
            Assert.That(first.Success, Is.True);
            Assert.That(first.State.Revision, Is.EqualTo(state.Revision + 1));
            Assert.That(first.Events, Has.Count.EqualTo(1));
            var replay = bus.Execute(first.State, new PlanShipRouteCommand(
                EntityId.Parse("cmd:route"), new[] { AC }, first.State.Revision));
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(replay.State, Is.SameAs(first.State));
            var stale = bus.Execute(first.State, new PlanShipRouteCommand(
                EntityId.Parse("cmd:stale"), new[] { AC }, 0));
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(stale.State, Is.SameAs(first.State));
        }

        [Test]
        public void RouteReplacementAndCancellationAtWaypointWork()
        {
            var world = Start();
            var bus = new WorldCommandBus();
            var first = bus.Execute(world, new PlanShipRouteCommand(
                EntityId.Parse("cmd:first"), new[] { AB, BC }, world.Revision)).State;
            Assert.That(first.Navigation.ActiveLegIds, Is.EqualTo(new[] { AB, BC }));
            var changed = bus.Execute(first, new PlanShipRouteCommand(
                EntityId.Parse("cmd:replace"), new[] { AC }, first.Revision)).State;
            Assert.That(changed.Navigation.ActiveLegIds, Is.EqualTo(new[] { AC }));
            Assert.That(changed.Navigation.CurrentWaypoint, Is.EqualTo(A));
            var cancelled = bus.Execute(changed, new CancelShipRouteCommand(
                EntityId.Parse("cmd:cancel"), changed.Revision));
            Assert.That(cancelled.Success, Is.True);
            Assert.That(cancelled.State.Navigation.IsActive, Is.False);
            Assert.That(cancelled.State.Navigation.CurrentWaypoint, Is.EqualTo(A));
        }
    }
}
