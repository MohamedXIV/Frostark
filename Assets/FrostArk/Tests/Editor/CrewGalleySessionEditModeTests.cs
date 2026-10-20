using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VContainer;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Integration of one WorldSessionScope with an optional galley component.
    /// G2 EditMode test: it must be executed in Unity before claiming success.
    /// </summary>
    public sealed class CrewGalleySessionEditModeTests
    {
        [Test]
        public void GalleyBootstrapHasItsOwnLoadableMonoScriptAsset()
        {
            const string path = "Assets/FrostArk/Unity/CrewGalleyFixtureBootstrap.cs";
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            Assert.That(script, Is.Not.Null,
                "Serialized fixture components require a matching .cs asset.");
            Assert.That(script.GetClass(), Is.EqualTo(typeof(CrewGalleyFixtureBootstrap)),
                "The MonoBehaviour must not share a differently named source file.");

            var root = new GameObject("GalleyBootstrapMonoScriptSmoke");
            try
            {
                var component = root.AddComponent<CrewGalleyFixtureBootstrap>();
                Assert.That(AssetDatabase.GetAssetPath(
                    MonoScript.FromMonoBehaviour(component)), Is.EqualTo(path));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GalleyReceivesTheSameScopedWorldAndItsCanonicalFixtureState()
        {
            var root = new GameObject("GalleyScopeEditModeTest");
            root.SetActive(false);
            WorldSessionScope scope = null;
            try
            {
                var actor = new GameObject("Engineer");
                actor.transform.SetParent(root.transform, false);

                var source = new GameObject("Galley");
                source.transform.SetParent(root.transform, false);
                source.transform.position = CrewGalleyFixtureState.Grid().WorldCenter(
                    CrewGalleyFixtureState.GalleyCell, 0.5f);

                var controllerObject = new GameObject("CrewGalleyController");
                controllerObject.transform.SetParent(root.transform, false);
                var galley = controllerObject.AddComponent<CrewGalleyController>();
                galley.Configure(actor.transform, source.transform);

                // Use the exact generated-scene composition order: all
                // inactive children/providers, then scope, then activation.
                root.AddComponent<CrewGalleyFixtureBootstrap>();
                scope = root.AddComponent<WorldSessionScope>();
                root.SetActive(true);
                if (scope.Container == null) scope.Build();

                var session = scope.Container.Resolve<WorldSession>();
                Assert.That(galley.IsBound, Is.True,
                    "The registered component must be injected by its owner scope.");
                Assert.That(scope.Container.Resolve<WorldSession>(), Is.SameAs(session));

                var state = session.Capture().State;
                Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Hunger,
                    Is.EqualTo(950));
                Assert.That(state.CrewDuty.Interactions.Slots, Has.Count.EqualTo(2));
                Assert.That(state.CrewDuty.EffectiveSkill(DutyRole.Engineering, state.Minute),
                    Is.EqualTo(600),
                    "The actual backup must cover engineering while the primary has critical Needs.");
            }
            finally
            {
                if (scope != null) scope.DisposeCore();
                Object.DestroyImmediate(root);
            }
        }
    }
}
