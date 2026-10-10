using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class SalvageCommandTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:shore-01");

        private static WorldState Start(long packGrams = 2000, int packSlots = 2)
        {
            var inventory = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Scrap, 2, 250, 5) }),
                new[] { new SalvagePickup(Pickup, Scrap, 3) },
                packGrams, packSlots, 10000, 10);
            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(), inventory);
        }

        [Test]
        public void CollectAndUnloadAreCanonicalRevisionedCommandsWithProvenance()
        {
            var bus = new WorldCommandBus();
            var original = Start();
            var collect = bus.Execute(original,
                new CollectSalvageCommand(EntityId.Parse("command:collect"), Pickup, 0));
            Assert.That(collect.Success, Is.True);
            Assert.That(original.Inventory.GetAmount(InventoryContainer.Pack, Scrap), Is.Zero);
            Assert.That(collect.State.Inventory.IsCollected(Pickup), Is.True);
            Assert.That(collect.State.Inventory.GetAmount(InventoryContainer.Pack, Scrap),
                Is.EqualTo(3));
            Assert.That(collect.Events[0].Kind, Is.EqualTo(WorldEventKind.SalvageCollected));

            var unload = bus.Execute(collect.State,
                new UnloadToShipCommand(EntityId.Parse("command:unload"), Scrap, 3, 1));
            Assert.That(unload.Success, Is.True);
            Assert.That(unload.State.Revision, Is.EqualTo(2));
            Assert.That(unload.State.Inventory.GetAmount(InventoryContainer.Pack, Scrap), Is.Zero);
            Assert.That(unload.State.Inventory.GetAmount(InventoryContainer.ShipHold, Scrap),
                Is.EqualTo(3));
            Assert.That(unload.Events[0].Kind, Is.EqualTo(WorldEventKind.SalvageUnloaded));
            Assert.That(unload.Events[0].Quantity, Is.EqualTo(3));
        }

        [Test]
        public void AlreadyCollectedAndStaleOrDuplicateCommandsNeverCommit()
        {
            var bus = new WorldCommandBus();
            var first = bus.Execute(Start(),
                new CollectSalvageCommand(EntityId.Parse("command:collect"), Pickup, 0));
            var again = bus.Execute(first.State,
                new CollectSalvageCommand(EntityId.Parse("command:again"), Pickup, 1));
            var duplicate = bus.Execute(first.State,
                new CollectSalvageCommand(EntityId.Parse("command:collect"), Pickup, 1));
            var stale = bus.Execute(first.State,
                new UnloadToShipCommand(EntityId.Parse("command:stale"), Scrap, 1, 0));

            Assert.That(again.Failure, Is.EqualTo(CommandFailure.AlreadyCollected));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(again.State, Is.SameAs(first.State));
            Assert.That(again.Events, Is.Empty);
        }

        [Test]
        public void OverweightCollectionAndExcessUnloadingRejectWithoutStateChanges()
        {
            var bus = new WorldCommandBus();
            var original = Start(packGrams: 100);
            var rejection = bus.Execute(original,
                new CollectSalvageCommand(EntityId.Parse("command:too-heavy"), Pickup, 0));
            Assert.That(rejection.Failure, Is.EqualTo(CommandFailure.InventoryTooHeavy));
            Assert.That(rejection.State, Is.SameAs(original));
            Assert.That(rejection.Events, Is.Empty);

            var collect = bus.Execute(Start(),
                new CollectSalvageCommand(EntityId.Parse("command:ok"), Pickup, 0));
            var unload = bus.Execute(collect.State,
                new UnloadToShipCommand(EntityId.Parse("command:too-much"), Scrap, 4, 1));
            Assert.That(unload.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
            Assert.That(unload.State, Is.SameAs(collect.State));
        }

        [Test]
        public void SerializedSalvageCanUnloadAfterReloadAndNeverRespawn()
        {
            var bus = new WorldCommandBus();
            var collected = bus.Execute(Start(),
                new CollectSalvageCommand(EntityId.Parse("command:first"), Pickup, 0));
            var scheduler = new FrostArk.Core.Time.SimulationScheduler(
                new FrostArk.Core.Time.WorldClock(collected.State.Minute),
                Array.Empty<FrostArk.Core.Time.IWorldMinuteSystem>(), seed: 321);
            var blob = SaveCodec.Encode(SaveGameDto.Capture(collected.State, scheduler));
            var load = SaveCodec.TryDecode(blob);
            Assert.That(load.Success, Is.True, load.Error.ToString());
            Assert.That(load.Value.State.Inventory.IsCollected(Pickup), Is.True);
            Assert.That(load.Value.State.Inventory.GetAmount(InventoryContainer.Pack, Scrap),
                Is.EqualTo(3));

            var unload = bus.Execute(load.Value.State,
                new UnloadToShipCommand(EntityId.Parse("command:ship"), Scrap, 3, 1));
            Assert.That(unload.Success, Is.True);
            var again = bus.Execute(unload.State,
                new CollectSalvageCommand(EntityId.Parse("command:duplicate-pickup"), Pickup, 2));
            Assert.That(again.Failure, Is.EqualTo(CommandFailure.AlreadyCollected));
            Assert.That(unload.State.Inventory.GetAmount(InventoryContainer.ShipHold, Scrap),
                Is.EqualTo(3));

            var nextSnapshot = SaveGameDto.Capture(unload.State, scheduler);
            Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(nextSnapshot)).Value.State.Inventory
                .GetAmount(InventoryContainer.ShipHold, Scrap), Is.EqualTo(3));
        }

        [Test]
        public void InventoryChangesWorldFingerprintDeterministically()
        {
            var before = Start();
            var after = new WorldCommandBus().Execute(before,
                new CollectSalvageCommand(EntityId.Parse("command:pickup"), Pickup, 0));
            Assert.That(after.Success, Is.True);
            Assert.That(before.Fingerprint(), Is.Not.EqualTo(after.State.Fingerprint()));
            Assert.That(Start().Fingerprint(), Is.EqualTo(before.Fingerprint()));
        }
    }
}
