using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using System.Text;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewMedicalTreatmentTests
    {
        private static readonly EntityId Medic = EntityId.Parse("crew:medic");
        private static readonly EntityId Patient = EntityId.Parse("crew:scout");
        private static readonly EntityId Kit = EntityId.Parse("item:medkit");

        private static WorldState Create(int wound = 600, long kits = 2,
            bool doctorAway = false, bool patientAway = false, bool hasDoctor = true)
        {
            var medicine = new MedicalTreatmentProtocol(Kit, healPermille: 250,
                minimumMedicalSkill: 400);
            var members = hasDoctor
                ? new[] { new CrewMember(Medic, DutyRole.Medical, 950, 0, 0, 1440,
                        isAway: doctorAway),
                    new CrewMember(Patient, DutyRole.Watch, 800, 0, 0, 1440,
                        wounds: wound, isAway: patientAway) }
                : new[] { new CrewMember(Patient, DutyRole.Watch, 800, 0, 0, 1440,
                        wounds: wound, isAway: patientAway) };
            var crew = CrewState.Create(members, treatments: new[] { medicine });
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Kit, 1, 100, 10)
            }), new[] { new SalvagePickup(EntityId.Parse("pickup:meds"), Kit, Math.Max(kits, 1)) },
                2000, 5, 2000, 5);
            if (kits > 0)
                inventory = inventory.TryCollect(EntityId.Parse("pickup:meds")).State
                    .TryUnload(Kit, kits).State;
            return WorldState.Create(new WorldMinute(70),
                Array.Empty<SupplyStock>(), hasDoctor ? new[] { Medic, Patient } : new[] { Patient },
                crewDuty: crew, inventory: inventory);
        }

        [Test]
        public void MedicalTreatmentConsumesRealShipKitAndReducesExistingWoundsOnce()
        {
            var before = Create();
            var patientSkill = before.CrewDuty.Get(Patient).SkillFor(DutyRole.Watch);
            var command = new TreatCrewWoundCommand(EntityId.Parse("command:bandage"), Patient, Kit, 0);
            var result = new WorldCommandBus().Execute(before, command);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.CrewDuty.Get(Patient).Wounds, Is.EqualTo(350));
            Assert.That(result.State.CrewDuty.Get(Patient).SkillFor(DutyRole.Watch),
                Is.GreaterThan(patientSkill));
            Assert.That(result.State.Inventory.GetAmount(InventoryContainer.ShipHold, Kit),
                Is.EqualTo(1));
            Assert.That(before.Inventory.GetAmount(InventoryContainer.ShipHold, Kit), Is.EqualTo(2));
            Assert.That(before.CrewDuty.Get(Patient).Wounds, Is.EqualTo(600));
            Assert.That(result.State.Revision, Is.EqualTo(1));
            Assert.That(result.Events, Has.Count.EqualTo(1));
            Assert.That(result.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewTreated));
            Assert.That(result.Events[0].Quantity, Is.EqualTo(250));
            var replay = new WorldCommandBus().Execute(result.State, command);
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(replay.State, Is.SameAs(result.State));
        }

        [Test]
        public void InsufficientSuppliesAbsentDoctorAndShoreCrewRejectWithoutSideEffects()
        {
            var bus = new WorldCommandBus();
            void Reject(WorldState state, CommandFailure expected)
            {
                var outcome = bus.Execute(state, new TreatCrewWoundCommand(
                    EntityId.Parse("command:try"), Patient, Kit, 0));
                Assert.That(outcome.Failure, Is.EqualTo(expected));
                Assert.That(outcome.State, Is.SameAs(state));
                Assert.That(outcome.Events, Is.Empty);
            }
            Reject(Create(kits: 0), CommandFailure.InsufficientInventory);
            Reject(Create(doctorAway: true), CommandFailure.MedicalStaffUnavailable);
            Reject(Create(hasDoctor: false), CommandFailure.MedicalStaffUnavailable);
            Reject(Create(patientAway: true), CommandFailure.PatientUnavailable);
            Reject(Create(wound: 0), CommandFailure.PatientNotWounded);
        }

        [Test]
        public void WoundsPersistDuringOrdinarySleepAndRepeatedTreatmentClampsToZero()
        {
            var before = Create(wound: 100);
            var slept = before.CrewDuty.Tick(new WorldMinute(71));
            Assert.That(slept.Get(Patient).Wounds, Is.EqualTo(100));

            var treated = new WorldCommandBus().Execute(before,
                new TreatCrewWoundCommand(EntityId.Parse("command:treat"), Patient, Kit, 0));
            Assert.That(treated.Success, Is.True);
            Assert.That(treated.State.CrewDuty.Get(Patient).Wounds, Is.Zero);
            Assert.That(treated.Events[0].Quantity, Is.EqualTo(100));
            var repeat = new WorldCommandBus().Execute(treated.State,
                new TreatCrewWoundCommand(EntityId.Parse("command:repeat"), Patient, Kit, 1));
            Assert.That(repeat.Failure, Is.EqualTo(CommandFailure.PatientNotWounded));
            Assert.That(repeat.State.Inventory.GetAmount(InventoryContainer.ShipHold, Kit),
                Is.EqualTo(1));
        }

        [Test]
        public void TreatmentPolicyAndRecoveredPatientRoundTripSaveWithoutMinter()
        {
            var accepted = new WorldCommandBus().Execute(Create(),
                new TreatCrewWoundCommand(EntityId.Parse("command:heal"), Patient, Kit, 0));
            Assert.That(accepted.Success, Is.True);
            var timeline = new SimulationScheduler(new WorldClock(accepted.State.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 17);
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(accepted.State, timeline));
            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.CrewDuty.Get(Patient).Wounds, Is.EqualTo(350));
            Assert.That(decoded.Value.State.CrewDuty.GetTreatment(Kit).HealPermille,
                Is.EqualTo(250));
            Assert.That(decoded.Value.State.Fingerprint(), Is.EqualTo(accepted.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void UnknownMedicineCannotBeSubstitutedForAnApprovedTreatment()
        {
            var start = Create();
            var outcome = new WorldCommandBus().Execute(start,
                new TreatCrewWoundCommand(EntityId.Parse("command:unknown-kit"),
                    Patient, EntityId.Parse("item:experimental-drug"), 0));
            Assert.That(outcome.Failure, Is.EqualTo(CommandFailure.UnknownTreatment));
            Assert.That(outcome.State, Is.SameAs(start));
            Assert.That(outcome.Events, Is.Empty);
        }

        [Test]
        public void InvalidChecksummedPolicyCannotCreateOverpoweredHealing()
        {
            var state = Create();
            var timeline = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 13);
            var encoded = SaveCodec.Encode(SaveGameDto.Capture(state, timeline));
            // Medical's min-skill is followed by v12 site count (4),
            // v13 checkpoint flag (1), v14 magazine count (4), and the
            // variable-length v15 recovery flags. Mutate the REAL policy
            // rather than accidentally corrupting the newest extension.
            int recoveryBytes = 4;
            foreach (var member in state.CrewDuty.Members)
                recoveryBytes += 4 + Encoding.UTF8.GetByteCount(member.Id.Value) + 1;
            int v16Needs = 8;
            foreach (var member in state.CrewDuty.Members)
                v16Needs += 12 + Encoding.UTF8.GetByteCount(member.Id.Value);
            int minimumSkillOffset = encoded.Length - 32 - 8 - v16Needs
                - recoveryBytes - 4 - 1 - 4 - 4;
            Array.Copy(BitConverter.GetBytes(1001), 0,
                encoded, minimumSkillOffset, sizeof(int));
            using (var hash = System.Security.Cryptography.SHA256.Create())
                Array.Copy(hash.ComputeHash(encoded, 0, encoded.Length - 32),
                    0, encoded, encoded.Length - 32, 32);
            var invalid = SaveCodec.TryDecode(encoded);
            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void RejectDuplicateInvalidAndUnrealisticMedicalPolicyDefinitions()
        {
            var valid = new MedicalTreatmentProtocol(Kit, 200, 400);
            Assert.Throws<ArgumentException>(() => CrewState.Create(
                Array.Empty<CrewMember>(), treatments: new[] { valid, valid }));
            Assert.Throws<ArgumentException>(() => new MedicalTreatmentProtocol(default, 100, 400));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MedicalTreatmentProtocol(Kit, 0, 400));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MedicalTreatmentProtocol(Kit, 1001, 400));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MedicalTreatmentProtocol(Kit, 200, 1001));
        }
    }
}
