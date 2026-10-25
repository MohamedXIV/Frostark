using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Real PlayMode proof that the persistent object graph can be selected and
    /// moved through the actual camera/physics command path using the ship grid.
    /// </summary>
    public sealed class PersistentWorldPlayModeTests
    {
        [UnityTest]
        public IEnumerator BuildWorld_SameCrewInstanceSelectedAndMovedThroughGameView()
        {
            var root = new GameObject("E12PersistentWorld_PlayMode");
            var session = new WorldSession(PersistentWorldRuntime.CreateDefaultState());
            try
            {
                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                yield return null;

                Assert.That(runtime.IsReady, Is.True, runtime.InitializationError);
                var actors = runtime.CrewActors.ToArray();
                Assert.That(actors.Length, Is.EqualTo(4));
                var captain = actors.First(a => a.ActorId == EntityId.Parse(
                    PersistentWorldRuntime.CaptainId));
                var captainInstance = captain.gameObject;
                var commander = runtime.Commander;
                var camera = runtime.GameplayCamera;
                Assert.That(camera, Is.Not.Null);

                Physics.SyncTransforms();
                var actorScreen = camera.WorldToScreenPoint(captain.transform.position);
                Assert.That(commander.ProcessPointerClick(actorScreen), Is.True,
                    "The one crew object must be selectable through the gameplay camera.");
                Assert.That(commander.Selection.IsSelected(captain.ActorId), Is.True);

                Physics.SyncTransforms();
                var enginePoint = new Vector3(8.5f, 0f, 2.5f);
                var engineScreen = camera.WorldToScreenPoint(enginePoint);
                Assert.That(commander.ProcessContextClick(engineScreen), Is.True,
                    "A ship-floor context click must route to the grid motor.");

                var timeout = 5f;
                while (captain.ShipMotor.IsMoving && timeout > 0f)
                {
                    yield return null;
                    timeout -= Time.deltaTime;
                }

                Assert.That(captain.gameObject, Is.SameAs(captainInstance),
                    "Ship movement must not recreate the actor object.");
                Assert.That(captain.ShipMotor.LastReachedCell.RoomId,
                    Is.EqualTo(EntityId.Parse(PersistentWorldRuntime.EngineRoomId)));
                Assert.That(captain.ShoreAgent.enabled, Is.False);
                Assert.That(root.GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
                Assert.That(root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
            }
            finally
            {
                Object.Destroy(root);
                session.Dispose();
            }
            yield return null;
        }
    }
}
