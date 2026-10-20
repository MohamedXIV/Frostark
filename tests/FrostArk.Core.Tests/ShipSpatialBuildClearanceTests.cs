using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipSpatialBuildClearanceTests
    {
        private static readonly EntityId Room = EntityId.Parse("room:engine");
        private static readonly EntityId Parts = EntityId.Parse("item:parts");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:workstation");
        private static readonly EntityId Station = EntityId.Parse("module:station");
        private static readonly EntityId Other = EntityId.Parse("module:other");
        private static readonly EntityId SlotId = EntityId.Parse("slot:station:work");

        private static WorldState Begin(ShipLayoutState layout,
            CrewInteractionState interactions)
        {
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Parts, 1, 100, 100)
            }), new[] { new SalvagePickup(EntityId.Parse("pickup:parts"), Parts, 15) },
                1000, 10, 1000, 10);
            inventory = inventory.TryCollect(EntityId.Parse("pickup:parts")).State
                .TryUnload(Parts, 15).State;
            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: inventory,
                crewDuty: CrewState.Create(Array.Empty<CrewMember>(), interactions: interactions),
                layout: layout);
        }

        private static ShipLayoutState Layout(bool existingStation = true)
        {
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Room, 6, 6)
            }, new[]
            {
                new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                    1, 1, Parts, 3, 1)
            }, Array.Empty<ShipModule>());
            return existingStation ? layout.Place(Station, Blueprint, Room, 2, 2) : layout;
        }

        private static CrewInteractionState Slots(EntityId source = default,
            int dx = 0, int dy = -1) =>
            CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(SlotId, source.IsValid ? source : Station,
                    CrewInteractionVerb.Work, dx, dy, 0)
            });

        private static BuildShipModuleCommand Build(EntityId id, int x, int y,
            long revision = 0) =>
            new BuildShipModuleCommand(
                EntityId.Parse("command:build:" + id.Value + ":" + x + ":" + y),
                id, Blueprint, Room, x, y, revision);

        [Test]
        public void BuildingOverExistingStationStandingCellRejectsBeforeSpendingOrEvent()
        {
            var start = Begin(Layout(), Slots());
            var command = Build(Other, 2, 1);
            Assert.That(start.Layout.CanPlace(Other, Blueprint, Room, 2, 1), Is.True,
                "The old footprint-only validator cannot see station standing clearance.");
            var outcome = new WorldCommandBus().Execute(start, command);
            Assert.That(outcome.Success, Is.False);
            Assert.That(outcome.Failure, Is.EqualTo(CommandFailure.InvalidPlacement));
            Assert.That(outcome.State, Is.SameAs(start));
            Assert.That(outcome.Events, Is.Empty);
            Assert.That(outcome.State.Inventory.GetAmount(InventoryContainer.ShipHold, Parts),
                Is.EqualTo(15));
            Assert.That(outcome.State.Layout.GetModule(Other), Is.Null);
        }

        [Test]
        public void UnrelatedPlacementCommitsExactlyOnceAndKeepsStandingSlotWalkable()
        {
            var start = Begin(Layout(), Slots());
            var result = new WorldCommandBus().Execute(start, Build(Other, 4, 4));
            Assert.That(result.Success, Is.True, result.Failure.ToString());
            Assert.That(result.State.Inventory.GetAmount(InventoryContainer.ShipHold, Parts),
                Is.EqualTo(12));
            Assert.That(result.State.Revision, Is.EqualTo(1));
            var map = ShipSpatialMap.Derive(result.State.Layout,
                Array.Empty<ShipModuleSpatialProfile>(), Array.Empty<ShipRoomPortal>());
            Assert.That(map.Walkable(new ShipGridCell(Room, 2, 1)), Is.True);
            Assert.That(map.PreservesModuleInteractionSlots(
                result.State.Layout, result.State.CrewDuty.Interactions), Is.True);
        }

        [Test]
        public void NewStationCannotBeBuiltWithOwnStandingSlotOutsideRoom()
        {
            var start = Begin(Layout(existingStation: false), Slots(source: Other));
            var result = new WorldCommandBus().Execute(start, Build(Other, 2, 0));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(CommandFailure.InvalidPlacement));
            Assert.That(result.State, Is.SameAs(start));
            Assert.That(start.Inventory.GetAmount(InventoryContainer.ShipHold, Parts),
                Is.EqualTo(15));
        }

        [Test]
        public void SlotForUnplacedFixtureSourceDoesNotBlockUnrelatedBuild()
        {
            var orphanSource = EntityId.Parse("fixture:independent-prop");
            var start = Begin(Layout(), Slots(orphanSource));
            var result = new WorldCommandBus().Execute(start, Build(Other, 2, 1));
            Assert.That(result.Success, Is.True,
                "Only slots whose source is a built canonical ShipModule are resolved.");
            Assert.That(result.State.Revision, Is.EqualTo(1));
        }

        [Test]
        public void SavedStationSourceAndStandingSlotRemainProtectedAfterRestore()
        {
            byte[] save;
            using (var session = new WorldSession(Begin(Layout(), Slots())))
                save = SaveCodec.Encode(session.Capture());
            var loaded = SaveCodec.TryDecode(save);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            using (var resumed = WorldSession.Restore(loaded.Value))
            {
                var before = resumed.Capture().State.Fingerprint();
                var blocked = resumed.Submit(Build(Other, 2, 1, resumed.Revision));
                Assert.That(blocked.Success, Is.False);
                Assert.That(blocked.Failure, Is.EqualTo(CommandFailure.InvalidPlacement));
                Assert.That(resumed.Revision, Is.Zero);
                Assert.That(resumed.Capture().State.Fingerprint(), Is.EqualTo(before));
                Assert.That(resumed.Capture().State.CrewDuty.Interactions.GetSlot(SlotId).SourceId,
                    Is.EqualTo(Station));
            }
        }
    }
}
