using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G2 contract for the E12 persistent composition: one physical crew object
    /// per stable ID, both locomotion motors on that object, one ship and one rig.
    /// </summary>
    public sealed class PersistentWorldEditModeTests
    {
        [Test]
        public void BuildWorld_CreatesOnePersistentGameObjectPerCrewWithDualMotors()
        {
            var root = new GameObject("E12PersistentWorld_EditMode");
            WorldSession session = null;
            try
            {
                session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);

                Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);
                Assert.That(session.SceneCheckpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));

                var actors = runtime.CrewActors.ToArray();
                Assert.That(actors.Length, Is.EqualTo(4));
                Assert.That(actors.Select(a => a.ActorId).Distinct().Count(), Is.EqualTo(4));
                Assert.That(root.GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
                Assert.That(root.GetComponentsInChildren<ShipCrewSelectionProxy>(true).Length,
                    Is.EqualTo(4), "No second ship proxy object may exist.");
                Assert.That(root.GetComponentsInChildren<TacticalActorProxy>(true).Length,
                    Is.EqualTo(4), "No second shore proxy object may exist.");
                Assert.That(root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1), "Exactly one resident ship may exist.");
                var geometry = runtime.ShipRoot.GetComponent<PersistentShipGeometry>();
                Assert.That(geometry, Is.Not.Null,
                    "The ship must be an authored prefab, not rebuilt geometry.");
                Assert.That(geometry.Rooms.Length, Is.EqualTo(2));
                Assert.That(geometry.Doors.Length, Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<ShipRoomClickSurface>(true).Length,
                    Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(1));

                foreach (var actor in actors)
                {
                    var go = actor.gameObject;
                    Assert.That(go.GetComponents<ShipGridWalker>().Length, Is.EqualTo(1));
                    Assert.That(go.GetComponents<ShipCrewSelectionProxy>().Length, Is.EqualTo(1));
                    Assert.That(go.GetComponents<NavMeshAgent>().Length, Is.EqualTo(1));
                    Assert.That(go.GetComponents<TacticalActorProxy>().Length, Is.EqualTo(1));
                    Assert.That(actor.ShipMotor.enabled, Is.True);
                    Assert.That(actor.ShoreAgent.enabled, Is.False,
                        "NavMeshAgent must be off while the actor is aboard.");
                    Assert.That(actor.IsOnShip, Is.True);
                }

                Assert.That(runtime.Commander.ShipActors.Count, Is.EqualTo(4));
                Assert.That(runtime.Commander.Actors.Count, Is.EqualTo(4));
                Assert.That(runtime.Commander.ShipActors.All(a => a.Walker.IsConfigured),
                    Is.True, "Every ship motor must bind to the canonical grid map.");
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                session?.Dispose();
            }
        }

        [Test]
        public void BuildWorld_IsIdempotentAndNeverDuplicatesShipOrCrew()
        {
            var root = new GameObject("E12PersistentWorld_Idempotence");
            WorldSession session = null;
            try
            {
                session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                runtime.BuildWorld(session);

                Assert.That(root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
                Assert.That(root.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<Light>(true).Length, Is.EqualTo(1));
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                session?.Dispose();
            }
        }

        [Test]
        public void BuildWorld_RejectsForeignKeyPhysicalCheckpoint()
        {
            var root = new GameObject("E12PersistentWorld_ForeignCheckpoint");
            WorldSession session = null;
            try
            {
                var captain = FrostArk.Core.World.EntityId.Parse(
                    PersistentWorldRuntime.CaptainId);
                session = new WorldSession(
                    PersistentWorldRuntime.CreateDefaultState(captainAway: true));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    FrostArk.Core.World.EntityId.Parse("scene:shore:foreign"),
                    FrostArk.Core.World.EntityId.Parse(PersistentWorldRuntime.ShoreSiteId),
                    FrostArk.Core.World.EntityId.Parse(PersistentWorldRuntime.ShoreAnchorId),
                    new[] { captain }));
                var runtime = root.AddComponent<PersistentWorldRuntime>();

                Assert.Throws<InvalidOperationException>(() => runtime.BuildWorld(session));

                Assert.That(runtime.IsReady, Is.False);
                Assert.That(root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true),
                    Is.Empty);
                Assert.That(root.GetComponentsInChildren<PersistentCrewActor>(true),
                    Is.Empty);
            }
            finally
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
                session?.Dispose();
            }
        }
    }
}
