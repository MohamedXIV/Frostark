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
    /// Explicit authoring fixture for the E12 persistent run root and the one
    /// additive shore environment. The shore contains geometry and NavMesh only;
    /// it must not own a session, camera, light or actor.
    /// </summary>
    public static class E12WorldFixture
    {
        public const string ScenePath = "Assets/FrostArk/Scenes/E12PersistentWorld.unity";
        public const string SceneName = "E12PersistentWorld";
        public const string ShoreScenePath = "Assets/FrostArk/Scenes/E12ShoreEnvironment.unity";
        public const string ShoreSceneName = "E12ShoreEnvironment";

        [MenuItem("FrostArk/Fixtures/Rebuild All E12 Scenes")]
        public static void RebuildAllScenes()
        {
            RebuildPersistentWorldScene();
            RebuildShoreEnvironmentScene();
        }

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

                SaveAndRegister(scene, ScenePath);
            }
            finally
            {
                RestorePrevious(previous, wasUntitled, scene);
            }
        }

        [MenuItem("FrostArk/Fixtures/Rebuild E12 Shore Environment Scene")]
        public static void RebuildShoreEnvironmentScene()
        {
            var previous = SceneManager.GetActiveScene();
            bool wasUntitled = string.IsNullOrEmpty(previous.path);
            var mode = wasUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);

            try
            {
                EditorSettings.serializationMode = SerializationMode.ForceText;

                var root = new GameObject(ShoreSceneName);
                root.transform.position = new Vector3(20f, 0f, 0f);
                root.AddComponent<ShoreEnvironmentRuntime>();

                SaveAndRegister(scene, ShoreScenePath);
            }
            finally
            {
                RestorePrevious(previous, wasUntitled, scene);
            }
        }

        private static void SaveAndRegister(Scene scene, string path)
        {
            if (!EditorSceneManager.SaveScene(scene, path))
                throw new InvalidOperationException("Could not save scene " + path);

            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(x => x.path == path))
                scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FrostArk] E12 scene rebuilt at " + path);
        }

        private static void RestorePrevious(Scene previous, bool wasUntitled, Scene created)
        {
            if (!wasUntitled && previous.IsValid() && previous.isLoaded)
                SceneManager.SetActiveScene(previous);
            if (created.IsValid() && created.isLoaded)
                EditorSceneManager.CloseScene(created, true);
        }
    }
}
