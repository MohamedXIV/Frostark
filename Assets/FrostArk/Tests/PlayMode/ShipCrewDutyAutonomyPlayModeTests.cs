using System;
using System.Collections;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Real Unity-frame acceptance for physical duty choreography. Core
    /// WorldClock is never driven by rendering, and all work-slot claims
    /// are owned by the canonical WorldSession.
    /// </summary>
    public sealed class ShipCrewDutyAutonomyPlayModeTests
    {
        private static readonly EntityId Crew = EntityId.Parse("crew:duty:g3:engineer");
        private static readonly EntityId Room = EntityId.Parse("room:duty:g3");
        private static readonly EntityId Scene = EntityId.Parse("scene:duty:g3");
        private static readonly EntityId Station = EntityId.Parse("module:duty:g3:engine");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:duty:g3");
        private static readonly EntityId Slot = EntityId.Parse("slot:duty:g3:work");

        [UnityTest]
        public IEnumerator EngineerReturnsToAuthoredStationAfterRealFramePlayerMove()
        {
            var root = new GameObject("G3-Duty-Ship");
            WorldSession session = null;
            try
            {
                var layout = ShipLayoutState.Create(
                    new[] { new ShipCompartmentSlot(Room, 7, 7) },
                    new[] { new ShipModuleBlueprint(Blueprint,
                        ShipModuleKind.Auxiliary, 1, 1,
                        EntityId.Parse("item:duty:g3:part"), 1, 1) },
                    new[] { new ShipModule(Station, Blueprint, Room, 4, 4) });
                var crew = CrewState.Create(new[]
                {
                    new CrewMember(Crew, DutyRole.Engineering, 950, 200, 0, 1440)
                }, interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Slot, Station,
                        CrewInteractionVerb.Work, -1, 0, 0)
                }));
                session = new WorldSession(WorldState.Create(
                    WorldMinute.Zero, Array.Empty<SupplyStock>(),
                    new[] { Crew }, crewDuty: crew, layout: layout));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    Scene, EntityId.Parse("anchor:duty:g3:start")));

                var map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                var actor = new GameObject("G3-Engineer");
                actor.transform.SetParent(root.transform, false);
                actor.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                var walker = actor.AddComponent<ShipGridWalker>();
                walker.Configure(root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    new ShipGridCell(Room, 1, 1), 8f);
                var proxy = actor.AddComponent<ShipCrewSelectionProxy>();
                proxy.SetActorId(Crew);
                proxy.BindScene(Scene);

                var control = new GameObject("G3-Duty-Controller");
                control.transform.SetParent(root.transform, false);
                var commander = control.AddComponent<TacticalCommandController>();
                commander.SetSession(session);
                commander.SetShipSpatialMap(map);
                commander.RegisterShipActor(proxy);
                var agency = control.AddComponent<ShipCrewAgencyCoordinator>();
                agency.Configure(session, commander, map);
                var duty = control.AddComponent<ShipCrewDutyAutonomy>();
                duty.Configure(session, commander, agency, map);
                Assert.That(session.Submit(new AssignCrewCommand(
                    EntityId.Parse("cmd:duty:g3:assign"), Crew, Station,
                    session.Revision)).Success, Is.True);

                yield return null; // Actual Unity Update starts autonomous Work.
                Assert.That(duty.HasActivity(Crew), Is.True);
                Assert.That(duty.ActiveVerb(Crew), Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(session.Capture().State.CrewDuty.Interactions.GetClaim(Slot),
                    Is.Not.Null);

                var workCell = new ShipGridCell(Room, 3, 4);
                for (int frame = 0; frame < 420 && walker.IsMoving; frame++)
                    yield return null;
                Assert.That(walker.IsMoving, Is.False,
                    "Real frame-based crew movement must finish without a test Tick shortcut.");
                Assert.That(walker.LastReachedCell, Is.EqualTo(workCell));
                Assert.That(duty.IsStationed(Crew), Is.True);
                Assert.That(session.Revision, Is.EqualTo(2),
                    "Standing near a station never grants unearned world work.");
                Assert.That(session.Minute, Is.EqualTo(WorldMinute.Zero));

                commander.SelectActor(Crew);
                Assert.That(commander.IssueShipMoveOrder(
                    new ShipGridCell(Room, 2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True);
                Assert.That(duty.HasActivity(Crew), Is.False);
                Assert.That(walker.IsMoving, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty, "Accepted player order releases only superseded Work claim.");

                commander.ClearSelection();
                var playerGoal = new ShipGridCell(Room, 2, 1);
                bool sawPlayerArrival = false;
                for (int frame = 0; frame < 420 &&
                    agency.HasActivePlayerMove(Crew); frame++)
                {
                    yield return null;
                    sawPlayerArrival |= walker.LastReachedCell == playerGoal;
                }
                agency.Tick();
                Assert.That(sawPlayerArrival, Is.True,
                    "Accepted player Move must physically arrive before resuming duty.");
                Assert.That(agency.HasActivePlayerMove(Crew), Is.False);
                // The scene's agency+duty Updates may already have resumed
                // Work on the same or next frame; don't assume the walker
                // remains idle for a test-only intermediate assertion.
                yield return null;
                Assert.That(duty.HasActivity(Crew), Is.True,
                    "Idle, unselected worker should return to assigned Work.");
                for (int frame = 0; frame < 420 && walker.IsMoving; frame++)
                    yield return null;
                Assert.That(walker.LastReachedCell, Is.EqualTo(workCell));
                Assert.That(duty.IsStationed(Crew), Is.True);
                Assert.That(session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                Assert.That(session.Minute, Is.EqualTo(WorldMinute.Zero));
                Assert.That(session.Revision, Is.EqualTo(4),
                    "Only one assignment, two claims and one release were committed.");
            }
            finally
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                session?.Dispose();
            }
        }
    }
}
