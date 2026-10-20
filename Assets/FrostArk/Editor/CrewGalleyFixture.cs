using System;
using System.IO;
using FrostArk.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// Opt-in, isolated shipboard-playable fixture. Does not read/replace
    /// ShipDeckEnvironment.unity, SampleScene or any designer-authored scene.
    /// Its generated YAML must be imported and playtested in a real Unity Editor
    /// before we report G2/G3 or ask the user to try the slice.
    /// </summary>
    public static class CrewGalleyFixture
    {
        public const string ScenePath = "Assets/FrostArk/Scenes/CrewGalleyVerticalSlice.unity";

        [MenuItem("FrostArk/Fixtures/Rebuild Crew Galley Slice")]
        public static void CreateScene()
        {
            var original = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(original.path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            try
            {
                var root = new GameObject("GalleyPlayableFixture");
                SceneManager.MoveGameObjectToScene(root, scene);
                var grid = CrewGalleyFixtureState.Grid();

                var deck = Cube(root.transform, "ShipDeckFloor",
                    new Vector3(0f, -0.13f, 0f),
                    new Vector3(grid.Width, 0.25f, grid.Height),
                    new Color(0.23f, 0.29f, 0.33f));
                deck.transform.SetSiblingIndex(0);

                foreach (var blocked in CrewGalleyFixtureState.WallCells())
                {
                    if (blocked == CrewGalleyFixtureState.GalleyCell) continue;
                    Cube(root.transform, "Bulkhead_" + blocked.x + "_" + blocked.y,
                        grid.WorldCenter(blocked, 0.85f),
                        new Vector3(1f, 1.7f, 1f),
                        new Color(0.12f, 0.19f, 0.23f));
                }

                var source = Cube(root.transform, "Galley_" +
                    CrewGalleyFixtureState.Galley.Value,
                    grid.WorldCenter(CrewGalleyFixtureState.GalleyCell, 0.5f),
                    new Vector3(0.9f, 1f, 0.9f),
                    new Color(0.74f, 0.36f, 0.17f));

                Marker(root.transform, grid, "EatInteractionSlot",
                    CrewGalleyFixtureState.EatCell, new Color(0.95f, 0.72f, 0.16f));
                Marker(root.transform, grid, "DrinkInteractionSlot",
                    CrewGalleyFixtureState.DrinkCell, new Color(0.20f, 0.70f, 0.94f));

                var engineer = Capsule(root.transform, "Engineer_" +
                    CrewGalleyFixtureState.Engineer.Value,
                    grid.WorldCenter(CrewGalleyFixtureState.StartCell, 1f),
                    new Color(0.98f, 0.77f, 0.31f));
                Capsule(root.transform, "Relief_" +
                    CrewGalleyFixtureState.Relief.Value,
                    grid.WorldCenter(new Vector2Int(1, 6), 1f),
                    new Color(0.44f, 0.92f, 0.71f));

                var camObj = new GameObject("CrewGalleyTopDownCamera");
                camObj.transform.SetParent(root.transform, false);
                camObj.transform.position = new Vector3(0, 13, -8);
                camObj.transform.rotation = Quaternion.Euler(62f, 0f, 0f);
                camObj.tag = "MainCamera";
                var cam = camObj.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 8f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.055f, 0.09f, 0.12f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 80f;

                var sunObj = new GameObject("ColdDeckWorkLight");
                sunObj.transform.SetParent(root.transform, false);
                sunObj.transform.rotation = Quaternion.Euler(48, -25, 0);
                var sun = sunObj.AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.3f;
                sun.color = new Color(0.80f, 0.89f, 1.0f);

                var scopeRoot = new GameObject("FrostArkSessionScope");
                SceneManager.MoveGameObjectToScene(scopeRoot, scene);
                scopeRoot.AddComponent<WorldSessionScope>();
                scopeRoot.AddComponent<CrewGalleyFixtureBootstrap>();

                var presenter = new GameObject("WorldSessionPresenter");
                presenter.transform.SetParent(scopeRoot.transform, false);
                presenter.AddComponent<WorldSessionPresenter>();

                var controllerObject = new GameObject("CrewGalleyController");
                controllerObject.transform.SetParent(scopeRoot.transform, false);
                var controller = controllerObject.AddComponent<CrewGalleyController>();
                controller.Configure(engineer.transform, source.transform);

                var dir = Path.GetDirectoryName(ScenePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException("Could not save generated crew galley fixture.");

                var existing = EditorBuildSettings.scenes;
                if (!Array.Exists(existing, s => s.path == ScenePath))
                {
                    var updated = new EditorBuildSettingsScene[existing.Length + 1];
                    Array.Copy(existing, updated, existing.Length);
                    updated[existing.Length] = new EditorBuildSettingsScene(ScenePath, true);
                    EditorBuildSettings.scenes = updated;
                }
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[CrewGalleyFixture] Generated scene: " + ScenePath +
                    " — Unity import and G2/G3 PlayMode are still required.");
            }
            finally
            {
                if (!untitled)
                {
                    if (original.IsValid() && original.isLoaded)
                        SceneManager.SetActiveScene(original);
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static GameObject Cube(Transform parent, string name, Vector3 position,
            Vector3 scale, Color color)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cube);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = position;
            o.transform.localScale = scale;
            o.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return o;
        }

        private static GameObject Capsule(Transform parent, string name, Vector3 position,
            Color color)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = position;
            o.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            o.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
            return o;
        }

        private static void Marker(Transform parent, CrewShipGridPath grid,
            string name, Vector2Int cell, Color color)
        {
            var o = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            o.name = name;
            o.transform.SetParent(parent, false);
            o.transform.position = grid.WorldCenter(cell, 0.04f);
            o.transform.localScale = new Vector3(0.65f, 0.02f, 0.65f);
            var collider = o.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            o.GetComponent<Renderer>().sharedMaterial = MaterialFor(color);
        }

        private static Material MaterialFor(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No Lit shader found for galley fixture.");
            var material = new Material(shader) { color = color };
            return material;
        }
    }
}
