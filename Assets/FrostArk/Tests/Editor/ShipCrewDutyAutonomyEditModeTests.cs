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
        private static readonly EntityId RepairJobId = EntityId.Parse("repair:duty:g2:job");
        private static readonly EntityId BoilerFuel = EntityId.Parse("supply:fuel");
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

            public Setup(bool offShift = false, int fatigue = 0,
                bool withRepairJob = false, bool withThermal = false)
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
                    new CrewMember(Engineer,
                        withRepairJob ? DutyRole.Repair : DutyRole.Engineering,
                        950, withRepairJob ? 0 : 200,
                        offShift ? 720 : 0, offShift ? 400 : 1440,
                        fatigue: fatigue)
                }, withRepairJob ?
                    new[] { new RepairJob(RepairJobId, 100) } :
                    Array.Empty<RepairJob>(),
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Work, Station,
                        CrewInteractionVerb.Work, -1, 0, 0),
                    new CrewInteractionSlot(Rest, Bed,
                        CrewInteractionVerb.Rest, 1, 0, 0)
                }));
                var thermal = withThermal ? ShipThermalState.Create(new[]
                {
                    new ThermalCompartment(Room, 1000, 20_000_000,
                        0, 1000, 1000)
                }, Array.Empty<ThermalConnection>(),
                    new BoilerConfiguration(Room, 1, 10_000)) : null;
                Session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                    withThermal ? new[] { new SupplyStock(BoilerFuel, 5) } :
                        Array.Empty<SupplyStock>(),
                    new[] { Engineer },
                    crewDuty: crew, layout: layout, thermal: thermal),
                    ambientMilliC: _ => 20_000);
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
        public void BoilerHeatRequiresStaffedEngineerAtAuthoredWorkStation()
        {
            using (var f = new Setup(withThermal: true))
            {
                f.Duties.Tick();
                Assert.That(f.Walker.IsMoving, Is.True);
                var initialEnergy = f.Session.Capture().State.Thermal.TotalEnergy;
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.Thermal.TotalEnergy,
                    Is.EqualTo(initialEnergy),
                    "Travelling Engineering crew cannot produce observed boiler heat.");
                Assert.That(f.Session.Capture().State.GetSupply(BoilerFuel), Is.EqualTo(5));

                f.WalkToCompletion();
                f.Duties.Tick();
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.Thermal.TotalEnergy,
                    Is.GreaterThan(initialEnergy));
                Assert.That(f.Session.Capture().State.GetSupply(BoilerFuel), Is.EqualTo(4));

                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                var before = f.Session.Capture().State.Thermal.TotalEnergy;
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.Thermal.TotalEnergy,
                    Is.EqualTo(before),
                    "Explicit player selection interrupts physical Engineering.");
                Assert.That(f.Session.Capture().State.GetSupply(BoilerFuel), Is.EqualTo(4));

                f.Commander.ClearSelection();
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.Thermal.TotalEnergy,
                    Is.GreaterThan(before));
                Assert.That(f.Session.Capture().State.GetSupply(BoilerFuel), Is.EqualTo(3));
                Assert.That(f.Session.Minute.Value, Is.EqualTo(4));
            }
        }

        [Test]
        public void RepairProgressRequiresPhysicalWorkArrivalThenStopsOnPlayerOverride()
        {
            using (var f = new Setup(withRepairJob: true))
            {
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.True);
                Assert.That(f.Walker.IsMoving, Is.True);
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.CrewDuty.GetRepair(RepairJobId)
                    .RemainingWork, Is.EqualTo(100),
                    "A repairer walking to their station cannot repair the hull.");

                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.CrewDuty.GetRepair(RepairJobId)
                    .RemainingWork, Is.EqualTo(91),
                    "Only verified physical attendance permits canonical progress.");

                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.CrewDuty.GetRepair(RepairJobId)
                    .RemainingWork, Is.EqualTo(91),
                    "Selected actor's interrupted Work produces nothing.");

                f.Commander.ClearSelection();
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                f.Session.Advance(1);
                Assert.That(f.Session.Capture().State.CrewDuty.GetRepair(RepairJobId)
                    .RemainingWork, Is.EqualTo(82),
                    "Reaching the real station again resumes the canonical job.");
                Assert.That(f.Session.Minute.Value, Is.EqualTo(4));
            }
        }

        [Test]
        public void SavedPlayerMoveRestoresBeforeAutonomousRepairResumesOnActualArrival()
        {
            using (var f = new Setup(withRepairJob: true))
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);

                f.Commander.SelectActor(Engineer);
                Assert.That(f.Commander.IssueShipMoveOrder(C(2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True);
                f.Commander.ClearSelection();
                f.Walker.Tick(0.08f);
                Assert.That(f.Walker.HasIncomingReservation, Is.True);
                Assert.That(f.Session.GetPendingCrewMove(Engineer), Is.Not.Null);
                var decoded = SaveCodec.TryDecode(SaveCodec.Encode(f.Session.Capture()));
                Assert.That(decoded.Success, Is.True);
                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    var map = ShipSpatialMap.Derive(restored.Capture().State.Layout,
                        Array.Empty<ShipModuleSpatialProfile>(),
                        Array.Empty<ShipRoomPortal>());
                    // A fresh loaded scene starts at its authored safe cell,
                    // never at the previous interpolation midpoint.
                    f.Walker.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                    f.Walker.Configure(f.Root.transform, map,
                        new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                        C(1, 1), 4f);
                    f.Commander.SetSession(restored);
                    f.Agency.Configure(restored, f.Commander, map);
                    f.Duties.Configure(restored, f.Commander, f.Agency, map);
                    Assert.That(f.Agency.HasActivePlayerMove(Engineer), Is.True);
                    Assert.That(f.Duties.HasActivity(Engineer), Is.False);

                    restored.Advance(1);
                    Assert.That(restored.Capture().State.CrewDuty.GetRepair(RepairJobId)
                        .RemainingWork, Is.EqualTo(100),
                        "A saved player route cannot restore phantom Work progress.");
                    f.WalkToCompletion();
                    f.Agency.Tick();
                    Assert.That(restored.PendingCrewMoves, Is.Empty);
                    f.Duties.Tick();
                    Assert.That(f.Duties.ActiveVerb(Engineer),
                        Is.EqualTo(CrewInteractionVerb.Work));
                    f.WalkToCompletion();
                    f.Duties.Tick();
                    Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                    restored.Advance(1);
                    Assert.That(restored.Capture().State.CrewDuty.GetRepair(RepairJobId)
                        .RemainingWork, Is.LessThan(100),
                        "The canonical repairer resumes only at an authored Work slot.");
                }
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
        public void SceneReentryResumesSameValidWorkClaimWithoutSecondWorldCommand()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.True);
                Assert.That(f.Session.Revision, Is.EqualTo(2));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));

                // Simulate a new physical scene binding: current interpolation
                // is NOT a save checkpoint. Reconstruct at the known safe spawn.
                f.Walker.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                var map = ShipSpatialMap.Derive(f.Session.Capture().State.Layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                f.Walker.Configure(f.Root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    C(1, 1), 4f);
                f.Duties.Configure(f.Session, f.Commander, f.Agency, map);
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);

                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.True);
                Assert.That(f.Session.Revision, Is.EqualTo(2),
                    "A persisted valid claim must be adopted, not re-issued.");
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1), "Exactly one canonical work slot remains claimed.");
            }
        }

        [Test]
        public void SelectedActorOnSceneReentryReleasesStaleUntrackedWorkClaim()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                Assert.That(f.Session.Revision, Is.EqualTo(2));

                f.Walker.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                var map = ShipSpatialMap.Derive(f.Session.Capture().State.Layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                f.Walker.Configure(f.Root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    C(1, 1), 4f);
                f.Duties.Configure(f.Session, f.Commander, f.Agency, map);
                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                Assert.That(f.Duties.HasActivity(Engineer), Is.False);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty, "Orphaned physical Work claim must not block the next task.");
                Assert.That(f.Session.Revision, Is.EqualTo(3));
            }
        }

        [Test]
        public void MovingWorkerCannotRenewLeaseWithoutActualStationArrival()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                Assert.That(f.Walker.IsMoving, Is.True);
                Assert.That(f.Session.Revision, Is.EqualTo(2));
                f.Session.Advance(11);
                f.Duties.Tick(); // The actor is still genuinely travelling.
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(15),
                    "No Work renewal may be earned from a route without arrival.");
                Assert.That(f.Session.Revision, Is.EqualTo(2));
                f.Session.Advance(4);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Null, "The unobserved slot reservation must expire.");
            }
        }

        [Test]
        public void StationedWorkerSingleMinuteClockPreservesLeaseWithoutFrameChurn()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                for (int minute = 1; minute <= 22; minute++)
                {
                    f.Duties.Tick(); // Fresh real walker-at-slot presence.
                    f.Session.Advance(1);
                }
                var lease = f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work);
                Assert.That(lease, Is.Not.Null);
                Assert.That(lease.ExpiresAt.Value, Is.EqualTo(35));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                Assert.That(f.Session.Revision, Is.EqualTo(4),
                    "Single-minute boundaries renew only twice, without frame churn.");
                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
            }
        }

        [Test]
        public void StationedRestingCrewUsesRealBedAttestationWithoutWorkProduction()
        {
            using (var f = new Setup(offShift: true, fatigue: 700))
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.ActiveVerb(Engineer),
                    Is.EqualTo(CrewInteractionVerb.Rest));
                Assert.That(f.Session.ReportShipWorkPresence(Engineer, Rest), Is.False);
                for (int minute = 1; minute <= 22; minute++)
                {
                    f.Duties.Tick();
                    f.Session.Advance(1);
                }
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Rest)
                    .ExpiresAt.Value, Is.EqualTo(35),
                    "The actual bed lease survives a long observed rest.");
                Assert.That(f.Session.Revision, Is.EqualTo(4));
                Assert.That(f.Session.Capture().State.CrewDuty.Get(Engineer).Fatigue,
                    Is.LessThan(700), "Canonical WorldClock alone owns fatigue recovery.");
            }
        }

        [Test]
        public void StationedWorkerRenewsSameLeaseAcrossTwentyTwoWorldMinutes()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                var first = f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work);
                Assert.That(first.ExpiresAt.Value, Is.EqualTo(15));
                Assert.That(f.Session.Revision, Is.EqualTo(2));

                f.Session.Advance(11);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Not.Null, "The canonical minute clock has not expired this claim.");
                f.Duties.Tick();
                var renewed = f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work);
                Assert.That(renewed.ExpiresAt.Value, Is.EqualTo(26));
                Assert.That(f.Session.Revision, Is.EqualTo(3));
                for (int i = 0; i < 15; i++) f.Duties.Tick();
                Assert.That(f.Session.Revision, Is.EqualTo(3),
                    "No per-frame renewal or command-revision churn.");

                f.Session.Advance(11);
                f.Duties.Tick();
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(37));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                Assert.That(f.Session.Revision, Is.EqualTo(4));
                Assert.That(f.Duties.IsStationed(Engineer), Is.True);
                Assert.That(f.Session.Minute.Value, Is.EqualTo(22));
            }
        }

        [Test]
        public void SelectingStationedWorkerReleasesLeaseAndNeverRenewsIt()
        {
            using (var f = new Setup())
            {
                f.Duties.Tick();
                f.WalkToCompletion();
                f.Duties.Tick();
                f.Session.Advance(11);
                f.Commander.SelectActor(Engineer);
                f.Duties.Tick();
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                Assert.That(f.Session.Revision, Is.EqualTo(3),
                    "Selected player interruption releases instead of renewing.");
                f.Session.Advance(20);
                f.Duties.Tick();
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                Assert.That(f.Session.Revision, Is.EqualTo(3));
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

        [Test]
        public void PersistentShipAndOnboardCrewRemainAliveAndWorkingAcrossShoreRoundTrip()
        {
            var scout = EntityId.Parse("crew:duty:g2:scout");
            var shoreScene = EntityId.Parse("scene:duty:g2:shore");
            var shoreSite = EntityId.Parse("island:duty:g2:coast");
            var shoreAnchor = EntityId.Parse("anchor:duty:g2:beach");
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Room, 7, 7)
            }, new[]
            {
                new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                    1, 1, EntityId.Parse("item:duty:g2:part"), 1, 1)
            }, new[]
            {
                new ShipModule(Station, Blueprint, Room, 4, 4)
            });
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Repair, 950, 0, 0, 1440),
                new CrewMember(scout, DutyRole.Watch, 800, 100, 0, 1440)
            }, new[] { new RepairJob(RepairJobId, 100) },
            interactions: CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(Work, Station, CrewInteractionVerb.Work, -1, 0, 0)
            }));

            var root = new GameObject("G2-PersistentShipRoot");
            var controllerGo = new GameObject("G2-PersistentController");
            using (var session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer, scout },
                crewDuty: crew, layout: layout)))
            {
                try
                {
                    session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                        Scene, EntityId.Parse("anchor:duty:g2:start")));
                    Assert.That(session.Submit(new AssignCrewCommand(
                        EntityId.Parse("cmd:duty:g2:assign-concurrent"), Engineer, Station,
                        session.Revision)).Success, Is.True);

                    var originGo = new GameObject("RoomOrigin");
                    originGo.transform.SetParent(root.transform, false);
                    var floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    floorGo.transform.SetParent(root.transform, false);
                    var surface = floorGo.AddComponent<ShipRoomClickSurface>();

                    var engGo = new GameObject("ShipEngineer");
                    engGo.transform.SetParent(root.transform, false);
                    engGo.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                    var engWalker = engGo.AddComponent<ShipGridWalker>();
                    var engProxy = engGo.AddComponent<ShipCrewSelectionProxy>();
                    engProxy.SetActorId(Engineer);

                    var scoutShipGo = new GameObject("ShipScout");
                    scoutShipGo.transform.SetParent(root.transform, false);
                    scoutShipGo.transform.localPosition = new Vector3(1.5f, 0f, 2.5f);
                    var scoutWalker = scoutShipGo.AddComponent<ShipGridWalker>();
                    var scoutShipProxy = scoutShipGo.AddComponent<ShipCrewSelectionProxy>();
                    scoutShipProxy.SetActorId(scout);

                    var commander = controllerGo.AddComponent<TacticalCommandController>();
                    var bootstrap = root.AddComponent<ShipTacticalSceneBootstrap>();
                    bootstrap.Configure(root.transform, commander,
                        new[]
                        {
                            new ShipTacticalSceneBootstrap.RoomBinding
                            {
                                RoomId = Room.Value, Origin = originGo.transform, Floor = surface
                            }
                        },
                        Array.Empty<ShipTacticalSceneBootstrap.DoorBinding>(),
                        new[]
                        {
                            new ShipTacticalSceneBootstrap.CrewBinding
                            {
                                Actor = engProxy, StartingRoomId = Room.Value,
                                StartingCell = new Vector2Int(1, 1)
                            },
                            new ShipTacticalSceneBootstrap.CrewBinding
                            {
                                Actor = scoutShipProxy, StartingRoomId = Room.Value,
                                StartingCell = new Vector2Int(1, 2)
                            }
                        });
                    bootstrap.Initialize(session);

                    int shipRootInstanceId = root.GetInstanceID();
                    int engInstanceId = engGo.GetInstanceID();
                    int scoutShipInstanceId = scoutShipGo.GetInstanceID();

                    var duties = commander.GetComponent<ShipCrewDutyAutonomy>();
                    var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
                    duties.Tick();
                    for (int i = 0; i < 150 && engWalker.IsMoving; i++)
                        engWalker.Tick(0.1f);
                    duties.Tick();
                    Assert.That(duties.IsStationed(Engineer), Is.True);

                    // Scout departs to Shore while Engineer stays aboard the persistent ship.
                    Assert.That(ShoreCrewTransfer.Transfer(session, new[] { scout }, true,
                        EntityId.Parse("transition:duty:g2:depart")).Success, Is.True);
                    session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                        shoreScene, shoreSite, shoreAnchor, new[] { scout }));

                    Assert.That(root.GetInstanceID(), Is.EqualTo(shipRootInstanceId));
                    Assert.That(engGo.GetInstanceID(), Is.EqualTo(engInstanceId));
                    Assert.That(engGo.activeSelf, Is.True,
                        "Onboard crew proxy stays active on the persistent ship.");
                    Assert.That(scoutShipGo.activeSelf, Is.False,
                        "Departing shore crew proxy is suspended on the ship while away.");

                    // Onboard Engineer continues physical work while party is ashore.
                    agency.Tick();
                    duties.Tick();
                    Assert.That(duties.IsStationed(Engineer), Is.True);
                    session.Advance(1);
                    Assert.That(session.Capture().State.CrewDuty.GetRepair(RepairJobId).RemainingWork,
                        Is.EqualTo(91),
                        "Onboard Engineer continues repairing while expedition party is ashore.");

                    // Onboard Engineer can also accept a ship move order while party is ashore.
                    commander.SelectActor(Engineer);
                    Assert.That(commander.IssueShipMoveOrder(C(2, 1), new Vector3(2.5f, 0f, 1.5f)),
                        Is.True);
                    Assert.That(engWalker.IsMoving, Is.True);

                    // Scout returns from Shore to the SAME persistent ship without recreating hierarchy.
                    Assert.That(ShoreCrewTransfer.Transfer(session, new[] { scout }, false,
                        EntityId.Parse("transition:duty:g2:return")).Success, Is.True);
                    session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                        Scene, EntityId.Parse("anchor:duty:g2:start")));

                    Assert.That(root.GetInstanceID(), Is.EqualTo(shipRootInstanceId));
                    Assert.That(engGo.GetInstanceID(), Is.EqualTo(engInstanceId));
                    Assert.That(scoutShipGo.GetInstanceID(), Is.EqualTo(scoutShipInstanceId));
                    Assert.That(scoutShipGo.activeSelf, Is.True,
                        "Returning crew proxy is reactivated on the same persistent ship root.");
                    Assert.That(scoutWalker.IsConfigured, Is.True);
                    Assert.That(engWalker.IsMoving, Is.True,
                        "Returning shore party does not reset or interrupt onboard crew movement.");
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(controllerGo);
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }
    }
}
