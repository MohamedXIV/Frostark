using System.Collections;
using System.Linq;
using FrostArk.Bootstrap;
using NUnit.Framework;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace FrostArk.Tests
{
    public sealed class PackageSmokeTests
    {
        public sealed class Probe : System.IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        [Test]
        public void LifetimeScopeResolvesScopedServiceAndDisposesIt()
        {
            var scope = LifetimeScope.Create(b => b.Register<Probe>(Lifetime.Scoped), "BootstrapSmokeScope");
            Probe probe = null;
            try
            {
                // In EditMode Awake is not guaranteed to run on a newly added MonoBehaviour.
                if (scope.Container == null) scope.Build();
                probe = scope.Container.Resolve<Probe>();
                Assert.That(scope.Container.Resolve<Probe>(), Is.SameAs(probe));
            }
            finally
            {
                // Dispose() uses delayed Destroy, which Unity forbids in EditMode.
                scope.DisposeCore();
                Object.DestroyImmediate(scope.gameObject);
            }
            Assert.That(probe.Disposed, Is.True);
        }

        [UnityTest]
        public IEnumerator AddressablesLoadsAndReleasesConfiguredTextAsset()
        {
            BootstrapSetup.Initialize();
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var fastMode = settings.DataBuilders.OfType<BuildScriptFastMode>().Single();
            var key = Addressables.kAddressablesRuntimeDataPath;
            var hadKey = PlayerPrefs.HasKey(key);
            var previous = PlayerPrefs.GetString(key);
            var previousLoadDelay = settings.SimulatedLoadDelay;
            AsyncOperationHandle<TextAsset> handle = default;
            AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> init = default;
            try
            {
                // Fast mode schedules reads through DelayedActionManager using
                // Time.unscaledTime, which does not advance in EditMode tests.
                // A zero delay makes the AssetDatabase provider complete the
                // actual Addressables load without waiting on frozen test time.
                settings.SimulatedLoadDelay = 0f;
                var result = fastMode.BuildData<AddressablesPlayModeBuildResult>(new AddressablesDataBuilderInput(settings));
                Assert.That(result.Error, Is.Null.Or.Empty);
                init = Addressables.InitializeAsync(false);
                yield return init;
                Assert.That(init.Status, Is.EqualTo(AsyncOperationStatus.Succeeded));
                foreach (var provider in Addressables.ResourceManager.ResourceProviders)
                {
                    if (provider is AssetDatabaseProvider assetDatabaseProvider)
                        assetDatabaseProvider.SetLoadDelay(0f);
                }
                handle = Addressables.LoadAssetAsync<TextAsset>(BootstrapSetup.FixtureAddress);
                yield return handle;
                Assert.That(handle.Status, Is.EqualTo(AsyncOperationStatus.Succeeded));
                Assert.That(handle.Result.text.Trim(), Is.EqualTo("FrostArk Addressables smoke fixture v1"));
                handle.Release();
                Assert.That(handle.IsValid(), Is.False);
            }
            finally
            {
                if (handle.IsValid()) Addressables.Release(handle);
                if (init.IsValid()) Addressables.Release(init);
                settings.SimulatedLoadDelay = previousLoadDelay;
                if (hadKey) PlayerPrefs.SetString(key, previous);
                else PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
