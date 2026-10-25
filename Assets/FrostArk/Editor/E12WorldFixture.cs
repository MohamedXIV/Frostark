using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FrostArk.Unity;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// Explicit authoring fixture for the E12 persistent run root. The scene is
    /// intentionally a composition root only; the ship, camera, light and one
    /// physical actor per crew are created once at Play from canonical state.
    /// </summary>
    public static class E12WorldFixture
    {
        public const string ScenePath = "Assets/FrostArk/Scenes/E12PersistentWorld.unity";
        public const string SceneName = "E12PersistentWorld";

        [MenuItem("FrostArk/Fixtures/Rebuild E12 Persistent World Scene")]
        public static void RebuildPersistentWorldScene()
        {
            var previous = SceneManager.GetActiveScene();
            bool wasUntitled = string.IsNullOrEmpty(previous.path);
            var mode = wasUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);

            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;

                var root = new GameObject(SceneName);
                root.AddComponent<WorldSessionScope>();
                root.AddComponent<PersistentWorldRuntime>();

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException(
                        "Could not save the E12 persistent world scene.");

                var scenes = EditorBuildSettings.scenes.ToList();
                if (!scenes.Any(x => x.path == ScenePath))
                    scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
                EditorBuildSettings.scenes = scenes.ToArray();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[FrostArk] E12 persistent world scene rebuilt at " + ScenePath);
            }
            finally
            {
                if (!wasUntitled && previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
                if (scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
