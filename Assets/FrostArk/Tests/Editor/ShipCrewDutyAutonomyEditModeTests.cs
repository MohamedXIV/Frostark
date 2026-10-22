using System;
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
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    public sealed class ShipCrewDutyAutonomyEditModeTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:duty:g2:engineer");
        private static readonly EntityId Room = EntityId.Parse("room:duty:g2");
        private static readonly EntityId Scene = EntityId.Parse("scene:duty:g2");
        private static readonly EntityId Station = EntityId.Parse("module:duty:g2:station");
        private static readonly EntityId Bed = EntityId.Parse("module:duty:g2:bed");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:duty:g2");
        private static readonly EntityId Work = EntityId.Parse("slot:duty:g2:work");
        private static readonly EntityId Rest = EntityId.Parse("slot:duty:g2:rest");
        private static ShipGridCell C(int x, int y) => new ShipGridCell(Room, x, y);

        private sealed class Setup : IDisposable
        {
            public GameObject Root = new GameObject("G2-PhysicalDuties-Ship");
            public GameObject Controller = new GameObject("G2-PhysicalDuties-Controller");
            public WorldSession Session;
            public ShipGridWalker Walker;
            public TacticalCommandController Commander;
            public ShipCrewAgencyCoordinator Agency;
            public ShipCrewDutyAutonomy Duties;

            public Setup(bool offShift = false, int fatigue = 0)
            {
                var layout = ShipLayoutState.Create(new[]
                {
                    new ShipCompartmentSlot(Room, 7, 7)
                }, new[]
                {
                    new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                        1, 1, EntityId.Parse("item:duty:g2:part"), 1, 1)
                }, new[]
                {
                    new ShipModule(Station, Blueprint, Room, 4, 4),
                    new ShipModule(Bed, Blueprint, Room, 1, 4)
                });
                var crew = CrewState.Create(new[]
                {
                    new CrewMember(Engineer, DutyRole.Engineering, 950, 200,
                        offShift ? 720 : 0, offShift ? 400 : 1440,
                        fatigue: fatigue)
                }, interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Work, Station,
                        CrewInteractionVerb.Work, -1, 0, 0),
                    new CrewInteractionSlot(Rest, Bed,
                        CrewInteractionVerb.Rest, 1, 0, 0)
                }));
                Session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                    Array.Empty<SupplyStock>(), new[] { Engineer },
                    crewDuty: crew, layout: layout));
                Session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    Scene, EntityId.Parse("anchor:duty:g2:start")));
                var map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                var actor = new GameObject("G2-Duty-Engineer");
                actor.transform.SetParent(Root.transform, false);
                actor.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                Walker = actor.AddComponent<ShipGridWalker>();
                Walker.Configure(Root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    C(1, 1), 4f);
                var proxy = actor.AddComponent<ShipCrewSelectionProxy>();
                proxy.SetActorId(Engineer);
                proxy.BindScene(Scene);
                Commander = Controller.AddComponent<TacticalCommandController>();
                Commander.SetSession(Session);
                Commander.SetShipSpatialMap(map);
                Commander.RegisterShipActor(proxy);
                Agency = Controller.AddComponent<ShipCrewAgencyCoordinator>();
                Agency.Configure(Session, Commander, map);
                Duties = Controller.AddComponent<ShipCrewDutyAutonomy>();
                Duties.Configure(Session, Commander, Agency, map);

                var assign = Session.Submit(new AssignCrewCommand(
                    EntityId.Parse("cmd:duty:g2:assign"), Engineer, Station,
                    Session.Revision));
                Assert.That(assign.Success, Is.True);
            }

            public void WalkToCompletion()
            {
                for (int i = 0; i < 150 && Walker.IsMoving; i++)
                    Walker.Tick(0.1f);
                Assert.That(Walker.IsMoving, Is.False,
                    "Fixture's permitted local route must complete.");
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Controller);
                UnityEngine.Object.DestroyImmediate(Root);
                Session.Dispose();
            }
        }

        [Test]
        public void DeselectAfterPlayerMoveCompletesReturnsToAuthoredWorkWithoutFakeProgress()
        {
            using (var f = new Setup())
            {
                f.Commander.SelectActor(Engineer);
                f.Agency.Tick();
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);

                Assert.That(f.Commander.IssueShipMoveOrder(C(2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True);
                f.Commander.ClearSelection();
                f.Agency.Tick();
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.False,
                    "An active player Move retains priority after deselection.");
                f.WalkToCompletion();
                f.Agency.Tick();
                f.Duties.Tick();
                Assert.That(f.Duties.ActiveVerb(Engineer),
                    Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Not.Null);
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(3, 4)));
                Assert.That(f.Session.Minute, Is.EqualTo(WorldMinute.Zero),
                    "Physical standing at work must not run a second WorldClock.");
                Assert.That(f.Session.Revision, Is.EqualTo(2),
                    "Only authored station assignment and actual slot claim commit.");
            }
        }

        [Test]
        public void SelectingTravellingWorkerStopsSafelyThenReleasesExclusiveClaim()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.True);
                Assert.That(f.Walker.IsMoving, Is.True);
                f.Walker.Tick(0.08f);
                Assert.That(f.Walker.HasIncomingReservation, Is.True);

                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                Assert.That(f.Walker.HasIncomingReservation, Is.True,
                    "Selection cannot unlock an edge while moving between cells.");
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);
                Assert.That(f.Walker.HasIncomingReservation, Is.False);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                Assert.That(f.Session.Revision, Is.EqualTo(3),
                    "Only assign + claim + release; never synthetic Work effects.");
            }
        }

        [Test]
        public void WaitingForTrafficRetainsWorkClaimAndResumesAfterBlockersClear()
        {
            using (var f = new Setup())
            {
                // Block all forward exits from (1,1), but do NOT block the
                // authored Work goal. Traffic is dynamic, not layout geometry.
                var blockers = new List<GameObject>();
                var map = ShipSpatialMap.Derive(f.Session.Capture().State.Layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                try
                {
                    foreach (var cell in new[] { C(1, 2), C(2, 1), C(2, 2) })
                    {
                        var go = new GameObject("G2-TemporaryDutyTrafficBlocker");
                        go.transform.SetParent(f.Root.transform, false);
                        go.transform.localPosition =
                            new Vector3(cell.X + 0.5f, 0f, cell.Y + 0.5f);
                        go.AddComponent<ShipGridWalker>().Configure(
                            f.Root.transform, map,
                            new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                            cell, 4f);
                        blockers.Add(go);
                    }
                    f.Duties.Tick();
                    Assert.That(f.Duties.HasActivity(Engineer), Is.True);
                    f.Walker.Tick(0.1f);
                    Assert.That(f.Walker.LastError, Is.EqualTo("WaitingForTraffic"));
                    f.Duties.Tick();
                    Assert.That(f.Duties.HasActivity(Engineer), Is.True,
                        "Congestion should not cancel a valid Work order.");
                    Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                        Is.Not.Null, "Traffic wait must preserve exclusive slot ownership.");
                    Assert.That(f.Session.Revision, Is.EqualTo(2));
                }
                finally
                {
                    foreach (var go in blockers) UnityEngine.Object.DestroyImmediate(go);
                }
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(3, 4)));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Not.Null);
            }
        }

        [Test]
        public void AcceptedPlayerOverrideReleasesOldWorkButDoesNotStopNewRoute()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                f.Commander.SelectActor(Engineer);
                Assert.That(f.Commander.IssueShipMoveOrder(C(2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True);
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);
                Assert.That(f.Walker.IsMoving, Is.True,
                    "Old duty release must not cancel the newly accepted player walk.");
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                f.Commander.ClearSelection();
                f.Agency.Tick();
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);
                f.WalkToCompletion();
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(2, 1)));
            }
        }

        [Test]
        public void OffShiftExhaustionUsesAuthoredRestSlotAndOnlyWorldClockRestoresFatigue()
        {
            using (var f = new Setup(offShift: true, fatigue: 700))
            {
                f.Agency.Tick();
                f.Duties.Tick();
                Assert.That(f.Duties.ActiveVerb(Engineer),
                    Is.EqualTo(CrewInteractionVerb.Rest));
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(2, 4)));
                Assert.That(f.Session.Capture().State.CrewDuty.Get(Engineer).Fatigue,
                    Is.EqualTo(700));
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.CrewDuty.Get(Engineer).Fatigue,
                    Is.EqualTo(698), "Only existing world-minute rest applies fatigue recovery.");
            }
        }
    }
}
