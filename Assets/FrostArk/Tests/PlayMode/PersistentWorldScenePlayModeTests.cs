using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G3 proof for the checked-in E12 composition root: the real Game-view
    /// pointer selects one persistent crew object and the grid motor moves it
    /// through the authored bulkhead without creating a second actor.
    /// </summary>
    public sealed class PersistentWorldScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator LoadedScene_RealPointerSelectsAndMovesSameCrewObject()
        {
            var load = SceneManager.LoadSceneAsync("E12PersistentWorld", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "E12PersistentWorld must be in Build Settings.");
            yield return load;
            yield return null;
            yield return null; // WorldSessionScope build + persistent runtime Start.

            var runtime = Object.FindAnyObjectByType<PersistentWorldRuntime>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);
            Assert.That(runtime.Session, Is.Not.Null);
            Assert.That(runtime.Session.SceneCheckpoint.Theater,
                Is.EqualTo(FrostArk.Core.Save.PhysicalTheater.Ship));

            var captain = runtime.CrewActors.First(a => a.ActorId ==
                EntityId.Parse(PersistentWorldRuntime.CaptainId));
            var captainObject = captain.gameObject;
            var commander = runtime.Commander;
            var camera = runtime.GameplayCamera;
            Assert.That(camera, Is.Not.Null);
            Assert.That(commander, Is.Not.Null);

            var prevBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            var prevBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            Mouse testMouse = null;
            bool mouseWasEnabled = true;
            try
            {
                commander.ClearSelection();
                Physics.SyncTransforms();

                var actorScreen = camera.WorldToScreenPoint(captain.transform.position);
                var engineScreen = camera.WorldToScreenPoint(new Vector3(8.5f, 0f, 2.5f));

                var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                Assert.That(mouse, Is.Not.Null);
                testMouse = mouse;
                mouseWasEnabled = mouse.enabled;
                if (!mouse.enabled)
                    InputSystem.EnableDevice(mouse);

                QueuePosition(mouse, actorScreen);
                QueueButton(mouse.leftButton, 1f);
                yield return null;
                QueueButton(mouse.leftButton, 0f);

                var deadline = Time.realtimeSinceStartup + 2f;
                while (!commander.Selection.IsSelected(captain.ActorId) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(commander.Selection.IsSelected(captain.ActorId), Is.True,
                    "Real Game-view click must select the persistent crew object.");

                QueuePosition(mouse, engineScreen);
                QueueButton(mouse.rightButton, 1f);
                yield return null;
                QueueButton(mouse.rightButton, 0f);

                deadline = Time.realtimeSinceStartup + 6f;
                while (captain.ShipMotor.LastReachedCell.RoomId !=
                       EntityId.Parse(PersistentWorldRuntime.EngineRoomId) &&
                       Time.realtimeSinceStartup < deadline)
                    yield return null;

                Assert.That(captain.gameObject, Is.SameAs(captainObject),
                    "The selected object must be the one that moved.");
                Assert.That(captain.ShipMotor.LastReachedCell.RoomId,
                    Is.EqualTo(EntityId.Parse(PersistentWorldRuntime.EngineRoomId)),
                    "Real right-click must move the same actor through the grid door.");
                Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
                Assert.That(runtime.transform.GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
                Assert.That(runtime.transform.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
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
            }

            if (runtime != null)
                Object.Destroy(runtime.transform.root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator LoadedScene_PhysicalGangwayCrossingKeepsOneCrewObject()
        {
            var load = SceneManager.LoadSceneAsync("E12PersistentWorld", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;
            yield return null;

            var runtime = Object.FindAnyObjectByType<PersistentWorldRuntime>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);

            float wait = 5f;
            while (!runtime.IsShoreReady && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(runtime.IsShoreReady, Is.True, runtime.ShoreError);

            var captain = runtime.CrewActors.First(a => a.ActorId ==
                EntityId.Parse(PersistentWorldRuntime.CaptainId));
            var captainObject = captain.gameObject;
            var commander = runtime.Commander;
            var camera = runtime.GameplayCamera;
            var gangway = runtime.GetComponentInChildren<PersistentGangway>(true);
            Assert.That(gangway, Is.Not.Null);
            commander.SelectActor(captain.ActorId);
            Physics.SyncTransforms();

            var gangwayScreen = camera.WorldToScreenPoint(gangway.transform.position);
            Assert.That(commander.ProcessContextClick(gangwayScreen), Is.True,
                "The docked gangway must accept the selected crew order.");

            wait = 20f;
            while ((runtime.IsCrossing || captain.IsOnShip) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(captain.gameObject, Is.SameAs(captainObject));
            Assert.That(runtime.IsCrossing, Is.False, runtime.LastTransitStatus);
            Assert.That(captain.IsOnShip, Is.False, runtime.LastTransitStatus);
            Assert.That(runtime.Session.Capture().State.CrewDuty.Get(captain.ActorId).IsAway,
                Is.True);
            Assert.That(runtime.Session.SceneCheckpoint.Theater,
                Is.EqualTo(FrostArk.Core.Save.PhysicalTheater.Shore));
            Assert.That(captain.ShoreAgent.enabled, Is.True);
            Assert.That(captain.ShipMotor.enabled, Is.False);
            Assert.That(SceneManager.GetSceneByName(PersistentWorldRuntime.ShoreSceneName).isLoaded,
                Is.True);
            Assert.That(runtime.transform.GetComponentsInChildren<PersistentCrewActor>(true).Length,
                Is.EqualTo(4));

            Physics.SyncTransforms();
            var shoreDestination = new Vector3(25f, 0f, 6f);
            var shoreScreen = camera.WorldToScreenPoint(shoreDestination);
            Assert.That(commander.ProcessContextClick(shoreScreen), Is.True,
                "A real shore-ground order must route to the same actor's NavMesh motor.");
            var shoreStart = captain.transform.position;
            var shoreDeadline = Time.realtimeSinceStartup + 3f;
            while (Vector3.Distance(shoreStart, captain.transform.position) < 0.2f &&
                   Time.realtimeSinceStartup < shoreDeadline)
                yield return null;
            Assert.That(Vector3.Distance(shoreStart, captain.transform.position),
                Is.GreaterThan(0.2f), "The away crew member must physically move on shore.");

            Physics.SyncTransforms();
            gangwayScreen = camera.WorldToScreenPoint(gangway.transform.position);
            Assert.That(commander.ProcessContextClick(gangwayScreen), Is.True,
                "The same gangway must accept the return order.");
            wait = 20f;
            while ((runtime.IsCrossing || !captain.IsOnShip) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(captain.gameObject, Is.SameAs(captainObject));
            Assert.That(runtime.IsCrossing, Is.False, runtime.LastTransitStatus);
            Assert.That(captain.IsOnShip, Is.True, runtime.LastTransitStatus);
            Assert.That(runtime.Session.Capture().State.CrewDuty.Get(captain.ActorId).IsAway,
                Is.False);
            Assert.That(runtime.Session.SceneCheckpoint.Theater,
                Is.EqualTo(FrostArk.Core.Save.PhysicalTheater.Ship));
            Assert.That(captain.ShoreAgent.enabled, Is.False);
            Assert.That(captain.ShipMotor.enabled, Is.True);
            Assert.That(runtime.transform.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                Is.EqualTo(1));

            Object.Destroy(runtime.transform.root.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator SaveAshore_RestoresOneShipAndAwayCrewOnShore()
        {
            var load = SceneManager.LoadSceneAsync("E12PersistentWorld", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;
            yield return null;

            var runtime = Object.FindAnyObjectByType<PersistentWorldRuntime>();
            Assert.That(runtime, Is.Not.Null);
            Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);
            float wait = 5f;
            while (!runtime.IsShoreReady && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(runtime.IsShoreReady, Is.True, runtime.ShoreError);

            var captain = runtime.CrewActors.First(a => a.ActorId ==
                EntityId.Parse(PersistentWorldRuntime.CaptainId));
            var commander = runtime.Commander;
            commander.SelectActor(captain.ActorId);
            Assert.That(runtime.TryCrossActor(captain.ActorId,
                EntityId.Parse("transition:save:depart")), Is.True);
            wait = 20f;
            while ((runtime.IsCrossing || captain.IsOnShip) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(captain.IsOnShip, Is.False, runtime.LastTransitStatus);
            Assert.That(runtime.Session.PendingCrewMoves, Is.Empty);

            Physics.SyncTransforms();
            var shorePoint = new Vector3(26f, 0f, -2f);
            Assert.That(commander.ProcessContextClick(
                runtime.GameplayCamera.WorldToScreenPoint(shorePoint)), Is.True);
            var shoreDeadline = Time.realtimeSinceStartup + 5f;
            while (Vector3.Distance(captain.transform.position, shorePoint) > 1.5f &&
                   Time.realtimeSinceStartup < shoreDeadline)
                yield return null;
            var savedPosition = captain.transform.position;
            Assert.That(Vector3.Distance(savedPosition, PersistentWorldRuntime.ShoreEntryPoint),
                Is.GreaterThan(3f), "The save must capture a non-entry shore position.");

            var saved = SaveCodec.TryDecode(
                SaveCodec.Encode(runtime.CaptureSnapshot()));
            Assert.That(saved.Success, Is.True);
            Assert.That(saved.Value.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Shore));
            Assert.That(saved.Value.SceneCheckpoint.PartyIds,
                Is.EqualTo(new[] { captain.ActorId }));
            Assert.That(saved.Value.ActorLocations.Count, Is.EqualTo(4));
            var savedCaptain = saved.Value.ActorLocations
                .First(x => x.CrewId == captain.ActorId);
            Assert.That(savedCaptain.Theater, Is.EqualTo(PhysicalTheater.Shore));
            Assert.That(savedCaptain.ShoreX,
                Is.EqualTo(savedPosition.x).Within(0.01f));
            Assert.That(savedCaptain.ShoreZ,
                Is.EqualTo(savedPosition.z).Within(0.01f));

            Object.Destroy(runtime.transform.root.gameObject);
            var unload = SceneManager.UnloadSceneAsync(
                PersistentWorldRuntime.ShoreSceneName);
            if (unload != null)
                yield return unload;
            yield return null;

            var restoredRoot = new GameObject("E12RestoredRun");
            restoredRoot.SetActive(false);
            var scope = restoredRoot.AddComponent<WorldSessionScope>();
            scope.SetRestoreSnapshot(saved.Value);
            var restoredRuntime = restoredRoot.AddComponent<PersistentWorldRuntime>();
            restoredRoot.SetActive(true);
            if (scope.Container == null)
                scope.Build();
            yield return null;
            yield return null;

            wait = 8f;
            while ((!restoredRuntime.IsReady || !restoredRuntime.IsShoreReady) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(restoredRuntime.IsReady, Is.True, restoredRuntime.InitializationError);
            Assert.That(restoredRuntime.IsShoreReady, Is.True, restoredRuntime.ShoreError);
            Assert.That(restoredRuntime.Session.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Shore));

            var restoredCaptain = restoredRuntime.CrewActors.First(a =>
                a.ActorId == captain.ActorId);
            Assert.That(restoredCaptain.IsOnShip, Is.False);
            Assert.That(restoredCaptain.ShipMotor.enabled, Is.False);
            Assert.That(restoredCaptain.ShoreAgent.enabled, Is.True);
            var restoredPoint = restoredCaptain.transform.position;
            Assert.That(Vector3.Distance(
                    new Vector3(restoredPoint.x, 0f, restoredPoint.z),
                    new Vector3(savedPosition.x, 0f, savedPosition.z)),
                Is.LessThan(0.75f),
                "Restore must preserve the exact saved shore position.");
            Assert.That(restoredRuntime.Session.Capture().State.CrewDuty
                .Get(captain.ActorId).IsAway, Is.True);
            Assert.That(restoredRuntime.CrewActors.Count(a => !a.IsOnShip), Is.EqualTo(1),
                "Exactly the saved shore party must be away after restore.");
            Assert.That(restoredRuntime.transform
                .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                Is.EqualTo(1), "Exactly one ship must exist after a shore restore.");
            Assert.That(restoredRuntime.transform
                .GetComponentsInChildren<PersistentCrewActor>(true).Length,
                Is.EqualTo(4));

            Assert.That(restoredRuntime.TryCrossActor(restoredCaptain.ActorId,
                EntityId.Parse("transition:save:return")), Is.True);
            wait = 20f;
            while ((restoredRuntime.IsCrossing || !restoredCaptain.IsOnShip) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(restoredCaptain.IsOnShip, Is.True, restoredRuntime.LastTransitStatus);
            Assert.That(restoredRuntime.Session.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Ship));
            Assert.That(restoredRuntime.Session.Capture().State.CrewDuty
                .Get(captain.ActorId).IsAway, Is.False);

            Object.Destroy(restoredRoot);
            var unloadRestored = SceneManager.UnloadSceneAsync(
                PersistentWorldRuntime.ShoreSceneName);
            if (unloadRestored != null)
                yield return unloadRestored;
            yield return null;
        }

        [UnityTest]
        public IEnumerator InPlaceRestore_RebuildsOneShipAndAwayCrew()
        {
            var load = SceneManager.LoadSceneAsync("E12PersistentWorld", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;
            yield return null;

            var runtime = Object.FindAnyObjectByType<PersistentWorldRuntime>();
            Assert.That(runtime, Is.Not.Null);
            float wait = 5f;
            while (!runtime.IsReady || !runtime.IsShoreReady)
            {
                if (wait <= 0f)
                    Assert.Fail("World did not initialize: " +
                        runtime.InitializationError + " / " + runtime.ShoreError);
                wait -= Time.deltaTime;
                yield return null;
            }

            var session = runtime.Session;
            var captain = runtime.CrewActors.First(a => a.ActorId ==
                EntityId.Parse(PersistentWorldRuntime.CaptainId));
            runtime.Commander.SelectActor(captain.ActorId);
            Assert.That(runtime.TryCrossActor(captain.ActorId,
                EntityId.Parse("transition:inplace:depart")), Is.True);
            wait = 20f;
            while ((runtime.IsCrossing || captain.IsOnShip) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            Assert.That(captain.IsOnShip, Is.False, runtime.LastTransitStatus);
            Physics.SyncTransforms();
            var shorePoint = new Vector3(27f, 0f, 5f);
            Assert.That(runtime.Commander.ProcessContextClick(
                runtime.GameplayCamera.WorldToScreenPoint(shorePoint)), Is.True);
            var shoreDeadline = Time.realtimeSinceStartup + 5f;
            while (Vector3.Distance(captain.transform.position, shorePoint) > 1.5f &&
                   Time.realtimeSinceStartup < shoreDeadline)
                yield return null;
            var savedPosition = captain.transform.position;
            var saved = SaveCodec.TryDecode(
                SaveCodec.Encode(runtime.CaptureSnapshot()));
            Assert.That(saved.Success, Is.True);

            Assert.That(runtime.RestoreFromSnapshot(saved.Value), Is.True);
            wait = 10f;
            while (runtime.IsRestoring && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            yield return null;

            Assert.That(runtime.IsRestoring, Is.False);
            Assert.That(runtime.IsReady, Is.True);
            Assert.That(runtime.IsShoreReady, Is.True, runtime.ShoreError);
            Assert.That(runtime.Session, Is.SameAs(session),
                "The one scoped WorldSession must survive an in-place load.");
            Assert.That(session.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Shore));
            Assert.That(session.Capture().State.CrewDuty
                .Get(captain.ActorId).IsAway, Is.True);
            Assert.That(runtime.CrewActors.Count, Is.EqualTo(4));
            Assert.That(runtime.transform
                .GetComponentsInChildren<PersistentCrewActor>(true).Length,
                Is.EqualTo(4));
            Assert.That(runtime.transform
                .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                Is.EqualTo(1));
            Assert.That(runtime.transform
                .GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
            Assert.That(runtime.transform
                .GetComponentsInChildren<Light>(true).Length, Is.EqualTo(1));
            Assert.That(runtime.transform
                .GetComponentsInChildren<PersistentWorldSaveCoordinator>(true).Length,
                Is.EqualTo(1));
            var restoredCaptain = runtime.CrewActors.First(a =>
                a.ActorId == captain.ActorId);
            Assert.That(restoredCaptain.IsOnShip, Is.False);
            Assert.That(restoredCaptain.ShoreAgent.enabled, Is.True);
            var restoredPoint = restoredCaptain.transform.position;
            Assert.That(Vector3.Distance(
                    new Vector3(restoredPoint.x, 0f, restoredPoint.z),
                    new Vector3(savedPosition.x, 0f, savedPosition.z)),
                Is.LessThan(0.75f),
                "In-place restore must preserve the exact saved shore position.");

            Object.Destroy(runtime.transform.root.gameObject);
            yield return null;
        }

        private static void QueuePosition(Mouse mouse, Vector2 screenPosition)
        {
            using (DeltaStateEvent.From(mouse.position, out var positionEvent))
            {
                mouse.position.WriteValueIntoEvent(screenPosition, positionEvent);
                InputSystem.QueueEvent(positionEvent);
            }
        }

        private static void QueueButton(UnityEngine.InputSystem.Controls.ButtonControl button,
            float value)
        {
            using (DeltaStateEvent.From(button, out var buttonEvent))
            {
                button.WriteValueIntoEvent(value, buttonEvent);
                InputSystem.QueueEvent(buttonEvent);
            }
        }

        [UnityTearDown]
        public IEnumerator CleanupPersistentRuns()
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
