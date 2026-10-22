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
            bool staffedBackup = false, bool restSlot = false)
        {
            var crew = staffedBackup ? new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0, 0, 1440,
                    isAway: true),
                new CrewMember(Watch, DutyRole.Watch, 850, 600, 0, 1440)
            } : new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0,
                    restSlot ? 720 : 0, restSlot ? 400 : 1440,
                    fatigue: restSlot ? 700 : 0)
            };
            var duty = CrewState.Create(crew,
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Work, Station,
                        restSlot ? CrewInteractionVerb.Rest : CrewInteractionVerb.Work,
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
        public void FreshObservedSingleMinutesRenewSameWorkLeaseWithoutRepeatedCommands()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                for (int minute = 1; minute <= 22; minute++)
                {
                    Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.True);
                    s.Advance(1);
                }
                Assert.That(s.Minute.Value, Is.EqualTo(22));
                var held = s.Capture().State.CrewDuty.Interactions.GetClaim(Work);
                Assert.That(held, Is.Not.Null);
                Assert.That(held.ExpiresAt.Value, Is.EqualTo(35),
                    "Observed renewals occur at t10 and t20 only.");
                Assert.That(s.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                Assert.That(s.Revision, Is.EqualTo(4),
                    "One station assignment, one claim, two due renewals; " +
                    "no per-minute revision churn.");
                var snapshot = SaveCodec.TryDecode(SaveCodec.Encode(s.Capture()));
                Assert.That(snapshot.Success, Is.True);
                Assert.That(snapshot.Value.State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(35),
                    "Renewed canonical lease survives SaveCodec without a new field.");
            }
        }

        [Test]
        public void OneRenderedFrameCannotRenewThroughTwentyMinuteTimeSkip()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                s.Advance(11);
                Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.True);
                var previousRevision = s.Revision;
                s.Advance(20);
                Assert.That(s.Revision, Is.EqualTo(previousRevision),
                    "No implicit lease commands inside batched fast-forward.");
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Null, "A stale attended claim must expire normally.");
                Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.False,
                    "An expired claim cannot be revived with a stale report.");
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(19),
                    "Only the first attested minute may produce on-stage heat.");
                s.EndShipWorkObservation(Scene);
                s.Advance(1);
                Assert.That(s.Capture().State.GetSupply(Fuel), Is.EqualTo(18),
                    "Offscreen heating resumes without an on-stage claim.");
            }
        }

        [Test]
        public void RestSlotCanRenewWhileWorkOnlyProofRemainsRejected()
        {
            using (var s = Session(restSlot: true))
            {
                s.BeginShipWorkObservation(Scene);
                s.Advance(11);
                Assert.That(s.ReportShipWorkPresence(Engineer, Work), Is.False,
                    "Physical Rest is never productive Work attestation.");
                Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.True);
                s.Advance(1);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(26),
                    "An occupied authored Rest claim may also renew.");
                Assert.That(s.Revision, Is.EqualTo(3));
                s.ClearShipWorkPresence(Engineer);
                s.Advance(14);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Null, "Unattended Rest must not become a permanent bed lock.");
            }
        }

        [Test]
        public void UnassignedWorkerCannotExtendPreviouslyHeldWorkLease()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                s.Advance(11);
                // The physical slot can remain owned when a standing
                // assignment is cancelled. That stale claim is not authority
                // to extend an obsolete Work duty.
                Assert.That(s.Submit(new UnassignCrewCommand(
                    EntityId.Parse("cmd:thermal:unassign"), Engineer,
                    s.Revision)).Success, Is.True);
                Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.False,
                    "The unassigned slot is no longer a genuine Work presence.");
                var rev = s.Revision;
                s.Advance(1);
                Assert.That(s.Revision, Is.EqualTo(rev),
                    "Unassigned Work is never renewed by old attendance.");
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(15));
            }
        }

        [Test]
        public void AdvanceUntilOneWorldMinuteUsesSameRealLeaseBoundary()
        {
            using (var s = Session())
            {
                s.BeginShipWorkObservation(Scene);
                s.Advance(11);
                Assert.That(s.ReportShipDutyPresence(Engineer, Work), Is.True);
                s.AdvanceUntil(s.Minute.Add(1));
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(26));
                Assert.That(s.Revision, Is.EqualTo(3));
                s.AdvanceUntil(s.Minute.Add(2));
                Assert.That(s.Revision, Is.EqualTo(3),
                    "A bulk AdvanceUntil cannot reuse last frame for renewal.");
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
