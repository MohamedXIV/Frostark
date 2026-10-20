using System.Collections;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Runs the real inactive-root scene composition path. Unlike the isolated
    /// controller tests, this never manually calls Controller.Construct().
    /// The active LifetimeScope must resolve/inject the unique WorldSession.
    /// G3 remains unverified until Unity executes this authored test.
    /// </summary>
    public sealed class CrewGalleyScopedPlayModeTests
    {
        private GameObject _root;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
            _root = null;
        }

        [UnityTest]
        public IEnumerator GeneratedSceneCompositionInjectsOneWorldAndRunsMealJourney()
        {
            _root = new GameObject("ScopedCrewGalleyPlayMode");
            _root.SetActive(false);

            var actor = new GameObject("Engineer");
            actor.transform.SetParent(_root.transform, false);
            var galley = new GameObject("Galley");
            galley.transform.SetParent(_root.transform, false);
            var grid = CrewGalleyFixtureState.Grid();
            galley.transform.position = grid.WorldCenter(
                CrewGalleyFixtureState.GalleyCell, 0.5f);

            var presenter = new GameObject("WorldSessionPresenter");
            presenter.transform.SetParent(_root.transform, false);
            presenter.AddComponent<WorldSessionPresenter>();

            var controllerObject = new GameObject("CrewGalleyController");
            controllerObject.transform.SetParent(_root.transform, false);
            var controller = controllerObject.AddComponent<CrewGalleyController>();
            controller.Configure(actor.transform, galley.transform, speed: 40f);

            _root.AddComponent<CrewGalleyFixtureBootstrap>();
            var scope = _root.AddComponent<WorldSessionScope>();
            _root.SetActive(true);
            yield return null;

            Assert.That(scope.Container, Is.Not.Null, "VContainer must build.");
            var worldSession = scope.Container.Resolve<WorldSession>();
            Assert.That(controller.IsBound, Is.True,
                "Galley must receive the scoped session through VContainer.");
            Assert.That(controller.HeldSlot.IsValid, Is.True,
                "The injected session must plan and reserve an urgent interaction.");
            Assert.That(worldSession.Capture().State.CrewDuty.Get(
                CrewGalleyFixtureState.Engineer).Thirst, Is.EqualTo(960),
                "The fixture world must not silently fall back to an empty session.");

            for (var i = 0; i < 2000 && !controller.IsPaused; i++)
                yield return null;

            var state = worldSession.Capture().State;
            Assert.That(controller.IsPaused, Is.True,
                "Complete the physical path and the provision actions.");
            Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Thirst,
                Is.EqualTo(310));
            Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Hunger,
                Is.EqualTo(350));
            Assert.That(state.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(3));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Water), Is.EqualTo(3));
        }
    }
}
