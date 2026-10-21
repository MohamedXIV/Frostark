using System.Collections;
using System.Linq;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G3 loads the exact checked-in ship demo scene through Build Settings.
    /// Exercises actual runtime-built world and the production RTS camera
    /// context path; does not fake a second WorldSession or use direct Move.
    /// Must run in Unity Editor/PlayMode to count as passing evidence.
    /// </summary>
    public sealed class ShipRtsDemoScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator OpenCheckedInSceneThenSelectAndMoveAcrossRooms()
        {
            var load = SceneManager.LoadSceneAsync("ShipRtsDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "Tracked scene is in EditorBuildSettings.");
            yield return load;
            yield return null; // Start() builds demo and binds the physical actors.

            var demo = UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.IsReady, Is.True);
            Assert.That(demo.Session.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Ship));
            var commander = demo.Commander;
            var actors = UnityEngine.Object
                .FindObjectsByType<ShipCrewSelectionProxy>(FindObjectsSortMode.None)
                .OrderBy(x => x.ActorId.Value).ToArray();
            Assert.That(actors.Length, Is.EqualTo(4));
            Assert.That(actors.All(x => x.Walker.IsConfigured), Is.True);

            commander.SelectActor(actors[0].ActorId);
            commander.SelectActor(actors[1].ActorId, addToSelection: true);
            var camera = UnityEngine.Camera.main;
            Assert.That(camera, Is.Not.Null);
            Physics.SyncTransforms();
            var target = camera.WorldToScreenPoint(new Vector3(8.5f, 0f, 2.5f));
            Assert.That(commander.ProcessContextClick(target), Is.True);
            Assert.That(commander.LastTheaterMoveDispatch.Count, Is.EqualTo(2));
            Assert.That(commander.LastTheaterMoveDispatch.Values.All(x => x.Accepted), Is.True);
            Assert.That(actors.Count(x => x.Walker.IsMoving), Is.EqualTo(2));

            commander.ClearSelection();
            Assert.That(commander.Selection.Count, Is.Zero);
            // Door closure must immediately invalidate travel for those still
            // in the bridge room. It must not mutate the game's WorldRevision.
            demo.SetDoorOpen(false);
            Assert.That(actors.All(x => !x.Walker.IsMoving), Is.True);
            Assert.That(actors.All(x => x.Walker.LastError == "RouteInvalidated"),
                Is.True);
            Assert.That(demo.Session.Revision, Is.Zero);
            demo.SetDoorOpen(true);

            commander.SelectActor(actors[0].ActorId);
            Assert.That(commander.ProcessContextClick(target), Is.True);
            yield return null;
            Assert.That(actors[0].Walker.IsMoving, Is.True);
            Assert.That(demo.Session.Revision, Is.Zero);
        }

        [UnityTearDown]
        public IEnumerator RestoreStarterScene()
        {
            // Isolate the named-scene load from other PlayMode suites.
            var restore = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            if (restore != null) yield return restore;
        }
    }
}
