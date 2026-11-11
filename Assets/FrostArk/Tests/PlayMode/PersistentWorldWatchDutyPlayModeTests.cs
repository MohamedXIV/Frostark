using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// The Scout's Watch duty must be a persistent physical station, not only a
    /// one-shot need response. Moving the actor away and deselecting it must
    /// re-offer the authored Watch slot.
    /// </summary>
    public sealed class PersistentWorldWatchDutyPlayModeTests
    {
        private static readonly EntityId Scout =
            EntityId.Parse(PersistentWorldRuntime.ScoutId);
        private static readonly EntityId BridgeConsole =
            EntityId.Parse(PersistentWorldRuntime.BridgeConsoleId);
        private static readonly EntityId WatchSlot =
            EntityId.Parse(PersistentWorldRuntime.WatchWorkSlotId);
        private static readonly EntityId Water =
            EntityId.Parse(PersistentWorldRuntime.WaterItemId);

        [UnityTest]
        public IEnumerator ScoutReturnsToWatchAfterPlayerMoveAndDeselect()
        {
            var root = new GameObject("E12-Watch-Duty");
            WorldSession session = null;
            try
            {
                session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
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

                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var commander = runtime.Commander;
                var duty = commander.GetComponent<ShipCrewDutyAutonomy>();
                Assert.That(duty, Is.Not.Null);

                yield return WaitUntil(() =>
                    duty.ActiveVerb(Scout) == CrewInteractionVerb.Work &&
                    duty.IsStationed(Scout), 30f,
                    "Scout must finish drinking and take the authored Watch station.");
                var world = session.Capture().State;
                Assert.That(world.GetCrewStation(Scout), Is.EqualTo(BridgeConsole));
                var claim = world.CrewDuty.Interactions.ClaimForCrew(Scout);
                Assert.That(claim, Is.Not.Null);
                Assert.That(claim.SlotId, Is.EqualTo(WatchSlot));
                Assert.That(world.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1));
                Assert.That(world.CrewDuty.Get(Scout).Thirst, Is.EqualTo(200));

                commander.SelectActor(Scout);
                Physics.SyncTransforms();
                var awayPoint = new Vector3(4.5f, 0f, 1.5f);
                Assert.That(commander.ProcessContextClick(
                    runtime.GameplayCamera.WorldToScreenPoint(awayPoint)), Is.True);
                yield return WaitUntil(() => !scout.ShipMotor.IsMoving, 10f,
                    "The player Move must complete away from the Watch station.");
                commander.ClearSelection();
                yield return null;

                yield return WaitUntil(() =>
                    duty.ActiveVerb(Scout) == CrewInteractionVerb.Work &&
                    duty.IsStationed(Scout), 20f,
                    "The Scout must return to the Watch station after deselection.");
                var returned = session.Capture().State
                    .CrewDuty.Interactions.ClaimForCrew(Scout);
                Assert.That(returned, Is.Not.Null);
                Assert.That(returned.SlotId, Is.EqualTo(WatchSlot));
                Assert.That(duty.ActiveVerb(Scout),
                    Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(duty.IsStationed(Scout), Is.True);

                Object.Destroy(root);
                session.Dispose();
                session = null;
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
        public IEnumerator CleanupWatchDutyWorld()
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
