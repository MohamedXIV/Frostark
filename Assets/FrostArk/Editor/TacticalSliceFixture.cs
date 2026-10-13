using System;
using System.IO;
using FrostArk.Core.World;
using FrostArk.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// Headless/Editor fixture generator for the Issue #21 Tactical Click-to-Move Vertical Slice.
    /// Builds a deterministic scene containing top-down camera, selectable protagonist and companion,
    /// walkable ground, obstacles, and a contextual salvage interactable.
    /// </summary>
    public static class TacticalSliceFixture
    {
        public const string ScenePath = "Assets/FrostArk/Scenes/TacticalVerticalSlice.unity";

        [MenuItem("FrostArk/Fixtures/Rebuild Tactical Slice Scene")]
        public static void CreateScene()
        {
            var previouslyActive = SceneManager.GetActiveScene();
            bool wasUntitled = string.IsNullOrEmpty(previouslyActive.path);
            var mode = wasUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var fixtureScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);

            try
            {
                var sceneRoot = new GameObject("TacticalSliceEnvironment");
                SceneManager.MoveGameObjectToScene(sceneRoot, fixtureScene);

                // 1. Walkable Ground
                var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ground.name = "WalkableGround";
                ground.transform.SetParent(sceneRoot.transform, false);
                ground.transform.position = new Vector3(0, -0.1f, 0);
                ground.transform.localScale = new Vector3(40, 0.2f, 40);

                // 2. Obstacle Cover Wall
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "CoverWall";
                wall.transform.SetParent(sceneRoot.transform, false);
                wall.transform.position = new Vector3(0, 1.0f, 3.0f);
                wall.transform.localScale = new Vector3(8.0f, 2.0f, 1.0f);

                // 3. Directional Sun
                var sunObj = new GameObject("TacticalSun");
                sunObj.transform.SetParent(sceneRoot.transform, false);
                sunObj.transform.rotation = Quaternion.Euler(50, -30, 0);
                var sunLight = sunObj.AddComponent<Light>();
                sunLight.type = LightType.Directional;
                sunLight.intensity = 1.0f;
                sunLight.color = new Color(0.9f, 0.95f, 1.0f);

                // 4. Steep-tilt Top-down Tactical Camera (Phase 2 baseline)
                var camObj = new GameObject("TacticalCamera");
                camObj.transform.SetParent(sceneRoot.transform, false);
                camObj.transform.position = new Vector3(0, 16.0f, -10.0f);
                camObj.transform.rotation = Quaternion.Euler(55, 0, 0);
                var cam = camObj.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 10.0f;
                cam.nearClipPlane = 0.3f;
                cam.farClipPlane = 100.0f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.2f, 0.25f, 0.3f);
                camObj.tag = "MainCamera";

                // 5. Destination feedback marker
                var markerObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                markerObj.name = "DestinationMarker";
                markerObj.transform.SetParent(sceneRoot.transform, false);
                markerObj.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
                var markerCollider = markerObj.GetComponent<Collider>();
                if (markerCollider != null) UnityEngine.Object.DestroyImmediate(markerCollider);
                var destMarker = markerObj.AddComponent<TacticalDestinationMarker>();
                markerObj.SetActive(false);

                // 6. Selectable Protagonist
                var protagonist = CreateActor(sceneRoot.transform, "Protagonist",
                    new Vector3(-2.5f, 1.0f, -2.0f), EntityId.Parse("actor:protagonist"), Color.yellow);

                // 7. Selectable Companion Crew
                var companion = CreateActor(sceneRoot.transform, "Companion",
                    new Vector3(2.5f, 1.0f, -2.0f), EntityId.Parse("actor:companion"), Color.cyan);

                // 8. Contextual Salvage Interactable
                var crateObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                crateObj.name = "SalvageCrate";
                crateObj.transform.SetParent(sceneRoot.transform, false);
                crateObj.transform.position = new Vector3(0, 0.5f, 7.0f);
                crateObj.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
                var crateProxy = crateObj.AddComponent<TacticalInteractableProxy>();
                crateProxy.SetTargetId(EntityId.Parse("salvage:supply_cache"));
                crateProxy.SetInteractionRange(2.5f);

                // 9. Session Scope & Presenters
                var scopeRoot = new GameObject("FrostArkSessionScope");
                SceneManager.MoveGameObjectToScene(scopeRoot, fixtureScene);
                var scope = scopeRoot.AddComponent<WorldSessionScope>();

                var presenterObj = new GameObject("SessionPresenter");
                presenterObj.transform.SetParent(scopeRoot.transform, false);
                presenterObj.AddComponent<WorldSessionPresenter>();

                var commanderObj = new GameObject("TacticalCommandController");
                commanderObj.transform.SetParent(scopeRoot.transform, false);
                var commander = commanderObj.AddComponent<TacticalCommandController>();
                commander.SetCamera(cam);
                commander.SetDestinationMarker(destMarker);

                // Save scene
                var sceneDir = Path.GetDirectoryName(ScenePath);
                if (!string.IsNullOrEmpty(sceneDir) && !Directory.Exists(sceneDir))
                    Directory.CreateDirectory(sceneDir);

                if (!EditorSceneManager.SaveScene(fixtureScene, ScenePath))
                    throw new InvalidOperationException("Failed to save tactical vertical slice scene.");

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[TacticalSliceFixture] Scene successfully saved to " + ScenePath);
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

        private static TacticalActorProxy CreateActor(Transform parent, string name,
            Vector3 position, EntityId actorId, Color color)
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = name;
            actorObj.transform.SetParent(parent, false);
            actorObj.transform.position = position;
            actorObj.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);

            var agent = actorObj.AddComponent<NavMeshAgent>();
            agent.speed = 4.0f;
            agent.acceleration = 12.0f;
            agent.stoppingDistance = 0.1f;
            agent.radius = 0.4f;
            agent.height = 2.0f;

            // Selection marker ring under feet
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            ring.transform.SetParent(actorObj.transform, false);
            ring.transform.localPosition = new Vector3(0, -0.95f, 0);
            ring.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);
            var ringCollider = ring.GetComponent<Collider>();
            if (ringCollider != null) UnityEngine.Object.DestroyImmediate(ringCollider);
            ring.SetActive(false);

            var proxy = actorObj.AddComponent<TacticalActorProxy>();
            proxy.SetActorId(actorId);
            proxy.SetSelectionMarker(ring);

            return proxy;
        }
    }
}
