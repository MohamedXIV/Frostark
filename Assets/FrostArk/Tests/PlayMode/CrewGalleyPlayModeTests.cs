using System.Collections;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.World;
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
            // Slow initial speed makes the cancellation boundary observable
            // instead of relying on arbitrary editor frame duration.
            _controller.Configure(actor.transform, source.transform, speed: 0.25f);
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
            var galley = _root.transform.Find("Galley");
            _controller.Configure(_actor, galley, speed: 40f);
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
        public IEnumerator CancelMidStepThenSwitchNeedCompletesSegmentWithoutTeleport()
        {
            var source = _root.transform.Find("Galley");
            Assert.That(source, Is.Not.Null);
            // Continue at slow speed until the actor is physically between
            // authored cell centers. No dependence on one particular FPS.
            var gridForWait = CrewGalleyFixtureState.Grid();
            for (int frame = 0; frame < 60 &&
                Vector3.Distance(_actor.position,
                    gridForWait.WorldCenter(_controller.CurrentCell, 1f)) <= 0.001f;
                frame++)
                yield return null;

            var grid = CrewGalleyFixtureState.Grid();
            var originalCell = _controller.CurrentCell;
            var drinkRoute = grid.FindPath(originalCell,
                CrewGalleyFixtureState.DrinkCell);
            Assert.That(drinkRoute.Count, Is.GreaterThan(1));
            var enteredCell = drinkRoute[1];
            var enteredCenter = grid.WorldCenter(enteredCell, 1f);
            var before = _actor.position;
            Assert.That(Vector3.Distance(before,
                grid.WorldCenter(originalCell, 1f)), Is.GreaterThan(0.001f),
                "The actor must be interrupted in the middle of its movement.");
            float distanceBefore = Vector3.Distance(before, enteredCenter);

            _controller.CancelTrip();
            Assert.That(_actor.position, Is.EqualTo(before),
                "Cancelling must not warp the actor to a grid center.");
            Assert.That(_session.Capture().State.CrewDuty.Interactions.Claims, Is.Empty);

            // The previous thirst goal is now satisfied by a valid direct
            // fixture domain command. The next goal must change to hunger.
            var drank = _session.Submit(new ConsumeCrewProvisionCommand(
                EntityId.Parse("cmd:fixture:water-while-paused"),
                CrewGalleyFixtureState.Engineer, CrewGalleyFixtureState.Water,
                _session.Revision));
            Assert.That(drank.Success, Is.True);
            Assert.That(_controller.TryStart(), Is.True);
            Assert.That(_controller.HeldSlot, Is.EqualTo(CrewGalleyFixtureState.Eat));
            Assert.That(_actor.position, Is.EqualTo(before),
                "Replanning must not teleport before Unity movement runs.");

            yield return null;
            Assert.That(Vector3.Distance(_actor.position, enteredCenter),
                Is.LessThan(distanceBefore),
                "Finish the safe underway step before turning to the new galley slot.");
        }

        [UnityTest]
        public IEnumerator MovedGalleySourceInvalidatesClaimWithoutFreeFood()
        {
            Assert.That(_controller.HeldSlot.IsValid, Is.True);
            var source = _root.transform.Find("Galley");
            Assert.That(source, Is.Not.Null);
            source.position += Vector3.right * 1.2f;

            // A moving/resized ship prop must invalidate an authored standing
            // anchor rather than letting the actor use stale coordinates.
            for (int i = 0; i < 10 && !_controller.IsPaused; i++)
                yield return null;

            var state = _session.Capture().State;
            Assert.That(_controller.IsPaused, Is.True);
            Assert.That(_controller.IsMoving, Is.False);
            Assert.That(state.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(4));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Water), Is.EqualTo(4));
            Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Thirst,
                Is.EqualTo(960));
        }

        [UnityTest]
        public IEnumerator RemovedGalleySourceReleasesClaimWithoutSpendingFood()
        {
            Assert.That(_controller.HeldSlot.IsValid, Is.True);
            var source = _root.transform.Find("Galley");
            Assert.That(source, Is.Not.Null);
            Object.Destroy(source.gameObject);
            // Unity destroys objects after the current update. Wait until the
            // controller has observed the missing source in a following frame.
            for (int frame = 0; frame < 10 && !_controller.IsPaused; frame++)
                yield return null;
            var state = _session.Capture().State;
            Assert.That(_controller.IsMoving, Is.False);
            Assert.That(_controller.IsPaused, Is.True);
            Assert.That(state.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Ration), Is.EqualTo(4));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold,
                CrewGalleyFixtureState.Water), Is.EqualTo(4));
            Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Hunger,
                Is.EqualTo(950));
            Assert.That(state.CrewDuty.Get(CrewGalleyFixtureState.Engineer).Thirst,
                Is.EqualTo(960));
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
