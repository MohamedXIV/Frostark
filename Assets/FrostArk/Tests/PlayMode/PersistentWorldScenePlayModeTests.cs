using System.Collections;
using System.Linq;
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

            var runtime = Object.FindFirstObjectByType<PersistentWorldRuntime>();
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
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var runtime in runtimes)
                if (runtime != null)
                    Object.Destroy(runtime.transform.root.gameObject);
            yield return null;
        }
    }
}
