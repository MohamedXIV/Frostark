using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Failed restores must be rejected before mutating the one session or
    /// destroying the current physical world. Exact saved positions are never
    /// replaced with an entry-point fallback.
    /// </summary>
    public sealed class PersistentWorldRestoreSafetyPlayModeTests
    {
        private static readonly EntityId Captain =
            EntityId.Parse(PersistentWorldRuntime.CaptainId);
        private static readonly EntityId Bridge =
            EntityId.Parse(PersistentWorldRuntime.BridgeRoomId);

        [UnityTest]
        public IEnumerator InvalidShipPlacementIsRejectedBeforeWorldMutation()
        {
            var root = new GameObject("E12-Restore-Invalid-Ship");
            WorldSession session = null;
            try
            {
                session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                yield return WaitReady(runtime);

                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var captainObject = captain.gameObject;
                var revision = session.Revision;
                var checkpoint = session.SceneCheckpoint;
                var invalid = session.Capture(new[]
                {
                    PhysicalActorLocation.Aboard(Captain,
                        new ShipGridCell(Bridge, 99, 99))
                });

                Assert.That(runtime.RestoreFromSnapshot(invalid), Is.False);
                Assert.That(runtime.IsRestoring, Is.False);
                Assert.That(runtime.LastRestoreError, Does.Contain(
                    "SavedShipPlacementInvalid"), runtime.LastRestoreError);
                Assert.That(runtime.Session, Is.SameAs(session));
                Assert.That(runtime.IsReady, Is.True);
                Assert.That(session.Revision, Is.EqualTo(revision));
                Assert.That(session.SceneCheckpoint, Is.SameAs(checkpoint));
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
            }
            finally
            {
                if (root != null)
                    Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator InvalidShorePlacementIsRejectedWithoutEntryFallback()
        {
            var root = new GameObject("E12-Restore-Invalid-Shore");
            WorldSession session = null;
            try
            {
                session = new WorldSession(
                    PersistentWorldRuntime.CreateDefaultState(captainAway: true));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    EntityId.Parse(PersistentWorldRuntime.ShoreSceneId),
                    EntityId.Parse(PersistentWorldRuntime.ShoreSiteId),
                    EntityId.Parse(PersistentWorldRuntime.ShoreAnchorId),
                    new[] { Captain }));
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                yield return WaitReady(runtime);

                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var captainObject = captain.gameObject;
                var revision = session.Revision;
                var checkpoint = session.SceneCheckpoint;
                var invalid = session.Capture(new[]
                {
                    PhysicalActorLocation.Ashore(Captain, 500f, 500f)
                });

                Assert.That(runtime.RestoreFromSnapshot(invalid), Is.False);
                Assert.That(runtime.IsRestoring, Is.False);
                Assert.That(runtime.LastRestoreError, Does.Contain(
                    "SavedShorePlacementInvalid"), runtime.LastRestoreError);
                Assert.That(runtime.Session, Is.SameAs(session));
                Assert.That(runtime.IsReady, Is.True);
                Assert.That(session.Revision, Is.EqualTo(revision));
                Assert.That(session.SceneCheckpoint, Is.SameAs(checkpoint));
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
            }
            finally
            {
                if (root != null)
                    Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator UnloadedShoreEnvironmentIsRejectedBeforeWorldMutation()
        {
            var root = new GameObject("E12-Restore-Missing-Shore");
            WorldSession session = null;
            try
            {
                session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                yield return WaitReady(runtime);

                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var captainObject = captain.gameObject;
                var revision = session.Revision;
                var checkpoint = session.SceneCheckpoint;
                var valid = runtime.CaptureSnapshot();

                var unload = SceneManager.UnloadSceneAsync(
                    PersistentWorldRuntime.ShoreSceneName);
                if (unload != null)
                    yield return unload;
                yield return null;

                Assert.That(runtime.RestoreFromSnapshot(valid), Is.False);
                Assert.That(runtime.IsRestoring, Is.False);
                Assert.That(runtime.LastRestoreError, Does.Contain(
                    "ShoreSceneNotLoaded"), runtime.LastRestoreError);
                Assert.That(runtime.Session, Is.SameAs(session));
                Assert.That(runtime.IsReady, Is.True);
                Assert.That(session.Revision, Is.EqualTo(revision));
                Assert.That(session.SceneCheckpoint, Is.SameAs(checkpoint));
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
            }
            finally
            {
                if (root != null)
                    Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        private static IEnumerator WaitReady(PersistentWorldRuntime runtime)
        {
            yield return null;
            yield return null;
            float wait = 8f;
            while ((!runtime.IsReady || !runtime.IsShoreReady) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);
            Assert.That(runtime.IsShoreReady, Is.True, runtime.ShoreError);
        }

        [UnityTearDown]
        public IEnumerator CleanupRestoreSafety()
        {
            var runtimes = Object.FindObjectsByType<PersistentWorldRuntime>(
                FindObjectsInactive.Include);
            foreach (var runtime in runtimes)
                if (runtime != null)
                    Object.Destroy(runtime.transform.root.gameObject);
            yield return null;

            var shore = SceneManager.GetSceneByName(PersistentWorldRuntime.ShoreSceneName);
            if (shore.IsValid() && shore.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(shore);
                if (unload != null)
                    yield return unload;
            }

            var reset = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            if (reset != null)
                yield return reset;
        }
    }
}
