using System;
using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Requires the explicit Editor fixture generator first:
    /// FrostArk/Fixtures/Rebuild Ship-Shore Transit Scenes.
    /// Only a real Unity PlayMode run can establish these G3 results.
    /// </summary>
    public sealed class ShoreSceneTransitPlayModeTests
    {
        private const string Launcher = "Assets/FrostArk/Scenes/ShipShoreTransitLauncher.unity";
        private const string ShorePath = "Assets/FrostArk/Scenes/ShoreLandingEnvironment.unity";
        private const string ShipPath = "Assets/FrostArk/Scenes/ShipDeckEnvironment.unity";
        private static readonly EntityId Hero = EntityId.Parse("actor:protagonist");
        private static readonly EntityId Companion = EntityId.Parse("actor:companion");

        private static SceneTransitDestination ShoreDestination()
        {
            var result = new SceneTransitDestination();
            result.Configure(PhysicalTheater.Shore, ShorePath,
                EntityId.Parse("scene:shore:landing"), EntityId.Parse("anchor:shore:entry"),
                EntityId.Parse("island:training-floe"));
            return result;
        }

        private static SceneTransitDestination ShipDestination()
        {
            var result = new SceneTransitDestination();
            result.Configure(PhysicalTheater.Ship, ShipPath,
                EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:ship:gangway"));
            return result;
        }

        [UnityTest]
        public IEnumerator RealAdditiveShoreRoundTripPreservesTheSameSessionAndWorldLoot()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "Generate the explicit ship/shore fixture scenes in Unity first.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null; // Awake, VContainer injection and run-root move to DDOL.

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null);
            var session = root.Container.Resolve<WorldSession>();
            var commander = UnityEngine.Object.FindFirstObjectByType<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();
            Assert.That(commander, Is.Not.Null);
            Assert.That(transit, Is.Not.Null);

            try
            {
                commander.SelectActor(Hero);
                commander.SelectActor(Companion, addToSelection: true);
                Assert.That(commander.Selection.Count, Is.EqualTo(2));
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:playmode:depart")), Is.True);

                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False, "Actual additive load/transition timed out.");
                Assert.That(transit.LastError, Is.Null, "Transit error: " + transit.LastError);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(root.Container.Resolve<WorldSession>(), Is.SameAs(session));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.True);
                Assert.That(commander.Selection.Count, Is.Zero,
                    "Presentation selection is not persisted across scenes.");

                var proxies = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None);
                Assert.That(proxies.Select(x => x.ActorId).Distinct().Count(), Is.EqualTo(2),
                    "The scene must not spawn duplicate persistent crew proxies.");

                var pickup = UnityEngine.Object.FindFirstObjectByType<TacticalInteractableProxy>();
                Assert.That(pickup, Is.Not.Null);
                commander.SelectActor(Hero);
                Assert.That(commander.IssueInteractOrder(pickup), Is.True);

                timeout = 9f;
                while (!pickup.IsDepleted && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(pickup.IsDepleted, Is.True, "Real NavMesh approach/loot did not complete.");
                var amountBefore = session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.Pack, EntityId.Parse("item:scrap_metal"));
                Assert.That(amountBefore, Is.EqualTo(5));

                var savedAshore = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
                Assert.That(savedAshore.Success, Is.True);
                Assert.That(savedAshore.Value.SceneCheckpoint.SiteId,
                    Is.EqualTo(EntityId.Parse("island:training-floe")));

                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:playmode:return")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShipPath));
                Assert.That(root.Container.Resolve<WorldSession>(), Is.SameAs(session));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.False);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")),
                    Is.EqualTo(amountBefore));
                Assert.That(UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).Select(x => x.ActorId).Distinct().Count(),
                    Is.EqualTo(2));

                // Second round-trip from the now-resident Ship scene: the ship
                // scene and its hierarchy must remain resident across Shore
                // departure and return without being destroyed or recreated.
                var shipScene = SceneManager.GetActiveScene();
                var shipEnvRoot = shipScene.GetRootGameObjects()
                    .First(go => go.name == "ShipDeckEnvironment");

                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:playmode:depart-2")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(shipScene.isLoaded, Is.True,
                    "Persistent ship scene must remain resident while landing party explores shore.");
                Assert.That(shipEnvRoot != null && !shipEnvRoot.Equals(null),
                    Is.True, "Ship root GameObject must not be destroyed on shore departure.");

                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:playmode:return-2")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShipPath));
                var activeShipRoot = SceneManager.GetActiveScene().GetRootGameObjects()
                    .FirstOrDefault(go => go.name == "ShipDeckEnvironment");
                Assert.That(activeShipRoot, Is.SameAs(shipEnvRoot),
                    "Returning from shore must reuse the same persistent ship root.");
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.Destroy(root.gameObject);
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    foreach (var go in activeScene.GetRootGameObjects())
                    {
                        if (go.name == "Code-based tests runner" || go.name.Contains("tests runner"))
                            continue;
                        UnityEngine.Object.Destroy(go);
                    }
                }
            }
        }

        [UnityTest]
        public IEnumerator MissingBuildSceneFailsBeforeCrewCommit()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            var session = root.Container.Resolve<WorldSession>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();
            try
            {
                var bad = new SceneTransitDestination();
                bad.Configure(PhysicalTheater.Shore, "Assets/FrostArk/Scenes/DOES_NOT_EXIST.unity",
                    EntityId.Parse("scene:wrong"), EntityId.Parse("anchor:wrong"),
                    EntityId.Parse("island:wrong"));

                long previousRevision = session.Revision;
                Assert.That(transit.TryDepart(bad, new[] { Hero },
                    EntityId.Parse("transition:bad")), Is.False);
                Assert.That(transit.LastError, Is.EqualTo("SceneNotInBuildSettings"));
                Assert.That(session.Revision, Is.EqualTo(previousRevision));
                Assert.That(session.SceneCheckpoint, Is.Null);
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.False);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.Destroy(root.gameObject);
                var activeScene = SceneManager.GetActiveScene();
                if (activeScene.IsValid())
                {
                    foreach (var go in activeScene.GetRootGameObjects())
                    {
                        if (go.name == "Code-based tests runner" || go.name.Contains("tests runner"))
                            continue;
                        UnityEngine.Object.Destroy(go);
                    }
                }
            }
        }
    }
    [SetUpFixture]
    public class PlayModeDeterministicFrameSetup
    {
        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Time.captureFramerate = 60;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Time.captureFramerate = 0;
        }
    }
}
