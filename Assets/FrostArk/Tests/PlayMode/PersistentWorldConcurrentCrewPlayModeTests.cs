using System;
using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
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
    /// Real PlayMode proof that an away protagonist does not stop onboard
    /// physical Work, Rest, or real water consumption under the one session.
    /// </summary>
    public sealed class PersistentWorldConcurrentCrewPlayModeTests
    {
        private static readonly EntityId Captain =
            EntityId.Parse(PersistentWorldRuntime.CaptainId);
        private static readonly EntityId Engineer =
            EntityId.Parse(PersistentWorldRuntime.EngineerId);
        private static readonly EntityId Medic =
            EntityId.Parse(PersistentWorldRuntime.MedicId);
        private static readonly EntityId Scout =
            EntityId.Parse(PersistentWorldRuntime.ScoutId);
        private static readonly EntityId RepairJobId =
            EntityId.Parse(PersistentWorldRuntime.RepairJobId);
        private static readonly EntityId Water =
            EntityId.Parse(PersistentWorldRuntime.WaterItemId);

        [UnityTest]
        public IEnumerator OnboardCrewWorkRestAndConsumeWhileCaptainExploresShore()
        {
            var root = new GameObject("E12-Concurrent-Crew");
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
                yield return null;
                yield return null;

                float wait = 8f;
                while (!runtime.IsReady || !runtime.IsShoreReady)
                {
                    if (wait <= 0f)
                        Assert.Fail("Persistent world did not initialize: " +
                            runtime.InitializationError + " / " + runtime.ShoreError);
                    wait -= Time.deltaTime;
                    yield return null;
                }

                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var engineer = runtime.CrewActors.First(a => a.ActorId == Engineer);
                var medic = runtime.CrewActors.First(a => a.ActorId == Medic);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var commander = runtime.Commander;
                var duty = commander.GetComponent<ShipCrewDutyAutonomy>();
                var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
                Assert.That(duty, Is.Not.Null);
                Assert.That(agency, Is.Not.Null);
                Assert.That(captain.IsOnShip, Is.False);
                Assert.That(session.Capture().State.CrewDuty.Get(Captain).IsAway, Is.True);

                // The protagonist physically explores the shore while the rest
                // of the crew remains in the one resident ship simulation.
                commander.SelectActor(Captain);
                Physics.SyncTransforms();
                var shoreDestination = new Vector3(25f, 0f, 6f);
                var shoreScreen = runtime.GameplayCamera.WorldToScreenPoint(
                    shoreDestination);
                Assert.That(commander.ProcessContextClick(shoreScreen), Is.True,
                    "The away protagonist must accept a real shore move.");
                var captainStart = captain.transform.position;
                var exploreDeadline = Time.realtimeSinceStartup + 3f;
                while (Vector3.Distance(captainStart, captain.transform.position) < 0.2f &&
                       Time.realtimeSinceStartup < exploreDeadline)
                    yield return null;
                Assert.That(Vector3.Distance(captainStart, captain.transform.position),
                    Is.GreaterThan(0.2f), "The protagonist must move on shore.");

                var readyDeadline = Time.realtimeSinceStartup + 25f;
                while (!OnboardActivitiesReady(duty, agency, session, engineer, medic) &&
                       Time.realtimeSinceStartup < readyDeadline)
                    yield return null;

                Assert.That(duty.ActiveVerb(Engineer),
                    Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(duty.IsStationed(Engineer), Is.True,
                    "Onboard repairer must reach the authored Work slot.");
                Assert.That(duty.ActiveVerb(Medic),
                    Is.EqualTo(CrewInteractionVerb.Rest));
                Assert.That(duty.IsStationed(Medic), Is.True,
                    "Onboard resting crew must reach the authored Rest slot.");
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1),
                    "Onboard thirsty crew must consume exactly one real water item.");
                Assert.That(session.Capture().State.CrewDuty.Get(Scout).Thirst,
                    Is.EqualTo(200));

                yield return null; // Let the duty adapter attest physical presence.
                duty.Tick();
                agency.Tick();

                long repairBefore = session.Capture().State.CrewDuty
                    .GetRepair(RepairJobId).RemainingWork;
                int fatigueBefore = session.Capture().State.CrewDuty.Get(Medic).Fatigue;
                long waterBefore = session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water);

                session.Advance(1);
                yield return null;

                var after = session.Capture().State;
                Assert.That(after.CrewDuty.GetRepair(RepairJobId).RemainingWork,
                    Is.EqualTo(repairBefore - 9),
                    "The stationed repairer must produce legitimate Work while ashore.");
                Assert.That(after.CrewDuty.Get(Medic).Fatigue,
                    Is.EqualTo(fatigueBefore - 2),
                    "The stationed resting crew must recover through the world clock.");
                Assert.That(after.Inventory.GetAmount(InventoryContainer.ShipHold, Water),
                    Is.EqualTo(waterBefore),
                    "Physical rest/work must not duplicate or phantom-consume water.");
                Assert.That(after.CrewDuty.Get(Captain).IsAway, Is.True);
                Assert.That(session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(captain.gameObject, Is.SameAs(
                    runtime.CrewActors.First(a => a.ActorId == Captain).gameObject));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        private static bool OnboardActivitiesReady(
            ShipCrewDutyAutonomy duty, ShipCrewAgencyCoordinator agency,
            WorldSession session, PersistentCrewActor engineer,
            PersistentCrewActor medic)
        {
            if (duty.ActiveVerb(engineer.ActorId) != CrewInteractionVerb.Work ||
                !duty.IsStationed(engineer.ActorId) ||
                duty.ActiveVerb(medic.ActorId) != CrewInteractionVerb.Rest ||
                !duty.IsStationed(medic.ActorId) ||
                agency.HasPendingProvision(Scout))
                return false;
            return session.Capture().State.Inventory.GetAmount(
                InventoryContainer.ShipHold, Water) == 1;
        }

        [UnityTearDown]
        public IEnumerator CleanupConcurrentCrewWorld()
        {
            var runtimes = UnityEngine.Object.FindObjectsByType<PersistentWorldRuntime>(
                FindObjectsInactive.Include);
            foreach (var runtime in runtimes)
                if (runtime != null)
                    UnityEngine.Object.Destroy(runtime.transform.root.gameObject);
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
