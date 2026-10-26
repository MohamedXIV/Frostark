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
    /// Regression coverage for the observed playtest failures: direct shore
    /// orders, multi-crew gangway queueing, and recovery from partial transit.
    /// </summary>
    public sealed class PersistentWorldMultiCrewTransitPlayModeTests
    {
        private static readonly EntityId Captain =
            EntityId.Parse(PersistentWorldRuntime.CaptainId);
        private static readonly EntityId Scout =
            EntityId.Parse(PersistentWorldRuntime.ScoutId);

        private WorldSession _session;

        [UnityTest]
        public IEnumerator MultiSelectGangway_CrossesAndReturnsAllSelectedCrew()
        {
            PersistentWorldRuntime runtime = null;
            try
            {
                yield return LoadWorld(r => runtime = r);
                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var captainObject = captain.gameObject;
                var scoutObject = scout.gameObject;
                var commander = runtime.Commander;
                var gangway = runtime.GetComponentInChildren<PersistentGangway>(true);
                Assert.That(gangway, Is.Not.Null);
                SelectBoth(commander);
                Physics.SyncTransforms();

                var gangwayScreen = runtime.GameplayCamera.WorldToScreenPoint(
                    gangway.transform.position);
                Assert.That(commander.ProcessContextClick(gangwayScreen), Is.True,
                    "Multi-select must be accepted at the gangway.");

                yield return null;
                Assert.That(captain.ShipMotor.IsMoving, Is.True,
                    "The first selected actor must start moving immediately.");
                Assert.That(scout.ShipMotor.IsMoving, Is.True,
                    "The next selected actor must start moving immediately to the gangway queue.");
                var firstQueueCell = new ShipGridCell(
                    EntityId.Parse(PersistentWorldRuntime.EngineRoomId), 4, 2);
                Assert.That(scout.ShipMotor.OrderedCell, Is.EqualTo(firstQueueCell),
                    "The crew member nearest the gangway must get the first queue slot, regardless of selection order.");
                Assert.That(captain.ShipMotor.OrderedCell, Is.Not.EqualTo(firstQueueCell));

                yield return WaitUntil(() => !runtime.IsCrossing &&
                    !captain.IsOnShip && !scout.IsOnShip, 35f,
                    "Both selected crew must cross the gangway.");
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(scout.gameObject, Is.SameAs(scoutObject));
                Assert.That(runtime.Session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(runtime.Session.SceneCheckpoint.PartyIds,
                    Is.EquivalentTo(new[] { Captain, Scout }),
                    "The shore party must be the union of every away actor.");
                Assert.That(Vector3.Distance(captain.transform.position,
                        scout.transform.position), Is.GreaterThan(0.5f),
                    "Actors must land on distinct shore exits instead of stacking.");

                SelectBoth(commander);
                Physics.SyncTransforms();
                var shipFloor = runtime.GameplayCamera.WorldToScreenPoint(
                    new Vector3(8.5f, 0f, 2.5f));
                Assert.That(commander.ProcessContextClick(shipFloor), Is.True,
                    "Direct shore-to-ship order must be accepted.");

                yield return WaitUntil(() => !runtime.IsCrossing &&
                    captain.IsOnShip && scout.IsOnShip, 35f,
                    "Both selected crew must return through the gangway.");
                Assert.That(captain.gameObject, Is.SameAs(captainObject));
                Assert.That(scout.gameObject, Is.SameAs(scoutObject));
                Assert.That(runtime.Session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(runtime.Session.SceneCheckpoint.PartyIds, Is.Empty);
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
            }
            finally
            {
                Cleanup(runtime);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator DirectShoreClick_CrossesAndMovesAllSelectedCrew()
        {
            PersistentWorldRuntime runtime = null;
            try
            {
                yield return LoadWorld(r => runtime = r);
                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var commander = runtime.Commander;
                SelectBoth(commander);
                Physics.SyncTransforms();

                var shorePoint = new Vector3(25f, 0f, 6f);
                var shoreScreen = runtime.GameplayCamera.WorldToScreenPoint(shorePoint);
                Assert.That(commander.ProcessContextClick(shoreScreen), Is.True,
                    "A shore click from the ship must command a crossing.");

                yield return null;
                Assert.That(captain.ShipMotor.IsMoving, Is.True,
                    "The first selected actor must start the crossing route immediately.");
                Assert.That(scout.ShipMotor.IsMoving, Is.True,
                    "The next selected actor must start staging immediately.");

                yield return WaitUntil(() => !runtime.IsCrossing &&
                    !captain.IsOnShip && !scout.IsOnShip, 35f,
                    "Both selected crew must cross before the shore move.");
                Assert.That(runtime.Session.SceneCheckpoint.PartyIds,
                    Is.EquivalentTo(new[] { Captain, Scout }));
                Assert.That(Vector3.Distance(captain.transform.position,
                        scout.transform.position), Is.GreaterThan(0.5f),
                    "Direct shore travel must not stack actors at the gangway mouth.");

                yield return WaitUntil(() =>
                    Vector3.Distance(captain.transform.position, shorePoint) < 4.5f &&
                    Vector3.Distance(scout.transform.position, shorePoint) < 4.5f,
                    10f, "Both crew must move away from the gangway mouth toward the clicked shore point.");
            }
            finally
            {
                Cleanup(runtime);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Reconcile_RepairsPartialAwayActorAndParty()
        {
            PersistentWorldRuntime runtime = null;
            try
            {
                yield return LoadWorld(r => runtime = r);
                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var commander = runtime.Commander;

                commander.SelectActor(Captain);
                Assert.That(runtime.TryCrossActor(Captain,
                    EntityId.Parse("transition:reconcile:captain")), Is.True);
                yield return WaitUntil(() => !runtime.IsCrossing && !captain.IsOnShip,
                    20f, "Captain must reach shore.");

                var transfer = ShoreCrewTransfer.Transfer(runtime.Session,
                    new[] { Scout }, true,
                    EntityId.Parse("transition:reconcile:scout"));
                Assert.That(transfer.Success, Is.True);
                var checkpoint = runtime.Session.SceneCheckpoint;
                Assert.That(checkpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(checkpoint.PartyIds, Is.EquivalentTo(new[] { Captain }),
                    "Simulate the old overwrite/partial state.");

                scout.ShipMotor.SuspendForShoreDeparture();
                scout.ShipMotor.enabled = true;
                scout.ShoreAgent.enabled = false;
                Assert.That(scout.IsOnShip, Is.True);

                runtime.ReconcilePresenceNow();

                Assert.That(scout.IsOnShip, Is.False,
                    "Away actor must not retain the ship motor mode.");
                Assert.That(scout.ShipMotor.enabled, Is.False);
                Assert.That(scout.ShoreAgent.enabled, Is.True);
                Assert.That(runtime.Session.SceneCheckpoint.PartyIds,
                    Is.EquivalentTo(new[] { Captain, Scout }),
                    "Reconcile must repair the shore party union.");
            }
            finally
            {
                Cleanup(runtime);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator ReusedTransitionId_RollsBackToShoreAndAllowsFreshReturn()
        {
            PersistentWorldRuntime runtime = null;
            try
            {
                yield return LoadWorld(r => runtime = r);
                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var transition = EntityId.Parse("transition:reused:crossing");

                Assert.That(runtime.TryCrossActor(Captain, transition), Is.True);
                yield return WaitUntil(() => !runtime.IsCrossing && !captain.IsOnShip,
                    20f, "Captain must reach shore before the reused transfer.");

                Assert.That(runtime.TryCrossActor(Captain, transition), Is.True);
                yield return WaitUntil(() => !runtime.IsCrossing, 20f,
                    "The reused transition must finish with a rollback.");

                Assert.That(captain.IsOnShip, Is.False,
                    "A rejected return transfer must keep the actor on shore.");
                Assert.That(runtime.LastTransitStatus, Does.Contain(
                    "ShipTransferRejected"), runtime.LastTransitStatus);
                Assert.That(captain.ShoreAgent.enabled, Is.True);
                Assert.That(captain.ShipMotor.enabled, Is.False);
                Assert.That(runtime.Session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Shore),
                    "Rollback must restore the shore checkpoint.");
                Assert.That(runtime.Session.SceneCheckpoint.PartyIds,
                    Is.EquivalentTo(new[] { Captain }));

                Assert.That(runtime.TryCrossActor(Captain,
                    EntityId.Parse("transition:reused:return")), Is.True);
                yield return WaitUntil(() => !runtime.IsCrossing && captain.IsOnShip,
                    20f, "A fresh transition ID must return the actor cleanly.");
                Assert.That(runtime.Session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Ship));
            }
            finally
            {
                Cleanup(runtime);
            }
            yield return null;
        }

        private static void SelectBoth(TacticalCommandController commander)
        {
            commander.ClearSelection();
            commander.SelectActor(Captain);
            commander.SelectActor(Scout, addToSelection: true);
            Assert.That(commander.Selection.Count, Is.EqualTo(2));
        }

        private IEnumerator LoadWorld(
            System.Action<PersistentWorldRuntime> assign)
        {
            var root = new GameObject("E12-Multi-Crew-Transit");
            _session = new WorldSession(
                PersistentWorldRuntime.CreateDefaultState(withActivities: false));
            var runtime = root.AddComponent<PersistentWorldRuntime>();
            runtime.BuildWorld(_session);
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
            assign(runtime);
        }

        private static IEnumerator WaitUntil(System.Func<bool> condition,
            float timeoutSeconds, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private static void Cleanup(PersistentWorldRuntime runtime)
        {
            if (runtime != null)
                Object.Destroy(runtime.transform.root.gameObject);
        }

        [UnityTearDown]
        public IEnumerator CleanupMultiCrewTransit()
        {
            var runtimes = Object.FindObjectsByType<PersistentWorldRuntime>(
                FindObjectsInactive.Include);
            foreach (var runtime in runtimes)
                if (runtime != null)
                    Object.Destroy(runtime.transform.root.gameObject);
            yield return null;
            _session?.Dispose();
            _session = null;

            var shore = SceneManager.GetSceneByName(
                PersistentWorldRuntime.ShoreSceneName);
            if (shore.IsValid() && shore.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(shore);
                if (unload != null)
                    yield return unload;
            }

            var reset = SceneManager.LoadSceneAsync("SampleScene",
                LoadSceneMode.Single);
            if (reset != null)
                yield return reset;
        }
    }
}
