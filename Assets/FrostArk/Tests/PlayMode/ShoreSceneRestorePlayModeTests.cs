using System;
using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
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
    /// A real G3 run requires explicitly generated scene assets via
    /// FrostArk/Fixtures/Rebuild Ship-Shore Transit Scenes, then PlayMode.
    /// These tests are NOT executed by the .NET Core G0/G1 runner.
    /// </summary>
    public sealed class ShoreSceneRestorePlayModeTests
    {
        private const string Launcher = "Assets/FrostArk/Scenes/ShipShoreTransitLauncher.unity";
        private const string Ship = "Assets/FrostArk/Scenes/ShipDeckEnvironment.unity";
        private const string Shore = "Assets/FrostArk/Scenes/ShoreLandingEnvironment.unity";
        private static readonly EntityId Hero = EntityId.Parse("actor:protagonist");
        private static readonly EntityId Companion = EntityId.Parse("actor:companion");
        private static readonly EntityId Pickup = EntityId.Parse("salvage:supply_cache");
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap_metal");

        private static SceneTransitDestination Landing()
        {
            var destination = new SceneTransitDestination();
            destination.Configure(PhysicalTheater.Shore, Shore,
                EntityId.Parse("scene:shore:landing"), EntityId.Parse("anchor:shore:entry"),
                EntityId.Parse("island:training-floe"));
            return destination;
        }
        private static SceneTransitDestination ShipDestination()
        {
            var destination = new SceneTransitDestination();
            destination.Configure(PhysicalTheater.Ship, Ship,
                EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:ship:gangway"));
            return destination;
        }


        [UnityTest]
        public IEnumerator SaveWhileAshoreRestoresSceneAndCollectedProxyWithoutResurrectingLoot()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "Generate fixture scenes in the Unity Editor first.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var oldRoot = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(oldRoot, Is.Not.Null);
            var session = oldRoot.Container.Resolve<WorldSession>();
            var transit = oldRoot.GetComponent<ShoreSceneTransitCoordinator>();
            Assert.That(transit, Is.Not.Null);
            var commander = oldRoot.GetComponentInChildren<TacticalCommandController>();
            commander.SelectActor(Hero);
            commander.SelectActor(Companion, addToSelection: true);
            Assert.That(transit.TryDepartSelected(Landing(),
                EntityId.Parse("transition:restore-test-depart")), Is.True);

            float time = 8f;
            while (transit.IsBusy && time > 0)
            {
                yield return null;
                time -= Time.deltaTime;
            }
            Assert.That(transit.IsBusy, Is.False);
            Assert.That(transit.LastError, Is.Null);
            Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

            // A canonical collected pickup is saved while the party is away.
            Assert.That(session.Submit(new CollectSalvageCommand(
                EntityId.Parse("command:restored-collect"), Pickup, session.Revision)).Success,
                Is.True);
            var oldRevision = session.Revision;
            var saved = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
            Assert.That(saved.Success, Is.True);
            Assert.That(saved.Value.SceneCheckpoint.PartyIds, Is.EqualTo(new[] { Companion, Hero }));
            UnityEngine.Object.Destroy(oldRoot.gameObject);
            yield return null;

            // Simulate reopening from a minimal neutral bootstrap scene.
            yield return SceneManager.LoadSceneAsync(Ship, LoadSceneMode.Single);
            var replacementRoot = new GameObject("RecoveredRunRoot");
            replacementRoot.SetActive(false);
            var scope = replacementRoot.AddComponent<WorldSessionScope>();
            scope.SetRestoreSnapshot(saved.Value);
            var adapter = replacementRoot.AddComponent<ShoreSceneTransitCoordinator>();
            adapter.ConfigureDestinations(Landing());
            var commandRoot = new GameObject("RestoredCommander");
            commandRoot.transform.SetParent(replacementRoot.transform, false);
            commandRoot.AddComponent<TacticalCommandController>();
            replacementRoot.SetActive(true);
            yield return null;

            try
            {
                if (scope.Container == null)
                    scope.Build();
                var restored = scope.Container.Resolve<WorldSession>();
                Assert.That(restored, Is.Not.SameAs(session));
                Assert.That(restored.Revision, Is.EqualTo(oldRevision));
                Assert.That(restored.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(adapter.TryRestorePhysicalScene(), Is.True);

                time = 8f;
                while (adapter.IsBusy && time > 0)
                {
                    yield return null;
                    time -= Time.deltaTime;
                }
                Assert.That(adapter.IsBusy, Is.False);
                Assert.That(adapter.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Shore));
                Assert.That(restored.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(restored.Capture().State.Inventory.IsCollected(Pickup), Is.True);
                Assert.That(restored.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, Scrap),
                    Is.EqualTo(5));
                Assert.That(UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(
                    FindObjectsSortMode.None).Select(x => x.ActorId).Distinct().Count(),
                    Is.EqualTo(2));

                var proxy = UnityEngine.Object.FindObjectsByType<TacticalInteractableProxy>()
                    .First(p => p.TargetId == Pickup);
                Assert.That(proxy, Is.Not.Null);
                Assert.That(proxy.IsDepleted, Is.True,
                    "New scene proxy must derive consumed state from saved WorldSession.");
                var restoredCommander = replacementRoot.GetComponentInChildren<TacticalCommandController>();
                restoredCommander.SelectActor(Hero);
                Assert.That(restoredCommander.IssueInteractOrder(proxy), Is.False,
                    "The loaded visual must not reoffer already-collected salvage.");
                Assert.That(restored.Revision, Is.EqualTo(oldRevision));
            }
            finally
            {
                if (replacementRoot != null)
                    UnityEngine.Object.Destroy(replacementRoot);
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
        public IEnumerator SaveWhileAshoreWithZeroPendingShipMovesRestoresPhysicalShipAndExpeditionCorrectly()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var oldRoot = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(oldRoot, Is.Not.Null);
            var session = oldRoot.Container.Resolve<WorldSession>();
            var transit = oldRoot.GetComponent<ShoreSceneTransitCoordinator>();
            var commander = oldRoot.GetComponentInChildren<TacticalCommandController>();

            // Hero departs alone; Companion stays aboard
            commander.SelectActor(Hero);
            Assert.That(transit.TryDepartSelected(Landing(),
                EntityId.Parse("transition:restore-zero-moves:depart")), Is.True);

            float time = 8f;
            while (transit.IsBusy && time > 0)
            {
                yield return null;
                time -= Time.deltaTime;
            }
            Assert.That(transit.IsBusy, Is.False);
            Assert.That(transit.LastError, Is.Null);
            Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

            // Explicitly assert ZERO pending ship moves
            Assert.That(session.PendingCrewMoves, Is.Empty,
                "Must have zero pending ship moves before capturing save ashore.");

            var oldRevision = session.Revision;
            var saved = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
            Assert.That(saved.Success, Is.True);
            Assert.That(saved.Value.PendingCrewMoves, Is.Empty,
                "Saved DTO must have zero pending ship moves.");
            Assert.That(saved.Value.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
            Assert.That(saved.Value.SceneCheckpoint.PartyIds, Is.EqualTo(new[] { Hero }));

            UnityEngine.Object.Destroy(oldRoot.gameObject);
            yield return null;

            // Reopen from neutral bootstrap scene
            yield return SceneManager.LoadSceneAsync(Ship, LoadSceneMode.Single);
            var replacementRoot = new GameObject("RecoveredRunRoot");
            replacementRoot.SetActive(false);
            var scope = replacementRoot.AddComponent<WorldSessionScope>();
            scope.SetRestoreSnapshot(saved.Value);
            var adapter = replacementRoot.AddComponent<ShoreSceneTransitCoordinator>();
            adapter.ConfigureDestinations(Landing(), ShipDestination());
            var commandRoot = new GameObject("RestoredCommander");
            commandRoot.transform.SetParent(replacementRoot.transform, false);
            commandRoot.AddComponent<TacticalCommandController>();
            replacementRoot.SetActive(true);
            yield return null;

            try
            {
                if (scope.Container == null)
                    scope.Build();
                var restored = scope.Container.Resolve<WorldSession>();
                Assert.That(restored.Revision, Is.EqualTo(oldRevision));
                Assert.That(restored.PendingCrewMoves, Is.Empty,
                    "Restored session must maintain zero pending ship moves.");
                Assert.That(restored.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(adapter.TryRestorePhysicalScene(), Is.True);

                time = 8f;
                while (adapter.IsBusy && time > 0)
                {
                    yield return null;
                    time -= Time.deltaTime;
                }
                Assert.That(adapter.IsBusy, Is.False);
                Assert.That(adapter.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Shore));

                // Verify actor partition on restore:
                Assert.That(restored.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
                Assert.That(restored.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);

                // Both actor identities are present across the restored scenes
                var proxies = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(FindObjectsSortMode.None);
                Assert.That(proxies.Select(x => x.ActorId).Distinct().Count(), Is.EqualTo(2));

                // Returning to ship from the restored ashore state must succeed cleanly
                Assert.That(adapter.TryReturn(ShipDestination(),
                    EntityId.Parse("transition:restore-zero-moves:return")), Is.True);
                time = 8f;
                while (adapter.IsBusy && time > 0)
                {
                    yield return null;
                    time -= Time.deltaTime;
                }
                Assert.That(adapter.IsBusy, Is.False);
                Assert.That(adapter.LastError, Is.Null);
                Assert.That(SceneManager.GetActiveScene().path, Is.EqualTo(Ship));
                Assert.That(restored.Capture().State.CrewDuty.Get(Hero).IsAway, Is.False);
                Assert.That(restored.Capture().State.CrewDuty.Get(Companion).IsAway, Is.False);
            }
            finally
            {
                if (replacementRoot != null)
                    UnityEngine.Object.Destroy(replacementRoot);
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
        public IEnumerator PlayMode_DiskSaveCoordinator_QuickSaveAndQuickLoad_RestoresPhysicalSessionFromDisk()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True,
                "Generate fixture scenes in the Unity Editor first.");
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var oldRoot = UnityEngine.Object.FindFirstObjectByType<WorldSessionScope>();
            Assert.That(oldRoot, Is.Not.Null);
            var session = oldRoot.Container.Resolve<WorldSession>();
            var transit = oldRoot.GetComponent<ShoreSceneTransitCoordinator>();
            var commander = oldRoot.GetComponentInChildren<TacticalCommandController>();
            Assert.That(transit, Is.Not.Null);
            Assert.That(commander, Is.Not.Null);

            // Depart Hero to shore
            commander.SelectActor(Hero);
            Assert.That(transit.TryDepartSelected(Landing(),
                EntityId.Parse("transition:disk-save:depart")), Is.True);

            float time = 8f;
            while (transit.IsBusy && time > 0)
            {
                yield return null;
                time -= Time.deltaTime;
            }
            Assert.That(transit.IsBusy, Is.False);
            Assert.That(transit.LastError, Is.Null);
            Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

            // Create temporary test directory for LocalDiskSaveBlobStore to ensure test isolation
            string testSaveDir = System.IO.Path.Combine(Application.temporaryCachePath, "FrostArkDiskSaveTest_" + Guid.NewGuid().ToString("N"));
            var store = new LocalDiskSaveBlobStore(testSaveDir, "quicksave_test");
            var coordinator = oldRoot.gameObject.AddComponent<DiskSaveCoordinator>();
            coordinator.Initialize(session, store);

            try
            {
                // QuickSave to disk via coordinator
                Assert.That(coordinator.QuickSave(), Is.True, "QuickSave to disk must succeed.");
                Assert.That(System.IO.File.Exists(store.PrimaryPath), Is.True, "Save file must exist on disk.");
                long savedRevision = session.Revision;

                // Load from disk via coordinator
                var loadResult = coordinator.QuickLoad();
                Assert.That(loadResult, Is.Not.Null);
                Assert.That(loadResult.Success, Is.True, "QuickLoad from disk must succeed: " + loadResult.Error);
                Assert.That(loadResult.Value.State.Revision, Is.EqualTo(savedRevision));
                Assert.That(loadResult.Value.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(loadResult.Value.SceneCheckpoint.PartyIds, Is.EqualTo(new[] { Hero }));
            }
            finally
            {
                if (System.IO.Directory.Exists(testSaveDir))
                {
                    try { System.IO.Directory.Delete(testSaveDir, true); } catch { }
                }
                if (oldRoot != null)
                    UnityEngine.Object.Destroy(oldRoot.gameObject);
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
        public IEnumerator PlayMode_DiskSaveCoordinator_SaveThenMutateThenQuickLoad_RestoresExactWorldAndSceneState()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindAnyObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null);
            var session = root.Container.Resolve<WorldSession>();
            var transit = root.GetComponent<ShoreSceneTransitCoordinator>();
            var commander = root.GetComponentInChildren<TacticalCommandController>();
            Assert.That(transit, Is.Not.Null);
            Assert.That(commander, Is.Not.Null);

            // Depart Hero to shore
            commander.SelectActor(Hero);
            Assert.That(transit.TryDepartSelected(Landing(),
                EntityId.Parse("transition:save-mutate-load:depart")), Is.True);

            float time = 8f;
            while (transit.IsBusy && time > 0)
            {
                yield return null;
                time -= Time.deltaTime;
            }
            Assert.That(transit.IsBusy, Is.False);
            Assert.That(transit.LastError, Is.Null);
            Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));

            // Set up coordinator with test store
            string testDir = System.IO.Path.Combine(Application.temporaryCachePath, "DiskSaveMutateTest_" + Guid.NewGuid().ToString("N"));
            var store = new LocalDiskSaveBlobStore(testDir, "save_slot");
            var coordinator = root.gameObject.AddComponent<DiskSaveCoordinator>();
            coordinator.Initialize(session, store);

            try
            {
                long initialRevision = session.Revision;
                var initialFuel = session.Capture().State.GetSupply(EntityId.Parse("supply:fuel"));
                var pickup = UnityEngine.Object.FindObjectsByType<TacticalInteractableProxy>(
                    FindObjectsSortMode.None).First(p => p.TargetId == EntityId.Parse("salvage:supply_cache"));
                Assert.That(pickup, Is.Not.Null);
                Assert.That(pickup.IsDepleted, Is.False);

                // 1. QuickSave initial ashore state
                Assert.That(coordinator.QuickSave(), Is.True, "QuickSave must succeed.");

                // 2. Mutate game state: loot salvage cache
                commander.SelectActor(Hero);
                Assert.That(commander.IssueInteractOrder(pickup), Is.True);
                float timeout = 9f;
                while (!pickup.IsDepleted && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(pickup.IsDepleted, Is.True, "Salvage should now be collected.");
                Assert.That(session.Capture().State.Inventory.IsCollected(pickup.TargetId), Is.True);
                Assert.That(session.Revision, Is.GreaterThan(initialRevision), "Revision must have advanced upon loot.");

                // 3. QuickLoad from disk
                var loadResult = coordinator.QuickLoad();
                Assert.That(loadResult, Is.Not.Null);
                Assert.That(loadResult.Success, Is.True, "QuickLoad must succeed: " + loadResult.Error);

                // Wait for any scene alignment to complete
                timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }

                // 4. Assert exact restored world and scene state
                Assert.That(session.Revision, Is.EqualTo(initialRevision),
                    "WorldSession revision must be restored to saved revision.");
                Assert.That(session.Capture().State.GetSupply(EntityId.Parse("supply:fuel")), Is.EqualTo(initialFuel));
                Assert.That(session.Capture().State.Inventory.IsCollected(pickup.TargetId), Is.False,
                    "Salvage collection state in WorldSession must be reverted.");
                Assert.That(pickup.IsDepleted, Is.False,
                    "Scene interactable visual must be restored to uncollected state.");
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(session.Capture().State.CrewDuty.Get(Hero).IsAway, Is.True);
            }
            finally
            {
                if (System.IO.Directory.Exists(testDir))
                {
                    try { System.IO.Directory.Delete(testDir, true); } catch { }
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

        [UnityTest]
        public IEnumerator PlayMode_SameActiveSceneRestore_ValidationFailure_DoesNotUnloadActiveScene()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindAnyObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null);
            var session = root.Container.Resolve<WorldSession>();
            var transit = root.GetComponent<ShoreSceneTransitCoordinator>();
            Assert.That(transit, Is.Not.Null);

            try
            {
                var activeScene = SceneManager.GetActiveScene();
                Assert.That(activeScene.isLoaded, Is.True);
                Assert.That(activeScene.path, Is.EqualTo(Launcher));

                // Author a same-active-scene destination with a nonexistent anchor ID so TryResolve fails
                var badDestination = new SceneTransitDestination();
                badDestination.Configure(PhysicalTheater.Ship, Launcher,
                    EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:nonexistent"));
                transit.ConfigureDestinations(badDestination);

                // Set a checkpoint matching this bad destination so TryRestorePhysicalScene picks it
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:nonexistent")));

                Assert.That(transit.TryRestorePhysicalScene(), Is.True); // begins async validation

                float timeout = 8f;
                while (transit.IsBusy && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }
                Assert.That(transit.IsBusy, Is.False);
                Assert.That(transit.LastError, Is.EqualTo("MissingSceneReentryAnchor"),
                    "Same-active-scene validation should reject missing anchor.");

                // CRITICAL INVARIANT: The active source scene must NOT have been unloaded
                Assert.That(activeScene.isLoaded, Is.True,
                    "Active source scene must remain loaded and valid when destination validation fails.");
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(activeScene));
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
        public IEnumerator PlayMode_DiskSaveCoordinator_QuickLoad_PhysicalSceneRestorationFailure_ReportsFailure()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded(Launcher), Is.True);
            yield return SceneManager.LoadSceneAsync(Launcher, LoadSceneMode.Single);
            yield return null;

            var root = UnityEngine.Object.FindAnyObjectByType<WorldSessionScope>();
            Assert.That(root, Is.Not.Null);
            var session = root.Container.Resolve<WorldSession>();
            var transit = root.GetComponent<ShoreSceneTransitCoordinator>();
            Assert.That(transit, Is.Not.Null);

            string testDir = System.IO.Path.Combine(Application.temporaryCachePath, "DiskSaveFailTest_" + Guid.NewGuid().ToString("N"));
            var store = new LocalDiskSaveBlobStore(testDir, "save_slot");
            var coordinator = root.gameObject.AddComponent<DiskSaveCoordinator>();
            coordinator.Initialize(session, store);

            try
            {
                // Set checkpoint pointing to an unmapped anchor and save
                var badCheckpoint = PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:unmapped_anchor"));
                session.SetSceneCheckpoint(badCheckpoint);

                Assert.That(coordinator.QuickSave(), Is.True, "QuickSave must succeed with valid domain state.");

                // QuickLoad should fail because checkpoint anchor is not mapped in transit destinations
                var loadResult = coordinator.QuickLoad();
                Assert.That(loadResult.Success, Is.False,
                    "QuickLoad must report failure if physical scene restoration is rejected.");
                Assert.That(loadResult.Error, Is.EqualTo(SaveDecodeError.InvalidData));
                Assert.That(coordinator.LastStatus, Does.Contain("Physical restoration rejected"),
                    "Status message must report physical restoration failure.");
            }
            finally
            {
                if (System.IO.Directory.Exists(testDir))
                {
                    try { System.IO.Directory.Delete(testDir, true); } catch { }
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
}
