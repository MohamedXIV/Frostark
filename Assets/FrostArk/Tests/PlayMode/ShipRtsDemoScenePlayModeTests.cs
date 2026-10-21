using System.Collections;
using System.Linq;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G3 loads the exact checked-in ship demo scene through Build Settings.
    /// Exercises actual runtime-built world and the production RTS camera
    /// context path; does not fake a second WorldSession or use direct Move.
    /// Must run in Unity Editor/PlayMode to count as passing evidence.
    /// </summary>
    public sealed class ShipRtsDemoScenePlayModeTests
    {
        [UnityTest]
        public IEnumerator OpenCheckedInSceneThenSelectAndMoveAcrossRooms()
        {
            var load = SceneManager.LoadSceneAsync("ShipRtsDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "Tracked scene is in EditorBuildSettings.");
            yield return load;
            yield return null; // Start() builds demo and binds the physical actors.

            var demo = UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.IsReady, Is.True);
            Assert.That(demo.Session.SceneCheckpoint.Theater,
                Is.EqualTo(PhysicalTheater.Ship));
            var commander = demo.Commander;
            var actors = UnityEngine.Object
                .FindObjectsByType<ShipCrewSelectionProxy>(FindObjectsSortMode.None)
                .OrderBy(x => x.ActorId.Value).ToArray();
            Assert.That(actors.Length, Is.EqualTo(4));
            Assert.That(actors.All(x => x.Walker.IsConfigured), Is.True);

            commander.SelectActor(actors[0].ActorId);
            commander.SelectActor(actors[1].ActorId, addToSelection: true);
            var camera = UnityEngine.Camera.main;
            Assert.That(camera, Is.Not.Null);
            Physics.SyncTransforms();
            var target = camera.WorldToScreenPoint(new Vector3(8.5f, 0f, 2.5f));
            Assert.That(commander.ProcessContextClick(target), Is.True);
            Assert.That(commander.LastTheaterMoveDispatch.Count, Is.EqualTo(2));
            Assert.That(commander.LastTheaterMoveDispatch.Values.All(x => x.Accepted), Is.True);
            Assert.That(actors.Count(x => x.Walker.IsMoving), Is.EqualTo(2));
            // Deterministic PlayMode tick: let both walkers reserve a step
            // without depending on Editor focus/runInBackground.
            actors[0].Walker.Tick(0.2f);
            actors[1].Walker.Tick(0.2f);
            Assert.That(actors.Take(2).All(x => x.Walker.HasIncomingReservation),
                Is.True, "Both ordered actors should be mid-edge before closure.");

            commander.ClearSelection();
            Assert.That(commander.Selection.Count, Is.Zero);
            // Door closure must immediately invalidate travel for those still
            // in the bridge room. It must not mutate the game's WorldRevision.
            demo.SetDoorOpen(false);
            Assert.That(actors.All(x => !x.Walker.IsMoving), Is.True);
            Assert.That(actors.Take(2).All(x => x.Walker.LastError == "RouteInvalidated"),
                Is.True, "Only the two ordered actors had a route to invalidate.");
            Assert.That(actors.Skip(2).All(x => x.Walker.LastError == null),
                Is.True, "Idle crew must not acquire a false route failure.");
            Assert.That(actors.All(x => x.Walker.LastReachedCell.RoomId.Value !=
                    ShipRtsDemoRuntime.EngineRoomId),
                Is.True, "Closing the bulkhead must stop every actor on the bridge side.");
            Assert.That(actors.Any(x => x.Walker.IsRecovering), Is.True,
                "At least one ordered actor was between cells when the door closed.");
            for (int i = 0; i < 30 && actors.Any(x => x.Walker.IsRecovering); i++)
                foreach (var actor in actors) actor.Walker.Tick(0.1f);
            Assert.That(actors.Any(x => x.Walker.IsRecovering), Is.False);
            Assert.That(actors.All(x => !x.Walker.HasIncomingReservation), Is.True,
                "Recovery must release the doorway reservation before a new order.");
            Assert.That(demo.Session.Revision, Is.Zero);
            demo.SetDoorOpen(true);

            commander.SelectActor(actors[0].ActorId);
            Assert.That(commander.ProcessContextClick(target), Is.True,
                "A recovered actor accepts a fresh route after the door reopens.");
            yield return null;
            Assert.That(actors[0].Walker.IsMoving, Is.True);
            Assert.That(demo.Session.Revision, Is.Zero);
        }

        [UnityTest]
        public IEnumerator SelectedCrewWaitsForPlayerAndDeselectDoesNotCancelAcceptedWalk()
        {
            var load = SceneManager.LoadSceneAsync("ShipRtsDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;
            var demo = UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>();
            Assert.That(demo, Is.Not.Null);
            Assert.That(demo.IsReady, Is.True);
            var actor = UnityEngine.Object.FindObjectsByType<ShipCrewSelectionProxy>(
                FindObjectsSortMode.None).First(a => a.ActorId.Value == "crew:demo:captain");
            var agency = demo.Commander.GetComponent<ShipCrewAgencyCoordinator>();
            Assert.That(agency, Is.Not.Null,
                "Checked-in scene bootstrap must attach one ship crew agency coordinator.");
            demo.Commander.SelectActor(actor.ActorId);
            agency.Tick();
            Assert.That(agency.LastDecision(actor.ActorId),
                Is.EqualTo(FrostArk.Core.Crew.CrewAgencyDecisionKind.HoldForPlayerSelection));
            Assert.That(demo.Commander.IssueShipMoveOrder(
                new ShipGridCell(FrostArk.Core.World.EntityId.Parse(
                    ShipRtsDemoRuntime.BridgeRoomId), 4, 1),
                new Vector3(4.5f, 0f, 1.5f)), Is.True);
            Assert.That(agency.HasActivePlayerMove(actor.ActorId), Is.True);
            demo.Commander.ClearSelection();
            agency.Tick();
            Assert.That(actor.Walker.IsMoving, Is.True);
            Assert.That(agency.HasActivePlayerMove(actor.ActorId), Is.True);
            for (int frame = 0; frame < 240 && actor.Walker.IsMoving; frame++)
                yield return null;
            agency.Tick();
            Assert.That(actor.Walker.IsMoving, Is.False);
            Assert.That(actor.Walker.LastReachedCell.X, Is.EqualTo(4));
            Assert.That(actor.Walker.LastReachedCell.Y, Is.EqualTo(1));
            Assert.That(agency.HasActivePlayerMove(actor.ActorId), Is.False);
            Assert.That(demo.Session.Revision, Is.Zero,
                "Transient selection and physical walk cannot manufacture world commands.");
        }

        [UnityTearDown]
        public IEnumerator RestoreStarterScene()
        {
            // Isolate the named-scene load from other PlayMode suites.
            var restore = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            if (restore != null) yield return restore;
        }
    }
}
