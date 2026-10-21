using System;
using System.Linq;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G2: verify the ACTUAL checked-in .unity asset resolves its script and
    /// the isolated runtime fixture establishes real ship scene/crew bindings.
    /// Unity Editor import + execution is required to validate these tests.
    /// </summary>
    public sealed class ShipRtsDemoSceneEditModeTests
    {
        private const string ScenePath = "Assets/FrostArk/Scenes/ShipRtsDemo.unity";

        [Test]
        public void TrackedSceneLoadsWithLiveDemoEntryAndNoMissingBootstrapScript()
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Assert.That(sceneAsset, Is.Not.Null, "Ship demo .unity should be imported.");
            var loaded = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                var roots = loaded.GetRootGameObjects();
                Assert.That(roots.Length, Is.EqualTo(1));
                Assert.That(roots[0].GetComponent<ShipRtsDemoRuntime>(), Is.Not.Null,
                    "Serialized MonoBehaviour must match its tracked .meta script GUID.");
                Assert.That(roots[0].GetComponents<MonoBehaviour>().All(x => x != null),
                    Is.True, "Do not ship missing-script components.");
            }
            finally
            {
                EditorSceneManager.CloseScene(loaded, true);
            }
        }

        [Test]
        public void DemoEntryCreatesFourCanonicalCrewTwoRoomsAndSwitchableDoor()
        {
            var go = new GameObject("ShipRTS-Demo-EditMode");
            try
            {
                var demo = go.AddComponent<ShipRtsDemoRuntime>();
                demo.BuildDemo();
                Assert.That(demo.IsReady, Is.True);
                Assert.That(demo.Session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(demo.Session.SceneCheckpoint.SceneId,
                    Is.EqualTo(EntityId.Parse(ShipRtsDemoRuntime.DemoSceneId)));
                Assert.That(demo.Session.Capture().State.Layout.Rooms.Count,
                    Is.EqualTo(2));
                Assert.That(demo.Session.Capture().State.CrewDuty.Members.Count,
                    Is.EqualTo(4));
                Assert.That(go.GetComponentsInChildren<ShipCrewSelectionProxy>(true).Length,
                    Is.EqualTo(4));
                Assert.That(go.GetComponentsInChildren<ShipRoomClickSurface>(true).Length,
                    Is.EqualTo(2));

                // The IMGUI overlay is *not* EventSystem UI. Gameplay pointer
                // events must not leak through its button/label rectangle.
                var buttonPixel = new Vector2(55f, Screen.height - 176f);
                var mapPixel = new Vector2(540f, Screen.height - 176f);
                Assert.That(demo.IsPointerOverHud(buttonPixel), Is.True);
                Assert.That(demo.Commander.IsPointerBlocked(buttonPixel), Is.True);
                Assert.That(demo.IsPointerOverHud(mapPixel), Is.False);
                Assert.That(demo.Commander.IsPointerBlocked(mapPixel), Is.False);

                var from = new ShipGridCell(
                    EntityId.Parse(ShipRtsDemoRuntime.BridgeRoomId), 1, 2);
                var to = new ShipGridCell(
                    EntityId.Parse(ShipRtsDemoRuntime.EngineRoomId), 2, 2);
                Assert.That(demo.Bootstrap.Map.FindPath(from, to).Count,
                    Is.GreaterThan(1));
                demo.SetDoorOpen(false);
                Assert.That(demo.Bootstrap.Map.FindPath(from, to), Is.Empty);
                demo.SetDoorOpen(true);
                Assert.That(demo.Bootstrap.Map.FindPath(from, to).Count,
                    Is.GreaterThan(1));
                Assert.That(demo.Session.Revision, Is.Zero,
                    "Scene actor motion and door fixture never mutate canonical stock.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
