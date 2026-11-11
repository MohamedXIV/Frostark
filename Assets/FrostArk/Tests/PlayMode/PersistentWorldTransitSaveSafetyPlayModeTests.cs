using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Save/Load and restore must be blocked for the whole multi-actor transit
    /// queue, including staging frames where no actor is crossing yet.
    /// </summary>
    public sealed class PersistentWorldTransitSaveSafetyPlayModeTests
    {
        private static readonly EntityId Captain =
            EntityId.Parse(PersistentWorldRuntime.CaptainId);
        private static readonly EntityId Scout =
            EntityId.Parse(PersistentWorldRuntime.ScoutId);

        [UnityTest]
        public IEnumerator StagingTransitRejectsSaveLoadAndRestoreWithoutMutatingWorld()
        {
            var root = new GameObject("E12-Transit-Save-Safety");
            WorldSession session = null;
            try
            {
                session = new WorldSession(
                    PersistentWorldRuntime.CreateDefaultState(withActivities: false));
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
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

                var coordinator = root.GetComponent<PersistentWorldSaveCoordinator>();
                Assert.That(coordinator, Is.Not.Null);
                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var captainObject = captain.gameObject;
                var scoutObject = scout.gameObject;
                var checkpoint = session.SceneCheckpoint;
                var revision = session.Revision;
                var preTransit = runtime.CaptureSnapshot();

                Assert.That(runtime.TryCrossSelected(new[] { Captain, Scout },
                    EntityId.Parse("transition:safety:staging")), Is.True);
                Assert.That(runtime.IsCrossing, Is.False,
                    "The queue must still be staging before its first crossing.");
                Assert.That(coordinator.QuickSave(), Is.False,
                    "Save must be rejected during gangway staging.");
                Assert.That(coordinator.LastStatus, Does.Contain("transit"),
                    coordinator.LastStatus);
                Assert.That(coordinator.QuickLoad(), Is.False,
                    "Load must be rejected during gangway staging.");
                Assert.That(coordinator.LastStatus, Does.Contain("transit"),
                    coordinator.LastStatus);
                Assert.That(runtime.RestoreFromSnapshot(preTransit), Is.False,
                    "A direct restore must be rejected during transit staging.");
                Assert.That(runtime.LastRestoreError, Does.Contain("transit"),
                    runtime.LastRestoreError);

                yield return WaitUntil(() => !runtime.IsCrossing &&
                    runtime.LastTransitStatus.StartsWith("TransitComplete") &&
                    !captain.IsOnShip && !scout.IsOnShip, 45f,
                    "The original transit must complete without a restore mutation.");

                Assert.That(runtime.Session, Is.SameAs(session));
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(scout.gameObject, Is.SameAs(scoutObject));
                Assert.That(session.Revision, Is.GreaterThan(revision));
                Assert.That(session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(session.SceneCheckpoint.PartyIds,
                    Is.EquivalentTo(new[] { Captain, Scout }));
                Assert.That(checkpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));

                Assert.That(coordinator.QuickSave(), Is.True,
                    "Save must be allowed once the queue has fully completed.");
            }
            finally
            {
                if (root != null)
                    Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition,
            float timeoutSeconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, message);
        }

        [UnityTearDown]
        public IEnumerator CleanupTransitSaveSafety()
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
