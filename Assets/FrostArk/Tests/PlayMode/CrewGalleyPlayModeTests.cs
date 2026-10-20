using System.Collections;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Real Unity PlayMode validation (G3) of physically interpolated arrival.
    /// Authored tests, NOT passing evidence until Unity executes them.
    /// </summary>
    public sealed class CrewGalleyPlayModeTests
    {
        private GameObject _root;
        private CrewGalleyController _controller;
        private WorldSession _session;
        private Transform _actor;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("GalleyPlayModeTestRoot");
            var actor = new GameObject("Engineer");
            actor.transform.SetParent(_root.transform, false);
            _actor = actor.transform;

            var source = new GameObject("Galley");
            source.transform.SetParent(_root.transform, false);
            var grid = CrewGalleyFixtureState.Grid();
            source.transform.position = grid.WorldCenter(
                CrewGalleyFixtureState.GalleyCell, 0.5f);

            _session = new WorldSession(CrewGalleyFixtureState.CreateDefaultState());
            _controller = _root.AddComponent<CrewGalleyController>();
            _controller.Configure(actor.transform, source.transform, speed: 40f);
            _controller.Construct(_session);
            yield return null; // Unity Start must establish authored slots and route.
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
            if (_session != null) _session.Dispose();
            _session = null;
        }

        [UnityTest]
        public IEnumerator CriticalEngineerWalksAroundBulkheadUsesStockAndReturnsToDuty()
        {
            Assert.That(_controller.IsBound, Is.True);
            Assert.That(_controller.IsMoving, Is.True,
                "Critical Needs must trigger a claim and real locomotion.");
            var start = _session.Capture().State;
            Assert.That(start.CrewDuty.Interactions.Claims, Has.Count.EqualTo(1));
            Assert.That(start.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(4));

            // Real Unity frames, not a fabricated Core-only path completion.
            for (int frame = 0; frame < 600 && !_controller.IsPaused; frame++)
                yield return null;

            var world = _session.Capture().State;
            Assert.That(_controller.IsPaused, Is.True,
                "Movement never finished: " + _controller.Status);
            Assert.That(_controller.IsMoving, Is.False);
            Assert.That(world.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Hunger,
                Is.EqualTo(350));
            Assert.That(world.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Thirst,
                Is.EqualTo(310));
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(3));
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Water), Is.EqualTo(3));
            Assert.That(world.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(world.CrewDuty.EffectiveSkill(DutyRole.Engineering,
                world.Minute), Is.EqualTo(900));
            Assert.That(world.Revision, Is.EqualTo(4),
                "Two claims and two atomic uses; no duplicate release commands.");
            Assert.That(Vector3.Distance(_actor.position,
                CrewGalleyFixtureState.Grid().WorldCenter(
                    CrewGalleyFixtureState.StartCell, 1f)), Is.LessThan(0.2f));
        }

        [UnityTest]
        public IEnumerator CancelWhileMovingReleasesClaimWithoutSpendingFoodOrWater()
        {
            Assert.That(_controller.HeldSlot.IsValid, Is.True);
            var first = _session.Capture().State;
            Assert.That(first.CrewDuty.Interactions.Claims, Has.Count.EqualTo(1));
            _controller.CancelTrip();
            yield return null;

            var result = _session.Capture().State;
            Assert.That(_controller.IsPaused, Is.True);
            Assert.That(result.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(result.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(4));
            Assert.That(result.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Water), Is.EqualTo(4));
            Assert.That(result.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Thirst,
                Is.EqualTo(960));
            Assert.That(result.Revision, Is.EqualTo(2),
                "Claim and cancel only; no Use command should have committed.");
        }
    }
}
