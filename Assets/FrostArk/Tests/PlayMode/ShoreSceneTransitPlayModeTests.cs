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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
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

                var pickup = UnityEngine.Object.FindObjectsByType<TacticalInteractableProxy>(
                    FindObjectsSortMode.None).First(p => p.TargetId == EntityId.Parse("salvage:supply_cache"));
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
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));
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
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));
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
    
        [UnityTest]
        public IEnumerator ShipResidency_ShipToShoreToShip_RetainsExactPhysicalShipAndOnboardCrewInstances()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();

            try
            {
                // Initial round trip establishing resident ship scene
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:residency:depart-1")), Is.True);
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);

                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:residency:return-1")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));

                // Capture exact resident ship root and crew instances
                var residentShipScene = SceneManager.GetActiveScene();
                var residentShipRoot = residentShipScene.GetRootGameObjects()
                    .First(go => go.name == "ShipDeckEnvironment");
                var companionActor = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).First(p => p.ActorId == Companion);
                var companionGo = companionActor.gameObject;
                var heroActor = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).First(p => p.ActorId == Hero);
                var heroShipGo = heroActor.gameObject;

                // Hero departs alone; Companion remains onboard
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:residency:depart-2")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));

                // Verify resident ship and onboard crew remain alive and identical
                Assert.That(residentShipScene.isLoaded, Is.True,
                    "Resident ship scene must remain loaded while ashore.");
                Assert.That(residentShipRoot != null && !residentShipRoot.Equals(null), Is.True,
                    "Ship root must not be destroyed while ashore.");
                Assert.That(companionGo != null && !companionGo.Equals(null), Is.True,
                    "Onboard crew GameObject must not be destroyed while ashore.");
                Assert.That(companionGo.activeInHierarchy, Is.True,
                    "Onboard crew must remain active on the persistent ship.");

                // Returning to ship reuses the exact same ship and onboard crew instances
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:residency:return-2")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));

                var activeShipRoot = SceneManager.GetActiveScene().GetRootGameObjects()
                    .FirstOrDefault(go => go.name == "ShipDeckEnvironment");
                Assert.That(activeShipRoot, Is.SameAs(residentShipRoot),
                    "Ship root must be the exact same instance across round-trips.");
                Assert.That(companionActor.gameObject, Is.SameAs(companionGo),
                    "Onboard crew GameObject must be the exact same instance.");
                Assert.That(heroShipGo.activeInHierarchy, Is.True,
                    "Returning crew GameObject must be reactivated on the persistent ship.");
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
        public IEnumerator ConcurrentSimulation_OnboardCrewSimulatesRealFramesWhileExpeditionIsAshore()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();

            try
            {
                // Establish resident ship scene
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:concurrent:depart-init")), Is.True);
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:concurrent:return-init")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));

                // Hero departs alone; Companion remains onboard
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:concurrent:depart")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

                // Verify partition: Hero is away, Companion is onboard
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);

                // Run real frames while ashore: simulation advances for onboard crew
                long revBefore = session.Revision;
                var advance = session.Advance(1);
                Assert.That(advance.AdvancedMinutes, Is.EqualTo(1));
                Assert.That(session.Minute.Value, Is.EqualTo(1));
                Assert.That(session.Revision, Is.EqualTo(revBefore), "Advancing time does not manufacture unearned world commands.");

                // Yield real frames for physics and NavMesh processing
                for (int frame = 0; frame < 20; frame++)
                    yield return null;

                // Hero remains away, Companion remains onboard without duplicate claims or phantom resources
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")), Is.Zero);

                // Return hero to ship
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:concurrent:return")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.False);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);
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
        public IEnumerator FailureRecovery_InvalidReturnOrConcurrentTransitDoesNotCorruptSessionOrCamera()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();

            try
            {
                // Depart to shore
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:failrecov:depart")), Is.True);
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                long stableRevision = session.Revision;

                // 1. Invalid return destination rejected safely
                var badReturn = new SceneTransitDestination();
                badReturn.Configure(PhysicalTheater.Ship, "Assets/FrostArk/Scenes/INVALID_SCENE.unity",
                    EntityId.Parse("scene:bad"), EntityId.Parse("anchor:bad"));

                Assert.That(transit.TryReturn(badReturn, EntityId.Parse("transition:failrecov:bad")), Is.False);
                Assert.That(transit.LastError, Is.EqualTo("SceneNotInBuildSettings"));
                Assert.That(session.Revision, Is.EqualTo(stableRevision),
                    "World revision must not advance on rejected transit.");
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore),
                    "Checkpoint must remain intact ashore.");
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath),
                    "Active scene must remain shore.");

                // Verify Camera and shore actors are intact
                var camera = Camera.main;
                Assert.That(camera, Is.Not.Null, "Main camera must remain functional after rejection.");
                var proxies = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(FindObjectsSortMode.None);
                Assert.That(proxies.Any(p => p.ActorId == Hero), Is.True,
                    "Shore actor proxy must remain active and functional.");

                // 2. Concurrent transit rejection while transit is busy
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:failrecov:valid-return")), Is.True);
                Assert.That(transit.IsBusy, Is.True);

                // Attempting a second transition while busy must fail safely
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:failrecov:concurrent")), Is.False);
                Assert.That(transit.LastError, Is.EqualTo("SceneTransitUnavailable"),
                    "Concurrent transit must be rejected with SceneTransitUnavailable.");

                // Await completion of original valid return
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
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
        public IEnumerator PlayMode_GangwayInteractable_ClickToCommandEmbarkAndDisembarkRoundTrip()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "Generate the explicit ship/shore fixture scenes in Unity first.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null);
            var session = root.Container.Resolve<WorldSession>();
            var commander = UnityEngine.Object.FindFirstObjectByType<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();
            Assert.That(commander, Is.Not.Null);
            Assert.That(transit, Is.Not.Null);

            try
            {
                // Create or find ship gangway interactable
                var shipGangway = UnityEngine.Object.FindFirstObjectByType<ShoreGangwayInteractable>();
                if (shipGangway == null)
                {
                    var gangwayObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    gangwayObj.name = "ShipGangwayInteractable";
                    gangwayObj.transform.position = new Vector3(0, 0.5f, -3f);
                    shipGangway = gangwayObj.AddComponent<ShoreGangwayInteractable>();
                    shipGangway.SetInteractionRange(3.5f);
                }
                shipGangway.Configure(transit, ShoreDestination(), GangwayTransitMode.DisembarkToShore);

                // Select Hero and issue interact order to the gangway
                commander.SelectActor(Hero);
                Assert.That(commander.Selection.Count, Is.EqualTo(1));
                Assert.That(commander.IssueInteractOrder(shipGangway), Is.True,
                    "Click-to-command interact order on ship gangway must be accepted.");

                // Await transit to shore
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False, "Transit to shore timed out.");
                Assert.That(transit.LastError, Is.Null, "Transit error: " + transit.LastError);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);

                // Now ashore: create or find shore return gangway interactable
                var shoreGangwayObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                shoreGangwayObj.name = "ShoreReturnGangwayInteractable";
                shoreGangwayObj.transform.position = new Vector3(0, 0.5f, -3f);
                var shoreGangway = shoreGangwayObj.AddComponent<ShoreGangwayInteractable>();
                shoreGangway.SetInteractionRange(3.5f);
                shoreGangway.Configure(transit, ShipDestination(), GangwayTransitMode.ReturnToShip);

                // Select Hero and issue return order to shore gangway
                commander.SelectActor(Hero);
                Assert.That(commander.IssueInteractOrder(shoreGangway), Is.True,
                    "Click-to-command interact order on shore return gangway must be accepted.");

                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False, "Return transit to ship timed out.");
                Assert.That(transit.LastError, Is.Null, "Return error: " + transit.LastError);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
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

        [UnityTest]
        public IEnumerator PlayMode_OnboardRepair_ProgressesLegitimatelyWhileExpeditionIsAshore_WithoutPhantomProductivity()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = UnityEngine.Object.FindFirstObjectByType<ShoreSceneTransitCoordinator>();

            try
            {
                // Establish resident ship scene
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:repair-regression:depart-init")), Is.True);
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:repair-regression:return-init")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));

                var hullJobId = EntityId.Parse("repair:hull");
                var repairBefore = session.Capture().State.CrewDuty.GetRepair(hullJobId);
                Assert.That(repairBefore, Is.Not.Null, "Fixture state must provide repair:hull job.");
                long initialRemaining = repairBefore.RemainingWork;
                Assert.That(initialRemaining, Is.EqualTo(500));

                // Hero departs alone; Companion remains onboard to work
                commander.SelectActor(Hero);
                Assert.That(transit.TryDepartSelected(ShoreDestination(),
                    EntityId.Parse("transition:repair-regression:depart")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

                // Verify partition: Hero is away ashore, Companion is onboard
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);

                // Advance simulation minute: onboard repairer produces legitimate work
                long revBefore = session.Revision;
                var advance = session.Advance(1);
                Assert.That(advance.AdvancedMinutes, Is.EqualTo(1));
                Assert.That(session.Revision, Is.EqualTo(revBefore),
                    "Advancing time must not manufacture unearned world commands.");

                // Legitimate repair progress occurred for onboard worker
                var repairAfter = session.Capture().State.CrewDuty.GetRepair(hullJobId);
                Assert.That(repairAfter, Is.Not.Null);
                Assert.That(repairAfter.RemainingWork, Is.LessThan(initialRemaining),
                    "Onboard repairer must make legitimate progress while expedition is ashore.");
                // Skill 850 / 100 = 8 work/minute
                Assert.That(repairAfter.RemainingWork, Is.EqualTo(initialRemaining - 8));

                // Assert absence of phantom productivity / duplicate resources for party ashore:
                var heroAshore = session.Capture().State.CrewDuty.Get(Hero);
                Assert.That(heroAshore.IsAway, Is.True);
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")), Is.Zero,
                    "Party ashore must not generate phantom resources.");

                // Yield real frames for physics and rendering
                for (int frame = 0; frame < 20; frame++)
                    yield return null;

                // Return Hero to ship
                Assert.That(transit.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:repair-regression:return")), Is.True);
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher).Or.EqualTo(ShipPath));

                // Verify onboard progress is retained upon return
                var repairOnReturn = session.Capture().State.CrewDuty.GetRepair(hullJobId);
                Assert.That(repairOnReturn.RemainingWork, Is.EqualTo(initialRemaining - 8));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.False);
                Assert.That(session.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);
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
        public IEnumerator PlayMode_FirstDepartureFromInitialLaunch_RetainsExactShipRootAndOnboardCrew()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "ShipShoreTransitLauncher.unity fixture must be loaded directly.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null, "WorldSessionScope must be present in ShipShoreTransitLauncher.");
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = root.GetComponent<ShoreSceneTransitCoordinator>();
            Assert.That(commander, Is.Not.Null);
            Assert.That(transit, Is.Not.Null);

            try
            {
                // Capture the exact initial ship scene, root, and crew upon FIRST launch
                var launchShipScene = SceneManager.GetActiveScene();
                Assert.That(launchShipScene.path, Is.EqualTo(Launcher),
                    "Active scene upon initial launch must be ShipShoreTransitLauncher.unity.");
                var launchShipRoot = launchShipScene.GetRootGameObjects()
                    .First(go => go.name == "ShipDeckEnvironment");
                var companionActor = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).First(p => p.ActorId == Companion);
                var companionGo = companionActor.gameObject;

                // Find the authored gangway in the fixture
                var shipGangway = UnityEngine.Object.FindAnyObjectByType<ShoreGangwayInteractable>();
                Assert.That(shipGangway, Is.Not.Null, "Playable fixture must author in-world gangway interactable.");
                shipGangway.Configure(transit, ShoreDestination(), GangwayTransitMode.DisembarkToShore);

                // Select Hero alone; Companion remains onboard
                commander.SelectActor(Hero);
                Assert.That(commander.IssueInteractOrder(shipGangway), Is.True,
                    "Click-to-command interact order on ship gangway must be accepted.");

                // Await FIRST transit to shore
                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

                // Verify original launch ship scene and root remain alive and active on FIRST departure
                Assert.That(launchShipScene.isLoaded, Is.True,
                    "Initial launch ship scene must remain loaded across the first shore departure.");
                Assert.That(launchShipRoot != null && !launchShipRoot.Equals(null), Is.True,
                    "Initial launch ship root must not be destroyed on first shore departure.");
                Assert.That(companionGo != null && !companionGo.Equals(null) && companionGo.activeInHierarchy, Is.True,
                    "Onboard crew GameObject must remain active on the original launch ship.");

                // Spatial crossing return: find UNMODIFIED authored return gangway in the shore fixture
                var returnGangway = UnityEngine.Object.FindObjectsByType<ShoreGangwayInteractable>(FindObjectsSortMode.None)
                    .FirstOrDefault(g => g.Mode == GangwayTransitMode.ReturnToShip);
                Assert.That(returnGangway, Is.Not.Null,
                    "Authored ShoreLandingEnvironment must contain ShoreReturnGangwayInteractable.");
                Assert.That(returnGangway.Destination.ScenePath, Is.EqualTo(ShipPath),
                    "Authored shore return gangway targets ShipDeckEnvironment.unity as authored.");

                // Order return via unmodified authored gangway
                commander.SelectActor(Hero);
                Assert.That(commander.IssueInteractOrder(returnGangway), Is.True,
                    "Click-to-command interact order on unmodified authored return gangway must be accepted.");

                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);

                // Never instantiate a second ship: ShipDeckEnvironment.unity must NOT be loaded
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Assert.That(SceneManager.GetSceneAt(i).path, Is.Not.EqualTo(ShipPath),
                        "Never instantiate a second ship scene (ShipDeckEnvironment.unity); must reuse resident launch ship.");
                }

                // Active scene is the original resident launch ship scene
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Launcher),
                    "Active scene upon return must be the original resident launch ship scene.");

                // Returning reuses the EXACT launch ship root
                var activeShipRoot = SceneManager.GetActiveScene().GetRootGameObjects()
                    .FirstOrDefault(go => go.name == "ShipDeckEnvironment");
                Assert.That(activeShipRoot, Is.SameAs(launchShipRoot),
                    "Returning from shore must return to the exact same initial launch ship root.");

                // Onboard crew instance identity preserved
                var activeCompanion = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(FindObjectsSortMode.None)
                    .FirstOrDefault(p => p.ActorId == Companion);
                Assert.That(activeCompanion, Is.Not.Null);
                Assert.That(activeCompanion.gameObject, Is.SameAs(companionGo),
                    "Returning to ship must preserve the exact onboard crew GameObject instance.");
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
        public IEnumerator PlayMode_PointerClickInputEvent_SelectsActor_EnablesDeparture_AndDisembarksSuccessfully()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "ShipShoreTransitLauncher.unity fixture must be loaded directly.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null, "WorldSessionScope must be present in ShipShoreTransitLauncher.");
            var session = root.Container.Resolve<WorldSession>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            var transit = root.GetComponent<ShoreSceneTransitCoordinator>();
            var fixtureControls = root.GetComponent<ShoreTransitFixtureControls>();
            Assert.That(commander, Is.Not.Null, "TacticalCommandController must be present.");
            Assert.That(transit, Is.Not.Null, "ShoreSceneTransitCoordinator must be present.");
            Assert.That(fixtureControls, Is.Not.Null, "ShoreTransitFixtureControls must be present.");

            var prevBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            var prevBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            Mouse testMouse = null;
            bool mouseWasEnabled = true;

            try
            {
                // Verify initial state: selection is empty
                commander.ClearSelection();
                Assert.That(commander.Selection.Count, Is.Zero);

                // Find Hero actor proxy and verify selection ring marker is initially inactive
                var heroActor = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).First(p => p.ActorId == Hero);
                var heroRing = heroActor.transform.Find("SelectionRing");
                Assert.That(heroRing, Is.Not.Null, "Hero proxy must have authored SelectionRing marker.");
                Assert.That(heroRing.gameObject.activeSelf, Is.False,
                    "Selection ring must be inactive before selection.");

                // Compute screen position of protagonist from Camera.main
                var camera = Camera.main;
                Assert.That(camera, Is.Not.Null, "Camera.main must be active.");
                Vector2 screenPos = camera.WorldToScreenPoint(heroActor.transform.position);

                // Ensure InputSystem Mouse device exists
                var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                Assert.That(mouse, Is.Not.Null, "Input System Mouse device must be present.");
                testMouse = mouse;
                mouseWasEnabled = mouse.enabled;
                if (!mouse.enabled)
                    InputSystem.EnableDevice(mouse);

                // Queue pointer position update to actor screen position
                using (DeltaStateEvent.From(mouse.position, out var posEvent))
                {
                    mouse.position.WriteValueIntoEvent(screenPos, posEvent);
                    InputSystem.QueueEvent(posEvent);
                }

                // Queue left mouse button press
                using (DeltaStateEvent.From(mouse.leftButton, out var pressEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, pressEvent);
                    InputSystem.QueueEvent(pressEvent);
                }

                yield return null;

                // Queue left mouse button release at the exact same screen position
                using (DeltaStateEvent.From(mouse.position, out var posEvent2))
                {
                    mouse.position.WriteValueIntoEvent(screenPos, posEvent2);
                    InputSystem.QueueEvent(posEvent2);
                }
                using (DeltaStateEvent.From(mouse.leftButton, out var releaseEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(0f, releaseEvent);
                    InputSystem.QueueEvent(releaseEvent);
                }

                float deadline = Time.realtimeSinceStartup + 2f;
                while (!commander.Selection.IsSelected(Hero) && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                // Verify actor selected through genuine Input System pointer click
                Assert.That(commander.Selection.Count, Is.EqualTo(1),
                    "Pointer click via InputSystem must select protagonist.");
                Assert.That(commander.Selection.IsSelected(Hero), Is.True);
                Assert.That(heroActor.IsSelected, Is.True);
                Assert.That(heroRing.gameObject.activeSelf, Is.True,
                    "Selection ring marker must be visible when actor is selected.");

                // Verify UI pointer blocker: clicking over HUD must NOT deselect actor
                Vector2 hudScreenPos = new Vector2(fixtureControls.HudRect.x + 20f,
                    Screen.height - (fixtureControls.HudRect.y + 20f));
                Assert.That(fixtureControls.IsPointerOverHud(hudScreenPos), Is.True,
                    "Fixture controls rect must intercept HUD screen position.");
                Assert.That(commander.IsPointerBlocked(hudScreenPos), Is.True,
                    "Commander must recognize pointer is blocked over fixture controls.");

                // Queue pointer press and release over the HUD
                using (DeltaStateEvent.From(mouse.position, out var hudPosEvent))
                {
                    mouse.position.WriteValueIntoEvent(hudScreenPos, hudPosEvent);
                    InputSystem.QueueEvent(hudPosEvent);
                }
                using (DeltaStateEvent.From(mouse.leftButton, out var hudPressEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, hudPressEvent);
                    InputSystem.QueueEvent(hudPressEvent);
                }
                yield return null;
                using (DeltaStateEvent.From(mouse.position, out var hudPosEvent2))
                {
                    mouse.position.WriteValueIntoEvent(hudScreenPos, hudPosEvent2);
                    InputSystem.QueueEvent(hudPosEvent2);
                }
                using (DeltaStateEvent.From(mouse.leftButton, out var hudReleaseEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(0f, hudReleaseEvent);
                    InputSystem.QueueEvent(hudReleaseEvent);
                }
                yield return null;

                // Selection must remain intact after clicking UI
                Assert.That(commander.Selection.Count, Is.EqualTo(1),
                    "Clicking fixture controls must not deselect crew.");
                Assert.That(commander.Selection.IsSelected(Hero), Is.True);

                // Now execute departure with the squad selected via pointer input
                EntityId opId = EntityId.Parse("op:pointer:depart:1");
                Assert.That(transit.TryDepartSelected(ShoreDestination(), opId), Is.True,
                    "TryDepartSelected must succeed when squad was selected via mouse pointer.");

                float transitTimeout = 8f;
                while (transit.IsBusy && transitTimeout > 0f)
                {
                    yield return null;
                    transitTimeout -= Time.deltaTime;
                }

                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(ShorePath));
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(session.SceneCheckpoint.PartyIds, Contains.Item(Hero));
            }
            finally
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = prevBehavior;
                InputSystem.settings.backgroundBehavior = prevBackground;
                if (testMouse != null && testMouse.enabled != mouseWasEnabled)
                {
                    if (mouseWasEnabled)
                        InputSystem.EnableDevice(testMouse);
                    else
                        InputSystem.DisableDevice(testMouse);
                }
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
