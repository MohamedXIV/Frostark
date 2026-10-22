using System;
using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

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

        [UnityTest]
        public IEnumerator AcceptedMoveSurvivesRepositorySaveAndRealSceneUnloadAndReload()
        {
            var load = SceneManager.LoadSceneAsync("ShipRtsDemo", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            yield return load;
            yield return null;

            var original = UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>();
            Assert.That(original, Is.Not.Null);
            Assert.That(original.IsReady, Is.True);
            var actor = UnityEngine.Object.FindObjectsByType<ShipCrewSelectionProxy>(
                FindObjectsSortMode.None).First(x => x.ActorId.Value == "crew:demo:captain");
            var start = actor.Walker.LastReachedCell;
            var goal = new ShipGridCell(
                EntityId.Parse(ShipRtsDemoRuntime.BridgeRoomId), 4, 1);
            original.Commander.SelectActor(actor.ActorId);
            Assert.That(original.Commander.IssueShipMoveOrder(
                goal, new Vector3(4.5f, 0f, 1.5f)), Is.True);
            actor.Walker.Tick(0.08f);
            Assert.That(actor.Walker.HasIncomingReservation, Is.True,
                "The save must be taken while the accepted edge is in flight.");

            var store = new MemorySaveStore();
            var repository = new SaveRepository(store);
            repository.Save(original.Session.Capture());
            var saved = repository.Load();
            Assert.That(saved.Success, Is.True);
            Assert.That(saved.Value.PendingCrewMoves.Count, Is.EqualTo(1));
            Assert.That(saved.Value.PendingCrewMoves[0].ShipGoal, Is.EqualTo(goal));

            yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            Assert.That(UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>(),
                Is.Null, "The old scene and its transient physical actors must be unloaded.");

            yield return SceneManager.LoadSceneAsync("ShipRtsDemo", LoadSceneMode.Single);
            yield return null;
            var reloaded = UnityEngine.Object.FindFirstObjectByType<ShipRtsDemoRuntime>();
            Assert.That(reloaded, Is.Not.Null);
            Assert.That(reloaded.IsReady, Is.True);
            var freshSession = reloaded.Session;
            Assert.That(freshSession, Is.Not.SameAs(original.Session));
            var loaded = repository.Load();
            Assert.That(loaded.Success, Is.True);

            var scopeRoot = new GameObject("G3-Restored-Ship-Run");
            scopeRoot.SetActive(false);
            var scope = scopeRoot.AddComponent<WorldSessionScope>();
            scope.SetRestoreSnapshot(loaded.Value);
            scopeRoot.SetActive(true);
            if (scope.Container == null)
                scope.Build();
            var restored = scope.Container.Resolve<WorldSession>();
            Assert.That(restored, Is.Not.SameAs(freshSession));
            Assert.That(restored.Revision, Is.Zero);

            var restoredActor = UnityEngine.Object.FindObjectsByType<ShipCrewSelectionProxy>(
                FindObjectsSortMode.None).First(x => x.ActorId == actor.ActorId);
            var agency = reloaded.Commander.GetComponent<ShipCrewAgencyCoordinator>();
            Assert.That(agency, Is.Not.Null);
            Assert.That(reloaded.Commander.Selection.Count, Is.Zero,
                "Mouse selection is presentation-only and must not be restored.");
            Assert.That(restoredActor.Walker.LastReachedCell, Is.EqualTo(start),
                "Reentry starts at the authored safe spawn, not the saved midpoint.");
            Assert.That(restoredActor.Walker.HasIncomingReservation, Is.False,
                "No transient grid reservation may cross the save boundary.");
            Assert.That(agency.HasActivePlayerMove(actor.ActorId), Is.True);
            Assert.That(restoredActor.Walker.IsMoving, Is.True);
            Assert.That(restored.PendingCrewMoves.Count, Is.EqualTo(1));

            freshSession.Dispose();
            int worldChanges = 0;
            restored.Changed += _ => worldChanges++;

            for (int frame = 0; frame < 360 && restoredActor.Walker.IsMoving; frame++)
                yield return null;
            agency.Tick();

            Assert.That(restoredActor.Walker.IsMoving, Is.False);
            Assert.That(restoredActor.Walker.LastReachedCell, Is.EqualTo(goal));
            Assert.That(agency.HasActivePlayerMove(actor.ActorId), Is.False);
            Assert.That(restored.PendingCrewMoves, Is.Empty);
            Assert.That(restored.Revision, Is.Zero);
            Assert.That(worldChanges, Is.Zero,
                "Replaying a physical Move must not fabricate canonical commands.");
            Assert.That(restored.Capture().State.CrewDuty.Interactions.Claims,
                Is.Empty);
            Assert.That(UnityEngine.Object.FindObjectsByType<ShipCrewSelectionProxy>(
                FindObjectsSortMode.None).Length, Is.EqualTo(4));
        }

        private sealed class MemorySaveStore : ISaveBlobStore
        {
            private byte[] _primary;
            private byte[] _backup;

            public byte[] ReadPrimary() => _primary == null
                ? Array.Empty<byte>() : (byte[])_primary.Clone();

            public byte[] ReadBackup() => _backup == null
                ? Array.Empty<byte>() : (byte[])_backup.Clone();

            public void Commit(byte[] primary, byte[] previousValidPrimary)
            {
                if (primary == null) throw new ArgumentNullException(nameof(primary));
                _backup = previousValidPrimary == null
                    ? null : (byte[])previousValidPrimary.Clone();
                _primary = (byte[])primary.Clone();
            }
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
