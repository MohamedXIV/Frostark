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

                var proxy = UnityEngine.Object.FindFirstObjectByType<TacticalInteractableProxy>();
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
    }
}
