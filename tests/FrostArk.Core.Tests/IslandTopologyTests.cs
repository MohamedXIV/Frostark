using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;
using FrostArk.Core.Worldgen;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class IslandTopologyTests
    {
        private static readonly EntityId Island = EntityId.Parse("island:far-north");
        private static WorldZoneProfile Zone() => new WorldZoneProfile(
            EntityId.Parse("zone:pack"), ThermalZoneBand.PackIce, -35000,
            contaminationPermille: 150, packIcePermille: 850);

        [Test]
        public void FixedIslandAndSeedAreByteStableAcrossIndependentRuns()
        {
            var a = IslandGenerator.Generate(Island, 1337, Zone(), nodeCount: 12);
            var b = IslandGenerator.Generate(Island, 1337, Zone(), nodeCount: 12);
            Assert.That(a.Fingerprint(), Is.EqualTo(b.Fingerprint()));
            Assert.That(a.Nodes.Select(x => x.Id.Value),
                Is.EqualTo(b.Nodes.Select(x => x.Id.Value)));
            Assert.That(a.Nodes.Count, Is.EqualTo(12));
            Assert.That(a.Edges.Count, Is.GreaterThanOrEqualTo(11));
            Assert.That(a.Get(a.LandingId).Kind, Is.EqualTo(IslandNodeKind.Landing));
            Assert.That(a.Get(a.ExtractionId).Kind, Is.EqualTo(IslandNodeKind.Extraction));
            Assert.That(a.CanReachExtraction(safeOnly: true), Is.True);
            Assert.That(a.AreAllSitesReachable(), Is.True);
            Assert.That(a.Nodes.Select(x => (x.X, x.Y)).Distinct().Count(), Is.EqualTo(12));
            Assert.That(a.Nodes.Count(x => x.Kind == IslandNodeKind.Salvage), Is.GreaterThan(0));
            Assert.That(a.Edges.Any(x => !x.IsSafe), Is.True);
        }

        [Test]
        public void DifferentWorldSeedsOrIslandIdsChangeTopologyWithoutUsingGlobalRng()
        {
            var original = IslandGenerator.Generate(Island, 12, Zone(), 14);
            var otherSeed = IslandGenerator.Generate(Island, 13, Zone(), 14);
            var otherIsland = IslandGenerator.Generate(
                EntityId.Parse("island:far-south"), 12, Zone(), 14);
            Assert.That(otherSeed.Fingerprint(), Is.Not.EqualTo(original.Fingerprint()));
            Assert.That(otherIsland.Fingerprint(), Is.Not.EqualTo(original.Fingerprint()));
            Assert.That(IslandGenerator.Generate(Island, 12, Zone(), 14).Fingerprint(),
                Is.EqualTo(original.Fingerprint()));
        }

        [Test]
        public void EverySeedHasASafeLandingToExtractionSpineAndReachableSites()
        {
            for (int seed = -64; seed < 64; seed++)
            {
                var graph = IslandGenerator.Generate(Island, seed, Zone(), nodeCount: 28);
                Assert.That(graph.CanReachExtraction(safeOnly: true), Is.True, "seed " + seed);
                Assert.That(graph.AreAllSitesReachable(), Is.True, "seed " + seed);
                Assert.That(graph.Get(graph.LandingId), Is.Not.Null);
                Assert.That(graph.Get(graph.ExtractionId), Is.Not.Null);
            }
        }

        [Test]
        public void AuthoredOrderDoesNotChangeFingerprintOrTraversal()
        {
            var graph = IslandGenerator.Generate(Island, 55, Zone(), 16);
            var reversed = IslandLayout.Create(Island, Zone(),
                graph.Nodes.Reverse(), graph.Edges.Reverse());
            Assert.That(reversed.Fingerprint(), Is.EqualTo(graph.Fingerprint()));
            Assert.That(reversed.CanReachExtraction(safeOnly: true), Is.True);
            Assert.That(reversed.AreAllSitesReachable(), Is.True);
        }

        [Test]
        public void GraphRejectsDanglingLinksDuplicateSitesOrUnsafeOnlyExtraction()
        {
            var land = new IslandNode(EntityId.Parse("site:land"), 0, 0, IslandNodeKind.Landing);
            var exit = new IslandNode(EntityId.Parse("site:exit"), 20, 0, IslandNodeKind.Extraction);
            var a = new IslandNode(EntityId.Parse("site:a"), 10, 0, IslandNodeKind.Salvage);
            var path = new[]
            {
                new IslandEdge(land.Id, a.Id, isSafe: true),
                new IslandEdge(a.Id, exit.Id, isSafe: true)
            };
            Assert.Throws<ArgumentException>(() => IslandLayout.Create(Island, Zone(),
                new[] { land, land, exit }, path));
            Assert.Throws<ArgumentException>(() => IslandLayout.Create(Island, Zone(),
                new[] { land, new IslandNode(EntityId.Parse("site:collision"), 0, 0,
                    IslandNodeKind.Salvage), exit }, path));
            Assert.Throws<ArgumentException>(() => IslandLayout.Create(Island, Zone(),
                new[] { land, exit }, path));
            Assert.Throws<ArgumentException>(() => IslandLayout.Create(Island, Zone(),
                new[] { land, a, exit }, new[]
                {
                    new IslandEdge(land.Id, a.Id, isSafe: true),
                    new IslandEdge(a.Id, exit.Id, isSafe: false)
                }));
            Assert.Throws<ArgumentException>(() => IslandLayout.Create(Island, Zone(),
                new[] { land, a, exit }, new[] { path[0], path[0], path[1] }));
        }

        [Test]
        public void InvalidProfilesAndUnboundedRequestsCannotBeMaterialized()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldZoneProfile(
                EntityId.Parse("zone:bad"), ThermalZoneBand.PackIce, -35000, 1001, 200));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldZoneProfile(
                EntityId.Parse("zone:bad"), ThermalZoneBand.Coastal, -200000, 1, 200));
            Assert.Throws<ArgumentException>(() => new WorldZoneProfile(
                default, ThermalZoneBand.Coastal, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                IslandGenerator.Generate(Island, 1, Zone(), nodeCount: 3));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                IslandGenerator.Generate(Island, 1, Zone(), nodeCount: 65));
            Assert.Throws<ArgumentException>(() =>
                IslandGenerator.Generate(default, 1, Zone(), nodeCount: 8));
        }
    }
}
