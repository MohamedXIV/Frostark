using System;
using FrostArk.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// Explicitly invoked headless/editor fixture generator. This does not
    /// touch SampleScene and never runs on Editor startup.
    /// Use -executeMethod FrostArk.Unity.Editor.FrostArkSessionFixture.CreateScene.
    /// Generated scene must undergo Unity Editor import and review separately.
    /// </summary>
    public static class FrostArkSessionFixture
    {
        public const string ScenePath = "Assets/FrostArk/Scenes/SessionBootstrap.unity";

        [MenuItem("FrostArk/Fixtures/Rebuild Session Bootstrap Scene")]
        public static void CreateScene()
        {
            var previouslyActive = SceneManager.GetActiveScene();
            bool wasUntitled = string.IsNullOrEmpty(previouslyActive.path);
            var mode = wasUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            try
            {
                var root = new GameObject("FrostArkSessionScope");
                SceneManager.MoveGameObjectToScene(root, fixtureScene);

                var presenter = new GameObject("SessionPresenter");
                presenter.transform.SetParent(root.transform, false);
                presenter.AddComponent<WorldSessionPresenter>();
                root.AddComponent<WorldSessionScope>();

                var sceneDir = System.IO.Path.GetDirectoryName(ScenePath);
                if (!string.IsNullOrEmpty(sceneDir) && !System.IO.Directory.Exists(sceneDir))
                    System.IO.Directory.CreateDirectory(sceneDir);

                if (!EditorSceneManager.SaveScene(fixtureScene, ScenePath))
                    throw new InvalidOperationException("Failed to save FrostArk session bootstrap fixture.");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[FrostArkSessionFixture] Scene saved to " + ScenePath);
            }
            finally
            {
                if (!wasUntitled)
                {
                    if (previouslyActive.IsValid() && previouslyActive.isLoaded)
                        SceneManager.SetActiveScene(previouslyActive);
                    if (fixtureScene.IsValid() && fixtureScene.isLoaded)
                        EditorSceneManager.CloseScene(fixtureScene, true);
                }
            }
        }
    }
}
