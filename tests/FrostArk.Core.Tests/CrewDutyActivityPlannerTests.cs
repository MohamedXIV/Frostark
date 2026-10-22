using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewDutyActivityPlannerTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:duty:engineer");
        private static readonly EntityId Watch = EntityId.Parse("crew:duty:watch");
        private static readonly EntityId Resting = EntityId.Parse("crew:duty:resting");
        private static readonly EntityId Backup = EntityId.Parse("crew:duty:backup");
        private static readonly EntityId Room = EntityId.Parse("room:duty");
        private static readonly EntityId Station = EntityId.Parse("module:duty:engine");
        private static readonly EntityId Bed = EntityId.Parse("module:duty:bed");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:duty");
        private static readonly EntityId WorkSlot = EntityId.Parse("slot:duty:work");
        private static readonly EntityId RestSlot = EntityId.Parse("slot:duty:rest");

        private static WorldSession Fixture(bool engineerAway = false,
            int restingFatigue = 700)
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 950, 100, 0, 1440,
                    isAway: engineerAway),
                new CrewMember(Watch, DutyRole.Watch, 850, 350, 0, 1440),
                new CrewMember(Resting, DutyRole.Cooking, 850, 100, 720, 480,
                    fatigue: restingFatigue),
                new CrewMember(Backup, DutyRole.Repair, 850, 100, 0, 1440)
            }, interactions: CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(WorkSlot, Station, CrewInteractionVerb.Work,
                    -1, 0, 0),
                new CrewInteractionSlot(RestSlot, Bed, CrewInteractionVerb.Rest,
                    1, 0, 0)
            }));
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Room, 7, 7)
            }, new[]
            {
                new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                    1, 1, EntityId.Parse("item:duty:part"), 1, 1)
            }, new[]
            {
                new ShipModule(Station, Blueprint, Room, 3, 3),
                new ShipModule(Bed, Blueprint, Room, 1, 1)
            });
            return new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer, Watch, Resting, Backup },
                crewDuty: crew, layout: layout));
        }

        private static void Assign(WorldSession session, EntityId crew, EntityId station)
        {
            var cmdId = EntityId.Parse("cmd:duty:assign:" + crew.Value);
            var result = session.Submit(new AssignCrewCommand(cmdId, crew,
                station, session.Revision));
            Assert.That(result.Success, Is.True);
        }

        [Test]
        public void OnlyActuallyStaffedAssignedCrewGetsAuthoredWorkSlot()
        {
            using (var session = Fixture())
            {
                var state = session.Capture().State;
                Assert.That(state.CrewDuty.StaffedRoleFor(Engineer, state.Minute),
                    Is.EqualTo(DutyRole.Engineering));
                Assert.That(state.CrewDuty.StaffedRoleFor(Watch, state.Minute),
                    Is.EqualTo(DutyRole.Watch));
                Assert.That(CrewDutyActivityPlanner.Find(state, Engineer), Is.Null,
                    "Nominally trained crew cannot invent a physical station.");

                Assign(session, Engineer, Station);
                var now = session.Capture().State;
                var offer = CrewDutyActivityPlanner.Find(now, Engineer);
                Assert.That(offer.HasValue, Is.True);
                Assert.That(offer.Value.Verb, Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(offer.Value.SlotId, Is.EqualTo(WorkSlot));
                Assert.That(offer.Value.SourceModuleId, Is.EqualTo(Station));
                Assert.That(offer.Value.StaffedRole, Is.EqualTo(DutyRole.Engineering));
                Assert.That(CrewDutyActivityPlanner.Find(now, Watch), Is.Null);
            }
        }

        [Test]
        public void ShiftRecoveryAndUnstaffedWorkUseRealRestSlotWithoutFakeProgress()
        {
            using (var session = Fixture())
            {
                var before = session.Capture().State;
                var offer = CrewDutyActivityPlanner.Find(before, Resting);
                Assert.That(offer.HasValue, Is.True);
                Assert.That(offer.Value.Verb, Is.EqualTo(CrewInteractionVerb.Rest));
                Assert.That(offer.Value.SlotId, Is.EqualTo(RestSlot));
                Assert.That(before.CrewDuty.StaffedRoleFor(Resting, before.Minute), Is.Null);
                Assert.That(session.Revision, Is.Zero,
                    "Read-only physical planner never advances canonical fatigue.");

                session.Advance(1);
                var next = session.Capture().State;
                Assert.That(next.CrewDuty.Get(Resting).Fatigue,
                    Is.LessThan(before.CrewDuty.Get(Resting).Fatigue),
                    "Existing WorldClock, not Unity, advances actual rest.");
            }

            using (var session = Fixture(restingFatigue: 0))
                Assert.That(CrewDutyActivityPlanner.Find(
                    session.Capture().State, Resting), Is.Null,
                    "Do not reserve beds for fully rested offshift crew.");
        }

        [Test]
        public void CanonicalBackupAndUnplacedStationRulesPreventPhantomWork()
        {
            using (var session = Fixture(engineerAway: true))
            {
                Assign(session, Watch, Station);
                var world = session.Capture().State;
                Assert.That(world.CrewDuty.StaffedRoleFor(Watch, world.Minute),
                    Is.EqualTo(DutyRole.Engineering),
                    "Staffed role must match existing canonical fallback priority.");
                Assert.That(CrewDutyActivityPlanner.Find(world, Engineer), Is.Null);
                Assert.That(CrewDutyActivityPlanner.Find(world, Watch)?.StaffedRole,
                    Is.EqualTo(DutyRole.Engineering));
            }

            using (var session = Fixture())
            {
                Assign(session, Engineer, EntityId.Parse("station:unplaced"));
                Assert.That(CrewDutyActivityPlanner.Find(
                    session.Capture().State, Engineer), Is.Null,
                    "An arbitrary stored assignment is not a physical GameObject.");
            }
        }

        [Test]
        public void BusySlotAndAlreadyClaimedCrewCannotCreateAnotherWork()
        {
            using (var session = Fixture())
            {
                Assign(session, Engineer, Station);
                Assign(session, Watch, Station);
                var state = session.Capture().State;
                var claim = session.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:duty:claim:watch"), Watch, WorkSlot,
                    15, state.Revision));
                Assert.That(claim.Success, Is.True);
                Assert.That(CrewDutyActivityPlanner.Find(
                    session.Capture().State, Engineer), Is.Null);
                Assert.That(CrewDutyActivityPlanner.Find(
                    session.Capture().State, Watch)?.SlotId, Is.EqualTo(WorkSlot));
            }
        }
    }
}
