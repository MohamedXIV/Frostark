using System;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Requires Unity EditMode: this suite intentionally does not count as
    /// passed merely because the standalone Core .NET CI is green.
    /// </summary>
    public sealed class ShoreSceneTransitEditModeTests
    {
        private static readonly EntityId Scene = EntityId.Parse("scene:landing");
        private static readonly EntityId Site = EntityId.Parse("island:floe");
        private static readonly EntityId Anchor = EntityId.Parse("anchor:shore");
        private static readonly EntityId Hero = EntityId.Parse("actor:hero");

        [Test]
        public void TypedDestinationAndAnchorUseStableIdentifiers()
        {
            var destination = new SceneTransitDestination();
            destination.Configure(PhysicalTheater.Shore, "Assets/FrostArk/Scenes/Shore.unity",
                Scene, Anchor, Site);
            Assert.That(destination.IsConfigured, Is.True);
            var checkpoint = destination.MakeCheckpoint(new[] { Hero });
            Assert.That(destination.Matches(checkpoint), Is.True);
            Assert.That(checkpoint.SceneId, Is.EqualTo(Scene));
            Assert.That(checkpoint.SiteId, Is.EqualTo(Site));

            var marker = new GameObject("G2-ShoreAnchor");
            try
            {
                var anchor = marker.AddComponent<SceneReentryAnchor>();
                anchor.Configure(Scene, Anchor);
                Assert.That(anchor.Matches(Scene, Anchor), Is.True);
                Assert.That(anchor.Matches(Scene, EntityId.Parse("anchor:wrong")), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(marker);
            }
        }

        [Test]
        public void UnconfiguredSceneAndInvalidTheaterCannotBeUsedAsDestination()
        {
            var empty = new SceneTransitDestination();
            Assert.That(empty.IsConfigured, Is.False);
            Assert.Throws<InvalidOperationException>(() =>
                empty.MakeCheckpoint(new[] { Hero }));
            Assert.Throws<ArgumentException>(() =>
                empty.Configure(PhysicalTheater.Shore, "Assets/Scenes/A.unity",
                    Scene, Anchor));
        }

        [Test]
        public void TacticalFixtureCrewHasCanonicalDutyRecordsForShoreCommands()
        {
            using (var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState()))
            {
                var actor = EntityId.Parse("actor:protagonist");
                Assert.That(session.Capture().State.CrewDuty.Get(actor), Is.Not.Null);
                var transfer = ShoreCrewTransfer.Transfer(session, new[] { actor }, true,
                    EntityId.Parse("transition:g2-fixture"));
                Assert.That(transfer.Success, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(actor).IsAway, Is.True);
            }
        }

        [Test]
        public void ScopeCanBeConfiguredFromV13CheckpointBeforeContainerBuild()
        {
            var state = WorldState.Create(new WorldMinute(22),
                new[] { new SupplyStock(EntityId.Parse("supply:fuel"), 8) },
                new[] { Hero });
            byte[] save;
            using (var session = new WorldSession(state))
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:ship"), EntityId.Parse("anchor:bridge")));
                save = SaveCodec.Encode(session.Capture());
            }
            var decoded = SaveCodec.TryDecode(save);
            Assert.That(decoded.Success, Is.True);

            var root = new GameObject("G2-RestoredRunScope");
            root.SetActive(false);
            WorldSessionScope scope = null;
            WorldSession restored = null;
            try
            {
                scope = root.AddComponent<WorldSessionScope>();
                scope.SetRestoreSnapshot(decoded.Value);
                root.SetActive(true);
                if (scope.Container == null)
                    scope.Build();
                restored = scope.Container.Resolve<WorldSession>();
                Assert.That(restored.Minute.Value, Is.EqualTo(22));
                Assert.That(restored.Capture().State.GetSupply(EntityId.Parse("supply:fuel")),
                    Is.EqualTo(8));
                Assert.That(restored.SceneCheckpoint.SceneId,
                    Is.EqualTo(EntityId.Parse("scene:ship")));
            }
            finally
            {
                if (scope != null)
                    scope.DisposeCore();
                UnityEngine.Object.DestroyImmediate(root);
            }
            if (restored != null)
                Assert.Throws<ObjectDisposedException>(() => restored.Read());
        }
    }
}
