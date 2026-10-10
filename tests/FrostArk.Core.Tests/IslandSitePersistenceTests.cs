using System;
using System.Linq;
using FrostArk.Core.Commands;
using FrostArk.Core.Events;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using FrostArk.Core.Worldgen;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class IslandSitePersistenceTests
    {
        private static readonly EntityId Island = EntityId.Parse("island:harbor-09");
        private static WorldZoneProfile Zone() => new WorldZoneProfile(
            EntityId.Parse("zone:coastal"), ThermalZoneBand.Coastal, 0, 300, 150);

        private static IslandLayout Layout() =>
            IslandGenerator.Generate(Island, seed: 913, Zone(), nodeCount: 9);

        private static (WorldState World, EntityId Site) Start()
        {
            var layout = Layout();
            var firstCrate = layout.Nodes.First(x => x.Kind == IslandNodeKind.Salvage).Id;
            var progress = IslandSiteProgress.Create(new[] { layout });
            return (WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                Array.Empty<EntityId>(), siteProgress: progress), firstCrate);
        }

        [Test]
        public void LootSiteChangesCanonicalWorldOnceAndNeverInventsItems()
        {
            var (world, site) = Start();
            var bus = new WorldCommandBus();
            var looted = bus.Execute(world, new LootSiteCommand(
                EntityId.Parse("cmd:loot-a"), site, expectedRevision: 0));
            Assert.That(looted.Success, Is.True);
            Assert.That(looted.State.SiteProgress.Get(site).IsLooted, Is.True);
            Assert.That(world.SiteProgress.Get(site).IsLooted, Is.False);
            Assert.That(looted.Events[0].Kind, Is.EqualTo(WorldEventKind.SiteLooted));
            Assert.That(looted.State.Revision, Is.EqualTo(1));
            Assert.That(looted.State.Inventory.Fingerprint(), Is.EqualTo(world.Inventory.Fingerprint()));

            var repeated = bus.Execute(looted.State, new LootSiteCommand(
                EntityId.Parse("cmd:loot-again"), site, expectedRevision: 1));
            Assert.That(repeated.Failure, Is.EqualTo(CommandFailure.SiteAlreadyLooted));
            Assert.That(repeated.State, Is.SameAs(looted.State));
            Assert.That(repeated.Events, Is.Empty);
        }

        [Test]
        public void DamageAccumulatesWithinBoundsAndCannotRewind()
        {
            var (world, site) = Start();
            var bus = new WorldCommandBus();
            var damaged = bus.Execute(world, new DamageSiteCommand(
                EntityId.Parse("cmd:hit-1"), site, damagePermille: 700, expectedRevision: 0));
            Assert.That(damaged.Success, Is.True);
            Assert.That(damaged.State.SiteProgress.Get(site).DamagePermille, Is.EqualTo(700));
            var capped = bus.Execute(damaged.State, new DamageSiteCommand(
                EntityId.Parse("cmd:hit-2"), site, damagePermille: 600, expectedRevision: 1));
            Assert.That(capped.Success, Is.True);
            Assert.That(capped.State.SiteProgress.Get(site).DamagePermille, Is.EqualTo(1000));
            Assert.That(capped.Events[0].Kind, Is.EqualTo(WorldEventKind.SiteDamaged));
            var done = bus.Execute(capped.State, new DamageSiteCommand(
                EntityId.Parse("cmd:hit-3"), site, damagePermille: 1, expectedRevision: 2));
            Assert.That(done.Failure, Is.EqualTo(CommandFailure.SiteFullyDamaged));
            Assert.That(done.State, Is.SameAs(capped.State));
            Assert.That(world.SiteProgress.Get(site).DamagePermille, Is.Zero);
        }

        [Test]
        public void UnknownSitesAndInvalidDamageNeverMutate()
        {
            var (world, _) = Start();
            var bus = new WorldCommandBus();
            var unknown = bus.Execute(world, new LootSiteCommand(
                EntityId.Parse("cmd:missing"), EntityId.Parse("site:missing"), 0));
            var invalid = bus.Execute(world, new DamageSiteCommand(
                EntityId.Parse("cmd:invalid"), Layout().LandingId, -1, 0));
            Assert.That(unknown.Failure, Is.EqualTo(CommandFailure.UnknownSite));
            Assert.That(invalid.Failure, Is.EqualTo(CommandFailure.InvalidQuantity));
            Assert.That(unknown.State, Is.SameAs(world));
            Assert.That(invalid.State, Is.SameAs(world));
        }

        [Test]
        public void SavedLootAndDamageStillExistWhenIslandIsRegeneratedAndRevisited()
        {
            var (world, site) = Start();
            var bus = new WorldCommandBus();
            world = bus.Execute(world, new LootSiteCommand(
                EntityId.Parse("cmd:loot"), site, 0)).State;
            world = bus.Execute(world, new DamageSiteCommand(
                EntityId.Parse("cmd:damage"), site, 350, 1)).State;
            var clock = new SimulationScheduler(new WorldClock(world.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 181);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(world, clock));
            var loaded = SaveCodec.TryDecode(bytes);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(SaveCodec.Encode(loaded.Value), Is.EqualTo(bytes));
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(world.Fingerprint()));
            var revisit = IslandGenerator.Generate(Island, 913, Zone(), 9);
            Assert.That(revisit.Nodes.Any(x => x.Id == site), Is.True);
            Assert.That(loaded.Value.State.SiteProgress.Get(site).IsLooted, Is.True);
            Assert.That(loaded.Value.State.SiteProgress.Get(site).DamagePermille, Is.EqualTo(350));
            var denied = bus.Execute(loaded.Value.State, new LootSiteCommand(
                EntityId.Parse("cmd:again"), site, 2));
            Assert.That(denied.Failure, Is.EqualTo(CommandFailure.SiteAlreadyLooted));
        }

        [Test]
        public void MultiIslandProgressHasIndependentStableMembershipAndCanonicalOrder()
        {
            var a = Layout();
            var b = IslandGenerator.Generate(EntityId.Parse("island:south"), 3, Zone(), 8);
            var first = IslandSiteProgress.Create(new[] { a, b });
            var reverse = IslandSiteProgress.Create(new[] { b, a });
            Assert.That(first.Fingerprint(), Is.EqualTo(reverse.Fingerprint()));
            Assert.That(first.Sites.Count, Is.EqualTo(17));
            Assert.That(first.Sites.Select(x => x.Id.Value).Distinct().Count(), Is.EqualTo(17));
            Assert.Throws<ArgumentException>(() => IslandSiteProgress.Create(new[] { a, a }));
        }
    }
}
