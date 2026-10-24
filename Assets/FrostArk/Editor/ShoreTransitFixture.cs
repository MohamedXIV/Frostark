using System;
using System.IO;
using System.Linq;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using FrostArk.Unity;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// Explicitly-invoked Editor fixture generator. Keeps the session scope
    /// in exactly ONE launch scene; loaded environment scenes are scope-free.
    /// It never runs automatically during editor import or PlayMode.
    /// </summary>
    public static class ShoreTransitFixture
    {
        public const string LauncherPath = "Assets/FrostArk/Scenes/ShipShoreTransitLauncher.unity";
        public const string ShipPath = "Assets/FrostArk/Scenes/ShipDeckEnvironment.unity";
        public const string ShorePath = "Assets/FrostArk/Scenes/ShoreLandingEnvironment.unity";

        private static readonly EntityId ShipScene = EntityId.Parse("scene:ship:deck");
        private static readonly EntityId ShipAnchor = EntityId.Parse("anchor:ship:gangway");
        private static readonly EntityId ShoreScene = EntityId.Parse("scene:shore:landing");
        private static readonly EntityId ShoreAnchor = EntityId.Parse("anchor:shore:entry");
        private static readonly EntityId ShoreSite = EntityId.Parse("island:training-floe");

        [MenuItem("FrostArk/Fixtures/Rebuild Ship-Shore Transit Scenes")]
        public static void CreateScenes()
        {
            // Named, isolated test assets ONLY; no user production scene edits.
            Generate(ShipPath, false, false, ShipScene, ShipAnchor, true);
            Generate(ShorePath, false, true, ShoreScene, ShoreAnchor, false);
            Generate(LauncherPath, true, false, ShipScene, ShipAnchor, true);

            foreach (var path in new[] { LauncherPath, ShipPath, ShorePath })
            {
                if (!EditorBuildSettings.scenes.Any(x => x.path == path))
                {
                    var original = EditorBuildSettings.scenes;
                    var updated = new EditorBuildSettingsScene[original.Length + 1];
                    Array.Copy(original, updated, original.Length);
                    updated[original.Length] = new EditorBuildSettingsScene(path, true);
                    EditorBuildSettings.scenes = updated;
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[FrostArk] Ship/Shore fixture scenes generated. Open " + LauncherPath);
        }

        private static void Generate(string path, bool withScope, bool withSalvage,
            EntityId sceneId, EntityId anchorId, bool shipVisual)
        {
            var previous = SceneManager.GetActiveScene();
            bool wasUntitled = string.IsNullOrEmpty(previous.path);
            var mode = wasUntitled ? NewSceneMode.Single : NewSceneMode.Additive;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);

            try
            {
                var environment = new GameObject(shipVisual ? "ShipDeckEnvironment" : "ShoreEnvironment");
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = shipVisual ? "Deck" : "IceGround";
                floor.transform.SetParent(environment.transform, false);
                floor.transform.position = new Vector3(0, -0.1f, 0);
                floor.transform.localScale = new Vector3(30, 0.2f, 24);

                var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                obstacle.name = shipVisual ? "BoilerHouse" : "IceCover";
                obstacle.transform.SetParent(environment.transform, false);
                obstacle.transform.position = new Vector3(0, 1, 3);
                obstacle.transform.localScale = new Vector3(6, 2, 1);

                var surface = environment.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.Children;
                surface.BuildNavMesh();

                var sun = new GameObject("DirectionalLight");
                sun.transform.SetParent(environment.transform, false);
                sun.transform.rotation = Quaternion.Euler(48, -35, 0);
                var light = sun.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.3f;

                var cameraObj = new GameObject("TopDownCamera");
                cameraObj.transform.SetParent(environment.transform, false);
                cameraObj.transform.position = new Vector3(0, 17, -12);
                cameraObj.transform.rotation = Quaternion.Euler(57, 0, 0);
                cameraObj.tag = "MainCamera";
                var camera = cameraObj.AddComponent<Camera>();
                camera.orthographic = true;
                camera.orthographicSize = 9;

                var anchorObj = new GameObject("ReentryAnchor");
                anchorObj.transform.SetParent(environment.transform, false);
                anchorObj.transform.position = new Vector3(0, 0, -5);
                anchorObj.AddComponent<SceneReentryAnchor>().Configure(sceneId, anchorId);

                CreateActor(environment.transform, "Protagonist", "actor:protagonist", -2);
                CreateActor(environment.transform, "Companion", "actor:companion", 2);

                if (withSalvage)
                {
                    var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    crate.name = "PersistentShoreSalvage";
                    crate.transform.SetParent(environment.transform, false);
                    crate.transform.position = new Vector3(0, 0.5f, 6);
                    crate.transform.localScale = new Vector3(1.2f, 1f, 1.2f);
                    var target = crate.AddComponent<TacticalInteractableProxy>();
                    target.SetTargetId(EntityId.Parse("salvage:supply_cache"));
                    target.SetInteractionRange(2.5f);
                }

                var gangwayObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
                gangwayObj.name = shipVisual ? "ShipGangwayInteractable" : "ShoreReturnGangwayInteractable";
                gangwayObj.transform.SetParent(environment.transform, false);
                gangwayObj.transform.position = new Vector3(0, 0.5f, -3.5f);
                gangwayObj.transform.localScale = new Vector3(2.5f, 0.4f, 1.2f);
                var gangway = gangwayObj.AddComponent<ShoreGangwayInteractable>();
                gangway.SetInteractionRange(3.5f);
                var targetDest = new SceneTransitDestination();
                if (shipVisual)
                {
                    targetDest.Configure(PhysicalTheater.Shore, ShorePath, ShoreScene, ShoreAnchor, ShoreSite);
                    gangway.Configure(null, targetDest, GangwayTransitMode.DisembarkToShore);
                }
                else
                {
                    targetDest.Configure(PhysicalTheater.Ship, ShipPath, ShipScene, ShipAnchor);
                    gangway.Configure(null, targetDest, GangwayTransitMode.ReturnToShip);
                }

                if (withScope)
                {
                    var scopeRoot = new GameObject("PersistentRunScope");
                    var scope = scopeRoot.AddComponent<WorldSessionScope>();
                    scopeRoot.AddComponent<TacticalSliceBootstrap>();
                    var transit = scopeRoot.AddComponent<ShoreSceneTransitCoordinator>();

                    var shoreDestination = new SceneTransitDestination();
                    shoreDestination.Configure(PhysicalTheater.Shore, ShorePath,
                        ShoreScene, ShoreAnchor, ShoreSite);
                    var shipDestination = new SceneTransitDestination();
                    shipDestination.Configure(PhysicalTheater.Ship, ShipPath,
                        ShipScene, ShipAnchor);
                    transit.ConfigureDestinations(shoreDestination, shipDestination);

                    var presenter = new GameObject("SessionPresenter");
                    presenter.transform.SetParent(scopeRoot.transform, false);
                    presenter.AddComponent<WorldSessionPresenter>();

                    var commanderObject = new GameObject("TacticalCommander");
                    commanderObject.transform.SetParent(scopeRoot.transform, false);
                    var commander = commanderObject.AddComponent<TacticalCommandController>();
                    commander.SetCamera(camera);

                    scopeRoot.AddComponent<ShoreTransitFixtureControls>()
                        .Configure(transit, shoreDestination, shipDestination);
                    scopeRoot.AddComponent<DiskSaveCoordinator>();
                }

                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);
                if (!EditorSceneManager.SaveScene(scene, path))
                    throw new InvalidOperationException("Could not save ship/shore fixture " + path);
            }
            finally
            {
                if (!wasUntitled)
                {
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void CreateActor(Transform root, string name, string stableId, float x)
        {
            var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.name = name;
            capsule.transform.SetParent(root, false);
            capsule.transform.position = new Vector3(x, 1, -3);
            capsule.AddComponent<NavMeshAgent>();
            capsule.AddComponent<TacticalActorProxy>().SetActorId(EntityId.Parse(stableId));
        }
    }
}
