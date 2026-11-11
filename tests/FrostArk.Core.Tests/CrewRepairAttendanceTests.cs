using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewRepairAttendanceTests
    {
        private static readonly EntityId Worker = EntityId.Parse("crew:attend:repair");
        private static readonly EntityId Other = EntityId.Parse("crew:attend:other");
        private static readonly EntityId Job = EntityId.Parse("repair:attend:hull");
        private static readonly EntityId Room = EntityId.Parse("room:attend:ship");
        private static readonly EntityId Module = EntityId.Parse("module:attend:station");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:attend:station");
        private static readonly EntityId Work = EntityId.Parse("slot:attend:work");
        private static readonly EntityId Drink = EntityId.Parse("slot:attend:drink");
        private static readonly EntityId Scene = EntityId.Parse("scene:attend:ship");

        private static WorldSession Session()
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Worker, DutyRole.Repair, 800, 0, 0, 1440),
                new CrewMember(Other, DutyRole.Watch, 900, 0, 0, 1440)
            }, new[] { new RepairJob(Job, 500) },
            interactions: CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(Work, Module, CrewInteractionVerb.Work,
                    -1, 0, 0),
                new CrewInteractionSlot(Drink, Module, CrewInteractionVerb.Drink,
                    0, -1, 2)
            }));
            var layout = ShipLayoutState.Create(new[]
            {
                new ShipCompartmentSlot(Room, 7, 7)
            }, new[]
            {
                new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary, 1, 1,
                    EntityId.Parse("item:attend:part"), 1, 1)
            }, new[]
            {
                new ShipModule(Module, Blueprint, Room, 4, 4)
            });
            var session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Worker, Other },
                crewDuty: crew, layout: layout));
            session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(Scene,
                EntityId.Parse("anchor:attend:start")));
            Assert.That(session.Submit(new AssignCrewCommand(
                EntityId.Parse("cmd:attend:assign"), Worker, Module, 0)).Success, Is.True);
            Assert.That(session.Submit(new ClaimCrewInteractionCommand(
                EntityId.Parse("cmd:attend:claim"), Worker, Work, 15,
                session.Revision)).Success, Is.True);
            return session;
        }

        private static long Remaining(WorldSession session) =>
            session.Capture().State.CrewDuty.GetRepair(Job).RemainingWork;

        [Test]
        public void SavedPlayerMoveRevokesRepairAttendanceAndRestoresWithoutPhantomWork()
        {
            using (var source = Session())
            {
                source.BeginShipWorkObservation(Scene);
                Assert.That(source.ReportShipWorkPresence(Worker, Work), Is.True);
                var target = new ShipGridCell(Room, 5, 2);
                var beforeRevision = source.Revision;
                source.AcceptShipCrewMove(Worker, target);
                Assert.That(source.Revision, Is.EqualTo(beforeRevision),
                    "Accepting a physical Move must not issue a world command.");
                source.Advance(1);
                Assert.That(Remaining(source), Is.EqualTo(500),
                    "Player movement must revoke previously attested repair work.");

                var saved = SaveCodec.TryDecode(SaveCodec.Encode(source.Capture()));
                Assert.That(saved.Success, Is.True);
                Assert.That(saved.Value.PendingCrewMoves.Count, Is.EqualTo(1));
                using (var restored = WorldSession.Restore(saved.Value))
                {
                    Assert.That(restored.GetPendingCrewMove(Worker).ShipGoal,
                        Is.EqualTo(target));
                    Assert.That(restored.Minute.Value, Is.EqualTo(1));
                    restored.BeginShipWorkObservation(Scene);
                    restored.Advance(1);
                    Assert.That(Remaining(restored), Is.EqualTo(500),
                        "Loaded scene must reacquire real Work attendance after reentry.");
                    Assert.That(restored.GetPendingCrewMove(Worker), Is.Not.Null,
                        "A valid accepted destination survives without selection.");
                    Assert.That(restored.CompleteCrewMove(Worker), Is.True);
                    Assert.That(restored.CompleteCrewMove(Worker), Is.False);
                    Assert.That(restored.Capture().PendingCrewMoves, Is.Empty);
                }
            }
        }

        [Test]
        public void LoadedShipNeedsActualStationedProofBeforeRepairProgress()
        {
            using (var session = Session())
            {
                session.BeginShipWorkObservation(Scene);
                long start = Remaining(session);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(start),
                    "A staffed repairer travelling to the Work slot earns zero repair.");
                Assert.That(session.Revision, Is.EqualTo(2),
                    "Scene presence and world-minute advance issue no fake commands.");
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(start - 8),
                    "A real, owner-attested station permits one canonical repair minute.");
                session.ClearShipWorkPresence(Worker);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(start - 8),
                    "Player move or selecting worker revokes the productive proof.");
                Assert.That(session.Minute.Value, Is.EqualTo(3));
            }
        }

        [Test]
        public void BatchedTwentyMinuteAdvanceCannotMultiplyOnePresenceFrame()
        {
            using (var session = Session())
            {
                session.BeginShipWorkObservation(Scene);
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.Advance(10);
                Assert.That(Remaining(session), Is.EqualTo(492),
                    "One attestation authorizes at most one WorldMinute of repair.");
                Assert.That(session.Minute.Value, Is.EqualTo(10));
                Assert.That(session.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Not.Null);
                long before = Remaining(session);
                int actualSkill = session.Capture().State.CrewDuty.Get(Worker)
                    .SkillFor(DutyRole.Repair);
                long oneMinuteWork = Math.Max(1, actualSkill / 100);
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.Advance(2);
                Assert.That(Remaining(session), Is.EqualTo(before - oneMinuteWork),
                    "Re-attestation allows only the NEXT productive minute; true " +
                    "fatigue-adjusted skill still determines work output.");

                // Compare the same saved canonical world against a separately
                // restored fully offscreen session. Do not hardcode repair
                // rates that properly change with canonical fatigue.
                var saved = SaveCodec.TryDecode(SaveCodec.Encode(session.Capture()));
                Assert.That(saved.Success, Is.True);
                using (var offscreen = WorldSession.Restore(saved.Value))
                {
                    session.EndShipWorkObservation(Scene);
                    session.Advance(4);
                    offscreen.Advance(4);
                    Assert.That(session.Capture().State.Fingerprint(),
                        Is.EqualTo(offscreen.Capture().State.Fingerprint()),
                        "After unload, the canonical world stays byte-deterministic " +
                        "with offscreen Save/Restore behavior.");
                }
            }
        }

        [Test]
        public void OnlyOwnedPlacedAssignedWorkSlotAuthorizesProduction()
        {
            using (var session = Session())
            {
                session.BeginShipWorkObservation(Scene);
                Assert.That(session.ReportShipWorkPresence(Other, Work), Is.False,
                    "The slot is exclusively held by the repair worker.");
                Assert.That(session.ReportShipWorkPresence(Worker, Drink), Is.False,
                    "A food/drink slot is not proof of repair attendance.");
                Assert.That(session.ReportShipWorkPresence(
                    Worker, EntityId.Parse("slot:absent")), Is.False);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(500));
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:attend:replacement"),
                    EntityId.Parse("anchor:attend:replacement")));
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.False);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(492),
                    "A scene replacement discards stale attendance and uses offscreen time.");
            }
        }

        [Test]
        public void SaveRestoresCanonicalProgressButNotAnOldUnityFramePresence()
        {
            using (var session = Session())
            {
                session.BeginShipWorkObservation(Scene);
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.Advance(1);
                var bytes = SaveCodec.Encode(session.Capture());
                var decoded = SaveCodec.TryDecode(bytes);
                Assert.That(decoded.Success, Is.True);
                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    Assert.That(restored.Minute.Value, Is.EqualTo(1));
                    Assert.That(Remaining(restored), Is.EqualTo(492));
                    // The scene has not been bound yet. Canonical offscreen
                    // production remains authoritative, no frame proof saved.
                    restored.Advance(1);
                    Assert.That(Remaining(restored), Is.EqualTo(484));
                    restored.BeginShipWorkObservation(Scene);
                    restored.Advance(1);
                    Assert.That(Remaining(restored), Is.EqualTo(484),
                        "Reentered scene cannot inherit a saved frame's attendance.");
                }
            }
        }

        [Test]
        public void StaleSceneCannotStopReplacementObservationOrForgeAttendance()
        {
            using (var session = Session())
            {
                Assert.Throws<InvalidOperationException>(() =>
                    session.BeginShipWorkObservation(EntityId.Parse("scene:wrong")));
                session.BeginShipWorkObservation(Scene);
                session.EndShipWorkObservation(EntityId.Parse("scene:wrong"));
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.True);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(492));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse("scene:another-ship"),
                    EntityId.Parse("anchor:another")));
                Assert.That(session.ReportShipWorkPresence(Worker, Work), Is.False);
                session.Advance(1);
                Assert.That(Remaining(session), Is.EqualTo(484),
                    "A truly unloaded/changed physical scene reverts to offscreen.");
            }
        }
    }
}
