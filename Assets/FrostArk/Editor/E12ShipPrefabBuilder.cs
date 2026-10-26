using System;
using UnityEditor;
using UnityEngine;
using FrostArk.Unity;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Editor
{
    /// <summary>
    /// One-time authoring generator for the persistent ship prefab. Runtime
    /// code instantiates the saved prefab; it never rebuilds this geometry.
    /// </summary>
    public static class E12ShipPrefabBuilder
    {
        public const string PrefabPath =
            "Assets/FrostArk/Resources/E12PersistentShip.prefab";
        private const string MaterialsRoot = "Assets/FrostArk/Materials";
        private const string MaterialsFolder = MaterialsRoot + "/E12";

        [MenuItem("FrostArk/Fixtures/Rebuild E12 Ship Prefab")]
        public static void Rebuild()
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            EnsureFolder("Assets/FrostArk", "Resources");
            EnsureFolder("Assets/FrostArk", "Materials");
            EnsureFolder(MaterialsRoot, "E12");

            var hullMaterial = MaterialAsset("E12ShipHull",
                new Color(0.12f, 0.18f, 0.22f));
            var floorMaterial = MaterialAsset("E12ShipFloor",
                new Color(0.25f, 0.35f, 0.40f));
            var bridgeMaterial = MaterialAsset("E12ShipBridgeConsole",
                new Color(0.20f, 0.59f, 0.68f));
            var engineMaterial = MaterialAsset("E12ShipEnginePlant",
                new Color(0.86f, 0.46f, 0.25f));
            var doorMaterial = MaterialAsset("E12ShipHatch",
                new Color(0.83f, 0.34f, 0.20f));

            var root = new GameObject("ShipWorld | persistent");
            try
            {
                var bridge = PersistentWorldRuntime.BridgeRoomId;
                var engine = PersistentWorldRuntime.EngineRoomId;
                var bridgeOrigin = Marker(root.transform, "BridgeOrigin", Vector3.zero);
                var engineOrigin = Marker(root.transform, "EngineOrigin",
                    new Vector3(6f, 0f, 0f));
                var bridgeFloor = Floor(root.transform, "BridgeFloor", Vector3.zero,
                    bridge, floorMaterial);
                var engineFloor = Floor(root.transform, "EngineFloor",
                    new Vector3(6f, 0f, 0f), engine, floorMaterial);

                Hull(root.transform, hullMaterial);
                DecorationCube(root.transform, "BridgeConsole",
                    new Vector3(0.5f, 0.45f, 5.5f),
                    new Vector3(0.8f, 0.9f, 0.8f), bridgeMaterial, false);
                DecorationCube(root.transform, "EnginePlant",
                    new Vector3(10.5f, 0.45f, 4.5f),
                    new Vector3(0.8f, 0.9f, 0.8f), engineMaterial, false);
                var doorVisual = DecorationCube(root.transform, "BulkheadHatch",
                    new Vector3(6f, 0.75f, 2.5f),
                    new Vector3(0.2f, 1.5f, 1f), doorMaterial, false);
                doorVisual.SetActive(false);

                var authoring = root.AddComponent<PersistentShipGeometry>();
                authoring.Configure(
                    new[]
                    {
                        new ShipTacticalSceneBootstrap.RoomBinding
                        {
                            RoomId = bridge,
                            Origin = bridgeOrigin,
                            Floor = bridgeFloor
                        },
                        new ShipTacticalSceneBootstrap.RoomBinding
                        {
                            RoomId = engine,
                            Origin = engineOrigin,
                            Floor = engineFloor
                        }
                    },
                    new[]
                    {
                        new ShipTacticalSceneBootstrap.DoorBinding
                        {
                            DoorId = PersistentWorldRuntime.DoorId,
                            FromRoomId = bridge,
                            FromCell = new Vector2Int(5, 2),
                            ToRoomId = engine,
                            ToCell = new Vector2Int(0, 2),
                            Open = true
                        }
                    });

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[FrostArk] E12 ship prefab rebuilt at " + PrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Material MaterialAsset(string name, Color color)
        {
            var path = MaterialsFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                             Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
                if (shader == null)
                    throw new InvalidOperationException("No usable ship material shader.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + child))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Transform Marker(Transform parent, string name,
            Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static ShipRoomClickSurface Floor(Transform parent, string name,
            Vector3 roomOrigin, string roomId, Material material)
        {
            var floor = DecorationCube(parent, name,
                roomOrigin + new Vector3(3f, -0.12f, 3f),
                new Vector3(6f, 0.24f, 6f), material, true);
            var surface = floor.AddComponent<ShipRoomClickSurface>();
            surface.Configure(EntityId.Parse(roomId), parent, roomOrigin);
            return surface;
        }

        private static void Hull(Transform parent, Material material)
        {
            DecorationCube(parent, "OuterHullNorth",
                new Vector3(6f, 0.55f, 6.08f),
                new Vector3(12.4f, 1.1f, 0.2f), material, false);
            DecorationCube(parent, "OuterHullSouth",
                new Vector3(6f, 0.55f, -0.08f),
                new Vector3(12.4f, 1.1f, 0.2f), material, false);
            DecorationCube(parent, "OuterHullWest",
                new Vector3(-0.08f, 0.55f, 3f),
                new Vector3(0.2f, 1.1f, 6.2f), material, false);
            DecorationCube(parent, "OuterHullEastSouth",
                new Vector3(12.08f, 0.55f, 0.95f),
                new Vector3(0.2f, 1.1f, 2.1f), material, false);
            DecorationCube(parent, "OuterHullEastNorth",
                new Vector3(12.08f, 0.55f, 5.05f),
                new Vector3(0.2f, 1.1f, 2.1f), material, false);
            DecorationCube(parent, "BulkheadSouthOfDoor",
                new Vector3(6f, 0.55f, 1f),
                new Vector3(0.16f, 1.1f, 2f), material, false);
            DecorationCube(parent, "BulkheadNorthOfDoor",
                new Vector3(6f, 0.55f, 4.5f),
                new Vector3(0.16f, 1.1f, 3f), material, false);
        }

        private static GameObject DecorationCube(Transform parent, string name,
            Vector3 localPosition, Vector3 scale, Material material,
            bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                var collision = go.GetComponent<Collider>();
                if (collision != null)
                    UnityEngine.Object.DestroyImmediate(collision);
            }
            return go;
        }
    }
}
