using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewEngineeringAttendanceTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:thermal:engineer");
        private static readonly EntityId Watch = EntityId.Parse("crew:thermal:watch");
        private static readonly EntityId Room = EntityId.Parse("room:thermal:boiler");
        private static readonly EntityId Station = EntityId.Parse("module:thermal:work");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:thermal:work");
        private static readonly EntityId Work = EntityId.Parse("slot:thermal:work");
        private static readonly EntityId Scene = EntityId.Parse("scene:thermal:ship");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");

        private static WorldSession Session(bool assignStation = true,
            bool staffedBackup = false)
        {
            var crew = staffedBackup ? new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0, 0, 1440,
                    isAway: true),
                new CrewMember(Watch, DutyRole.Watch, 850, 600, 0, 1440)
            } : new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0, 0, 1440)
            };
            var duty = CrewState.Create(crew,
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Work, Station, CrewInteractionVerb.Work,
                        -1, 0, 0)
                }));
            var layout = ShipLayoutState.Create(
                new[] { new ShipCompartmentSlot(Room, 7, 7) },
                new[] { new ShipModuleBlueprint(Blueprint,
                    ShipModuleKind.Auxiliary, 1, 1,
                    EntityId.Parse("item:thermal:part"), 1, 1) },
                new[] { new ShipModule(Station, Blueprint, Room, 4, 4) });
            var thermal = ShipThermalState.Create(new[]
            {
                new ThermalCompartment(Room, 1000, 20_000_000,
                    0, 1000, 1000)
            }, Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(Room, 1, 10_000));
            var session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                new[] { new SupplyStock(Fuel, 20) },
                crew.Select(c => c.Id), thermal: thermal,
                crewDuty: duty, layout: layout),
                ambientMilliC: _ => 20_000);
            session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(Scene,
                EntityId.Parse("anchor:thermal:spawn")));
            if (assignStation)
            {
                var staffed = staffedBackup ? Watch : Engineer;
                Assert.That(session.Submit(new AssignCrewCommand(
                    EntityId.Parse("cmd:thermal:assign"), staffed, Station,
                    session.Revision)).Success, Is.True);
                Assert.That(session.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:thermal:claim"), staffed, Work,
                    15, session.Revision)).Success, Is.True);
            }
            return session;
        }

        private static long Energy(WorldSession session) =>
            session.Capture().State.Thermal.TotalEnergy;

        [Test]
        public void StaffedEngineerMustArriveBeforeCanonicalBoilerFuelAndHeat()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                var start = Energy(s);
                s.Advance(1);
                Assert.That(Energy(s), Is.EqualTo(start),
                    "A travelling assigned engineer must not fire a loaded-scene boiler.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(20));
                Assert.That(s.ReportShipWorkPresence(Engineer, Work), Is.True);
                s.Advance(1);
                Assert.That(Energy(s), Is.GreaterThan(start));
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19));
                var worked = Energy(s);
                s.ClearShipWorkPresence(Engineer);
                s.Advance(1);
                Assert.That(Energy(s), Is.EqualTo(worked),
                    "No new attestation means no new on-stage boiler heat.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19));
                s.EndShipWorkObservation(Scene);
                s.Advance(1);
                Assert.That(Energy(s), Is.GreaterThan(worked),
                    "Offscreen boiler restores legacy staffing semantics.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(18));
            }
        }

        [Test]
        public void FastForwardCanApplyOnlyOneAttestedEngineeringMinute()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                Assert.That(s.ReportShipWorkPresence(Engineer, Work), Is.True);
                s.Advance(10);
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19),
                    "A single observed frame never runs ten unattended boiler minutes.");
                var before = Energy(s);
                Assert.That(s.ReportShipWorkPresence(Engineer, Work), Is.True);
                s.Advance(3);
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(18));
                Assert.That(Energy(s), Is.GreaterThan(before));
                var resume = Energy(s);
                s.EndShipWorkObservation(Scene);
                s.Advance(2);
                Assert.That(Energy(s), Is.GreaterThan(resume),
                    "After unloading, thermal still advances without Unity frames.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(16));
            }
        }

        [Test]
        public void NoAuthoredPhysicalEngineeringStationKeepsPriorThermalSemantics()
        {
            using (var s = Session(assignStation: false))
            {
                s.BeginShipWorkObservation(Scene);
                var start = Energy(s);
                s.Advance(1);
                Assert.That(Energy(s), Is.GreaterThan(start),
                    "Legacy ship without an assigned physical Work station must not lose heating.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19));
            }
        }

        [Test]
        public void StaffedBackupRatherThanAwayPrimaryOwnsObservedBoiler()
        {
            using (var s = Session(staffedBackup: true))
            {
                Assert.That(s.Capture().State.CrewDuty.StaffedCrewFor(
                    DutyRole.Engineering, WorldMinute.Zero), Is.EqualTo(Watch));
                s.BeginShipWorkObservation(Scene);
                Assert.That(s.ReportShipWorkPresence(Engineer, Work), Is.False);
                s.Advance(1);
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(20));
                Assert.That(s.ReportShipWorkPresence(Watch, Work), Is.True);
                s.Advance(1);
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19));
            }
        }
    }
}
