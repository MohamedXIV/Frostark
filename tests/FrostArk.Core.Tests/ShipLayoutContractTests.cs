using System;
using System.Linq;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipLayoutContractTests
    {
        private static readonly EntityId Room = EntityId.Parse("room:engine");
        private static readonly EntityId Type = EntityId.Parse("blueprint:engine");
        private static readonly EntityId Part = EntityId.Parse("item:parts");

        private static ShipCompartmentSlot Slot() => new ShipCompartmentSlot(Room, 4, 3);
        private static ShipModuleBlueprint Blueprint() => new ShipModuleBlueprint(
            Type, ShipModuleKind.Engine, 2, 2, Part, 5, 3);

        [Test]
        public void InvalidAuthoredLayoutRejectsDuplicatesAndMissingReferences()
        {
            Assert.Throws<ArgumentException>(() => ShipLayoutState.Create(new[] { Slot(), Slot() },
                new[] { Blueprint() }, Array.Empty<ShipModule>()));
            Assert.Throws<ArgumentException>(() => ShipLayoutState.Create(new[] { Slot() },
                new[] { Blueprint(), Blueprint() }, Array.Empty<ShipModule>()));
            Assert.Throws<ArgumentException>(() => ShipLayoutState.Create(new[] { Slot() },
                new[] { Blueprint() }, new[] { new ShipModule(
                    EntityId.Parse("module:one"), EntityId.Parse("blueprint:missing"), Room, 0, 0) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShipModuleBlueprint(
                Type, (ShipModuleKind)255, 2, 2, Part, 5, 3));
        }

        [Test]
        public void OutOfBoundsAndOverflowingOriginsAreRejected()
        {
            var layout = ShipLayoutState.Create(new[] { Slot() }, new[] { Blueprint() },
                Array.Empty<ShipModule>());
            Assert.That(layout.CanPlace(EntityId.Parse("module:first"), Type, Room, 2, 1), Is.True);
            Assert.That(layout.CanPlace(EntityId.Parse("module:overflow"), Type, Room, int.MaxValue, 1), Is.False);
            Assert.That(layout.CanPlace(EntityId.Parse("module:negative"), Type, Room, -1, 0), Is.False);
            Assert.That(layout.CanPlace(EntityId.Parse("module:outside"), Type, Room, 3, 2), Is.False);
        }

        [Test]
        public void EmptyLayoutKeepsLegacyFullEfficiency()
        {
            Assert.That(ShipLayoutState.Empty.PropulsionPermille, Is.EqualTo(1000));
            Assert.That(ShipLayoutState.Empty.BoilerOutputPermille, Is.EqualTo(1000));
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            Assert.That(world.Layout.Modules, Is.Empty);
            Assert.That(world.Layout.BoilerOutputPermille, Is.EqualTo(1000));
        }

        [Test]
        public void StableOrderGivesSameFingerprint()
        {
            var types = new[] { Blueprint(), new ShipModuleBlueprint(EntityId.Parse("blueprint:aux"),
                ShipModuleKind.Auxiliary, 1, 1, Part, 1, 1) };
            var modules = new[] {
                new ShipModule(EntityId.Parse("module:b"), types[1].Id, Room, 3, 0),
                new ShipModule(EntityId.Parse("module:a"), Type, Room, 0, 0)
            };
            var left = ShipLayoutState.Create(new[] { Slot() }, types, modules);
            var right = ShipLayoutState.Create(new[] { Slot() }, types.AsEnumerable().Reverse(), modules.AsEnumerable().Reverse());
            Assert.That(left.Fingerprint(), Is.EqualTo(right.Fingerprint()));
            Assert.That(left.Modules.Select(m => m.Id.Value), Is.Ordered);
        }
    }
}
