using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrostArk.Bootstrap
{
    // Explicit bootstrap only; never runs automatically on Editor startup.
    public static class BootstrapSetup
    {
        public const string FixturePath = "Assets/BootstrapFixtures/addressables-smoke.txt";
        public const string FixtureAddress = "bootstrap/addressables-smoke";

        public static void Initialize()
        {
            AssetDatabase.Refresh();
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            var group = settings.FindGroup("BootstrapFixtures") ?? settings.CreateGroup(
                "BootstrapFixtures", false, false, false, null,
                typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            var guid = AssetDatabase.AssetPathToGUID(FixturePath);
            if (string.IsNullOrEmpty(guid))
                throw new System.InvalidOperationException("Bootstrap fixture was not imported.");
            settings.CreateOrMoveEntry(guid, group).address = FixtureAddress;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        [Serializable]
        private sealed class PackageEvidence
        {
            public string name;
            public string version;
            public string source;
        }

        [Serializable]
        private sealed class ReadinessEvidence
        {
            public string unityVersion;
            public bool androidModule;
            public bool webglModule;
            public PackageEvidence[] packages;
        }

        // Called during the reproducible headless import gate, after compilation.
        public static void ReportReadiness()
        {
            var expected = new System.Collections.Generic.Dictionary<string, string>
            {
                ["jp.hadashikick.vcontainer"] = "1.19.0",
                ["com.unity.addressables"] = "2.11.2",
                ["com.unity.cinemachine"] = "6.6.0",
                ["com.unity.render-pipelines.universal"] = "17.6.0",
                ["com.unity.inputsystem"] = "1.20.1",
                ["com.unity.ai.navigation"] = "2.0.16",
                ["com.unity.test-framework"] = "1.8.0"
            };
            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages();
            foreach (var pair in expected)
            {
                var package = packages.SingleOrDefault(p => p.name == pair.Key);
                if (package == null || package.version != pair.Value)
                    throw new InvalidOperationException("Bootstrap package mismatch: " + pair.Key);
            }
            var evidence = new ReadinessEvidence
            {
                unityVersion = Application.unityVersion,
                androidModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android),
                webglModule = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL),
                packages = packages.Where(p => expected.ContainsKey(p.name)).Select(p => new PackageEvidence
                {
                    name = p.name, version = p.version, source = p.source.ToString()
                }).ToArray()
            };
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/bootstrap-readiness.json", JsonUtility.ToJson(evidence, true));
            Debug.Log("[FrostArkBootstrap] Required packages match the pinned contract; readiness evidence written.");
        }
    }
}
