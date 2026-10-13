using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace FrostArk.VisualBenchmark.Editor
{
    /// <summary>
    /// Opt-in visual experiment only. Never changes SampleScene or project
    /// quality settings. Generated scenes/assets are not gameplay authority.
    /// Unity import, rendering and platform results require a real G2/G3 run.
    /// </summary>
    public static class VisualBenchmarkBuilder
    {
        private const string Root = "Assets/VisualBenchmarks";
        private const string SpecPath = Root + "/visual-benchmark.json";
        private const string SceneFolder = Root + "/Scenes";
        private const string MaterialFolder = Root + "/Materials";
        private const string TextureFolder = Root + "/Textures";
        private const string CaptureFolder = "Logs/visual-benchmark";

        [Serializable]
        private sealed class BenchmarkSpec
        {
            public int schemaVersion;
            public string textureFilter;
            public bool vertexSnapping;
            public int texturePixels;
            public float tileWorldUnits;
            public CameraProfile[] profiles;
        }

        [Serializable]
        private sealed class CameraProfile
        {
            public string id;
            public float pitchDegrees;
            public float yawDegrees;
            public float orthographicSize;
            public float fogDensity;
        }

        [MenuItem("FrostArk/Visual Benchmark/Generate Comparison Scenes")]
        public static void CreateScene()
        {
            var spec = LoadSpec();
            EnsureFolder(SceneFolder);
            EnsureFolder(MaterialFolder);
            EnsureFolder(TextureFolder);

            var snow = CreateMaterial("Snow", new Color(0.77f, 0.85f, 0.86f), spec);
            var ice = CreateMaterial("SeaIce", new Color(0.37f, 0.66f, 0.73f), spec);
            var steel = CreateMaterial("ShipSteel", new Color(0.24f, 0.33f, 0.39f), spec);
            var rust = CreateMaterial("RustCover", new Color(0.55f, 0.32f, 0.22f), spec);
            var dark = CreateMaterial("DarkMetal", new Color(0.17f, 0.20f, 0.23f), spec);
            var survivor = CreateMaterial("Survivor", new Color(0.91f, 0.65f, 0.21f), spec);
            var pine = CreateMaterial("Pine", new Color(0.17f, 0.35f, 0.33f), spec);

            foreach (var profile in spec.profiles)
            {
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                try
                {
                    SceneManager.SetActiveScene(scene);
                    var root = new GameObject("V01_VisualBenchmark");
                    AddBox(root.transform, "SnowShelf", new Vector3(0, -0.3f, 0),
                        new Vector3(30, 0.5f, 23), snow);
                    AddBox(root.transform, "SeaIce", new Vector3(-10, -0.03f, -5),
                        new Vector3(9, 0.08f, 11), ice);
                    AddBox(root.transform, "ShipHull", new Vector3(-6, 0.45f, 2),
                        new Vector3(8, 1.0f, 4.5f), dark);
                    AddBox(root.transform, "ShipDeck", new Vector3(-6, 1.05f, 2),
                        new Vector3(8.3f, 0.25f, 4.8f), steel);
                    AddBox(root.transform, "ShipCabin", new Vector3(-8.4f, 2.1f, 2),
                        new Vector3(2.4f, 2.1f, 2.5f), rust);
                    AddBox(root.transform, "ShipRail", new Vector3(-6, 1.6f, 4.25f),
                        new Vector3(8.1f, 0.5f, 0.15f), steel);
                    AddBox(root.transform, "IndustrialCoverA", new Vector3(3.5f, 0.8f, 1.2f),
                        new Vector3(2.8f, 1.6f, 0.7f), rust);
                    AddBox(root.transform, "IndustrialCoverB", new Vector3(6.4f, 1.1f, 3),
                        new Vector3(0.8f, 2.2f, 3.4f), dark);
                    AddBox(root.transform, "IndustrialTower", new Vector3(8.4f, 3.2f, 5),
                        new Vector3(1.2f, 6.4f, 1.2f), steel);
                    AddBox(root.transform, "SnowDrift", new Vector3(5, 0.15f, -5),
                        new Vector3(7, 0.7f, 2.8f), snow);
                    AddPrimitive(root.transform, PrimitiveType.Capsule, "Protagonist",
                        new Vector3(2.0f, 1.0f, -2.0f), new Vector3(0.8f, 1, 0.8f), survivor);
                    AddPrimitive(root.transform, PrimitiveType.Capsule, "OcclusionTarget",
                        new Vector3(5.5f, 1.0f, 2.5f), new Vector3(0.8f, 1, 0.8f), survivor);
                    AddPrimitive(root.transform, PrimitiveType.Cylinder, "PineTrunk",
                        new Vector3(9.5f, 1.0f, -3.0f), new Vector3(0.35f, 1, 0.35f), dark);
                    AddPrimitive(root.transform, PrimitiveType.Capsule, "PineCanopy",
                        new Vector3(9.5f, 3.3f, -3.0f), new Vector3(1.2f, 2.0f, 1.2f), pine);

                    var sun = new GameObject("ArcticDirectionalLight");
                    sun.transform.SetParent(root.transform, false);
                    sun.transform.rotation = Quaternion.Euler(48, -28, 0);
                    var sunLight = sun.AddComponent<Light>();
                    sunLight.type = LightType.Directional;
                    sunLight.intensity = 1.1f;
                    sunLight.color = new Color(0.76f, 0.87f, 1f);

                    var lamp = new GameObject("IndustrialWarmLight");
                    lamp.transform.SetParent(root.transform, false);
                    lamp.transform.position = new Vector3(6.5f, 3.6f, 2.5f);
                    var point = lamp.AddComponent<Light>();
                    point.type = LightType.Point;
                    point.range = 8;
                    point.intensity = 4;
                    point.color = new Color(1f, 0.56f, 0.28f);

                    var cameraObject = new GameObject("BenchmarkCamera");
                    cameraObject.transform.SetParent(root.transform, false);
                    var rotation = Quaternion.Euler(profile.pitchDegrees, profile.yawDegrees, 0);
                    var target = new Vector3(0, 0.6f, 0);
                    cameraObject.transform.rotation = rotation;
                    cameraObject.transform.position = target - rotation * Vector3.forward * 32;
                    var camera = cameraObject.AddComponent<Camera>();
                    camera.orthographic = true;
                    camera.orthographicSize = profile.orthographicSize;
                    camera.nearClipPlane = 0.3f;
                    camera.farClipPlane = 100;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(0.43f, 0.58f, 0.64f);

                    RenderSettings.ambientLight = new Color(0.49f, 0.55f, 0.62f);
                    RenderSettings.fog = true;
                    RenderSettings.fogMode = FogMode.ExponentialSquared;
                    RenderSettings.fogColor = camera.backgroundColor;
                    RenderSettings.fogDensity = profile.fogDensity;

                    var path = SceneFolder + "/" + profile.id + ".unity";
                    if (!EditorSceneManager.SaveScene(scene, path))
                        throw new InvalidOperationException("Unable to save benchmark scene " + path);
                    Debug.Log("[FrostArk V01] Generated scene: " + path);
                }
                finally
                {
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
            AssetDatabase.SaveAssets();
        }

        [MenuItem("FrostArk/Visual Benchmark/Capture Comparison PNGs")]
        public static void CaptureScreenshots()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("G3 capture requires a graphics-capable Unity Editor.");
            var spec = LoadSpec();
            // Additive scenes share a renderer with other loaded scenes. Refuse
            // contaminated captures rather than toggling user scene objects.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var loaded = SceneManager.GetSceneAt(i);
                if (loaded.isLoaded && loaded.GetRootGameObjects().Length > 0)
                    throw new InvalidOperationException(
                        "Open an empty Editor scene before benchmark capture; other scene objects would contaminate the images.");
            }
            Directory.CreateDirectory(CaptureFolder);
            foreach (var profile in spec.profiles)
            {
                var path = SceneFolder + "/" + profile.id + ".unity";
                if (!File.Exists(path))
                    throw new FileNotFoundException("Generate benchmark scenes first.", path);
                var previous = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    SceneManager.SetActiveScene(scene);
                    var camera = scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                        .Single(c => c.name == "BenchmarkCamera");
                    Capture(camera, profile.id, 1600, 900, "desktop-16x9");
                    Capture(camera, profile.id, 1024, 768, "controller-4x3");
                    Capture(camera, profile.id, 960, 540, "mobile-landscape");
                }
                finally
                {
                    if (previous.IsValid() && previous.isLoaded)
                        SceneManager.SetActiveScene(previous);
                    if (scene.IsValid() && scene.isLoaded)
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void Capture(Camera camera, string profile, int width, int height, string target)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prior = RenderTexture.active;
            Texture2D image = null;
            try
            {
                rt.Create();
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (!RenderPipeline.SupportsRenderRequest(camera, request))
                    throw new NotSupportedException("Active URP renderer does not support render requests.");
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt;
                image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(CaptureFolder, profile + "-" + target + ".png"),
                    image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prior;
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static BenchmarkSpec LoadSpec()
        {
            var spec = JsonUtility.FromJson<BenchmarkSpec>(File.ReadAllText(SpecPath));
            if (spec == null || spec.schemaVersion != 1 || spec.vertexSnapping ||
                spec.textureFilter != "Point" || spec.texturePixels < 8 ||
                spec.tileWorldUnits <= 0 || spec.profiles == null ||
                spec.profiles.Length != 4 ||
                spec.profiles.Select(p => p.pitchDegrees).Distinct().Count() != 2 ||
                spec.profiles.Select(p => p.fogDensity).Distinct().Count() != 2 ||
                spec.profiles.Select(p => p.pitchDegrees + "/" + p.fogDensity).Distinct().Count() != 4 ||
                spec.profiles.Select(p => p.id).Distinct().Count() != spec.profiles.Length ||
                spec.profiles.Any(p => p.pitchDegrees < 20 || p.pitchDegrees > 75 ||
                    p.orthographicSize <= 0 || p.fogDensity < 0 || p.fogDensity > 0.1f))
                throw new InvalidDataException("Unsupported or invalid V01 benchmark spec.");
            return spec;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static Material CreateMaterial(string name, Color baseColor, BenchmarkSpec spec)
        {
            var texturePath = TextureFolder + "/" + name + ".png";
            if (!File.Exists(texturePath))
            {
                var texture = new Texture2D(spec.texturePixels, spec.texturePixels,
                    TextureFormat.RGBA32, false);
                for (int y = 0; y < spec.texturePixels; y++)
                for (int x = 0; x < spec.texturePixels; x++)
                {
                    var variation = ((x / 4 + y / 4) % 2 == 0) ? 0.92f : 1f;
                    texture.SetPixel(x, y, baseColor * variation);
                }
                texture.Apply();
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
                AssetDatabase.ImportAsset(texturePath);
            }
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException("Texture did not import: " + texturePath);
            if (importer.filterMode != FilterMode.Point || importer.mipmapEnabled)
            {
                importer.filterMode = FilterMode.Point;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
            var path = MaterialFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    throw new InvalidOperationException("URP Lit shader not available.");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetTextureScale("_BaseMap",
                new Vector2(spec.tileWorldUnits, spec.tileWorldUnits));
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject AddBox(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            return AddPrimitive(parent, PrimitiveType.Cube, name, position, scale, material);
        }

        private static GameObject AddPrimitive(Transform parent, PrimitiveType primitive,
            string name, Vector3 position, Vector3 scale, Material material)
        {
            var obj = GameObject.CreatePrimitive(primitive);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            obj.transform.localScale = scale;
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
    }
}
