using System;
using FrostArk.Core.Application;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    [TestFixture]
    public sealed class TacticalSelectionBridgeTests
    {
        [Test]
        public void SelectionController_SelectToggleClear_MaintainsOrderAndEventNotification()
        {
            var controller = new TacticalSelectionController();
            TacticalSelection lastNotified = null;
            controller.SelectionChanged += s => lastNotified = s;

            var idA = EntityId.Parse("actor:protagonist");
            var idB = EntityId.Parse("actor:companion");

            Assert.That(controller.Count, Is.Zero);
            Assert.That(controller.IsSelected(idA), Is.False);

            // 1. Select solo actor
            controller.Select(idA);
            Assert.That(controller.Count, Is.EqualTo(1));
            Assert.That(controller.IsSelected(idA), Is.True);
            Assert.That(lastNotified, Is.Not.Null);
            Assert.That(lastNotified.Contains(idA), Is.True);

            // 2. Toggle to include second actor
            controller.Toggle(idB);
            Assert.That(controller.Count, Is.EqualTo(2));
            Assert.That(controller.IsSelected(idA), Is.True);
            Assert.That(controller.IsSelected(idB), Is.True);

            // 3. Toggle off primary actor
            controller.Toggle(idA);
            Assert.That(controller.Count, Is.EqualTo(1));
            Assert.That(controller.IsSelected(idA), Is.False);
            Assert.That(controller.IsSelected(idB), Is.True);

            // 4. Clear
            controller.Clear();
            Assert.That(controller.Count, Is.Zero);
            Assert.That(controller.Current.Count, Is.Zero);
        }

        [Test]
        public void CommandController_MoveOrder_BuildsIntentAndDispatchesToActors()
        {
            var root = new GameObject("TestRoot_Move");
            try
            {
                var actorObj = new GameObject("TestActor");
                actorObj.transform.SetParent(root.transform);
                var proxy = actorObj.AddComponent<TacticalActorProxy>();
                proxy.SetActorId(EntityId.Parse("actor:hero"));

                var cmdObj = new GameObject("TestCommander");
                cmdObj.transform.SetParent(root.transform);
                var commander = cmdObj.AddComponent<TacticalCommandController>();
                commander.RegisterActor(proxy);

                // No selection: order rejected
                bool orderedWithoutSelection = commander.IssueMoveOrder(new Vector3(10, 0, 20));
                Assert.That(orderedWithoutSelection, Is.False);
                Assert.That(commander.LastIssuedIntent, Is.Null);

                // Select hero: order accepted and intent built
                commander.SelectActor(EntityId.Parse("actor:hero"));
                bool ordered = commander.IssueMoveOrder(new Vector3(10, 0, 20));
                Assert.That(ordered, Is.True);
                Assert.That(commander.LastIssuedIntent, Is.Not.Null);
                Assert.That(commander.LastIssuedIntent.Kind, Is.EqualTo(TacticalOrderKind.Move));
                Assert.That(commander.LastIssuedIntent.DestinationX, Is.EqualTo(10.0));
                Assert.That(commander.LastIssuedIntent.DestinationZ, Is.EqualTo(20.0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CommandController_InteractOrder_BuildsIntentAndTriggersContextualInteraction()
        {
            var root = new GameObject("TestRoot_Interact");
            try
            {
                var actorObj = new GameObject("TestActor");
                actorObj.transform.SetParent(root.transform);
                actorObj.transform.position = Vector3.zero;
                var actor = actorObj.AddComponent<TacticalActorProxy>();
                actor.SetActorId(EntityId.Parse("actor:hero"));

                var crateObj = new GameObject("TestCrate");
                crateObj.transform.SetParent(root.transform);
                crateObj.transform.position = new Vector3(1.0f, 0, 0); // Within interaction range
                var crate = crateObj.AddComponent<TacticalInteractableProxy>();
                crate.SetTargetId(EntityId.Parse("salvage:box_01"));
                crate.SetInteractionRange(2.0f);

                var cmdObj = new GameObject("TestCommander");
                cmdObj.transform.SetParent(root.transform);
                var commander = cmdObj.AddComponent<TacticalCommandController>();
                commander.RegisterActor(actor);
                commander.RegisterInteractable(crate);

                // Track session command submission
                var session = new WorldSession();
                commander.SetSession(session);

                commander.SelectActor(actor.ActorId);
                bool ordered = commander.IssueInteractOrder(crate);

                Assert.That(ordered, Is.True);
                Assert.That(commander.LastIssuedIntent, Is.Not.Null);
                Assert.That(commander.LastIssuedIntent.Kind, Is.EqualTo(TacticalOrderKind.Interact));
                Assert.That(commander.LastIssuedIntent.TargetId, Is.EqualTo(EntityId.Parse("salvage:box_01")));

                // Because actor is within interaction range, interaction completes
                Assert.That(crate.IsDepleted, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
