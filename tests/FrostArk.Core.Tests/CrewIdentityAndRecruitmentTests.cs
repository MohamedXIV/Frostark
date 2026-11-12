using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewIdentityAndRecruitmentTests
    {
        private static readonly EntityId Protagonist = EntityId.Parse("crew:ship:captain");
        private static readonly EntityId Engineer = EntityId.Parse("crew:ship:engineer");
        private static readonly EntityId Scout = EntityId.Parse("crew:ship:scout");
        private static readonly EntityId SurvivorMedic = EntityId.Parse("crew:survivor:ilya");
        private static readonly EntityId SurvivorEngineer = EntityId.Parse("crew:survivor:astrid");
        private static readonly EntityId DistressSite = EntityId.Parse("site:distress-beacon-01");
        private static readonly EntityId WreckSite = EntityId.Parse("site:icebreaker-wreck");
        private static readonly EntityId BoilerRoom = EntityId.Parse("room:boiler");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");

        [Test]
        public void CrewIdentityValidatesDisplayNameAndOptionalLocalizationKey()
        {
            var identity = new CrewIdentity(Protagonist, "Capt. Mara Solberg", "crew.protagonist.mara");
            Assert.That(identity.Id, Is.EqualTo(Protagonist));
            Assert.That(identity.DisplayName, Is.EqualTo("Capt. Mara Solberg"));
            Assert.That(identity.LocalizationKey, Is.EqualTo("crew.protagonist.mara"));
            Assert.That(identity.HasLocalizationKey, Is.True);
            Assert.That(identity.IsDefaultDerived, Is.False);

            var defaultCaptain = CrewIdentity.CreateDefault(Protagonist);
            Assert.That(defaultCaptain.DisplayName, Is.EqualTo("Captain"));
            Assert.That(defaultCaptain.LocalizationKey, Is.Empty);
            Assert.That(defaultCaptain.HasLocalizationKey, Is.False);
            Assert.That(defaultCaptain.IsDefaultDerived, Is.True);
            Assert.That(CrewIdentity.CreateDefault(EntityId.Parse("crew:01")).DisplayName,
                Is.EqualTo("Crew 01"));
            Assert.That(CrewIdentity.CreateDefault(EntityId.Parse("crew:hana-lind")).DisplayName,
                Is.EqualTo("Hana Lind"));

            Assert.Throws<ArgumentException>(() => new CrewIdentity(default, "Mara"));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, ""));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "   "));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, " LeadingSpace"));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "TrailingSpace "));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "Line\nBreak"));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist,
                new string('A', CrewIdentity.MaxDisplayNameLength + 1)));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "Mara", "   "));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "Mara", "bad key"));
            Assert.Throws<ArgumentException>(() => new CrewIdentity(Protagonist, "Mara",
                new string('k', CrewIdentity.MaxLocalizationKeyLength + 1)));
        }

        [Test]
        public void ProtagonistAndCrewRetainIdentitiesAcrossDutiesConditionAndShoreTransfers()
        {
            var captainIdentity = new CrewIdentity(Protagonist, "Capt. Mara Solberg", "crew.captain");
            var engineerIdentity = new CrewIdentity(Engineer, "Soren Lindqvist", "crew.soren");
            var bandages = EntityId.Parse("item:bandages");
            var ration = EntityId.Parse("item:ration");
            var pickup = EntityId.Parse("pickup:supplies");
            var inventory = InventoryState.Create(
                new ItemCatalog(new[]
                {
                    new ItemDefinition(bandages, 1, 50, 5),
                    new ItemDefinition(ration, 1, 200, 10)
                }),
                new[] { new SalvagePickup(pickup, ration, 2) },
                10000, 10, 10000, 10)
                .TryCollect(pickup).State.TryUnload(ration, 2).State;

            var roster = CrewState.Create(
                new[]
                {
                    new CrewMember(captainIdentity, DutyRole.Watch, 900, 300, 0, 1440, hunger: 300),
                    new CrewMember(engineerIdentity, DutyRole.Engineering, 950, 200, 0, 1440)
                },
                consumables: new[] { new CrewConsumable(ration, 250, 0) });

            var world = WorldState.Create(new WorldMinute(60),
                Array.Empty<SupplyStock>(), new[] { Protagonist, Engineer },
                inventory: inventory, crewDuty: roster);

            using (var session = new WorldSession(world))
            {
                Assert.That(session.Read().CrewCount, Is.EqualTo(2));
                Assert.That(session.Read().MaxCrewCapacity, Is.EqualTo(12));
                Assert.That(session.Read().GetCrewIdentity(Protagonist), Is.EqualTo(captainIdentity));
                Assert.That(session.Read().GetCrewIdentity(Engineer), Is.EqualTo(engineerIdentity));

                Assert.That(session.Submit(new AssignCrewCommand(
                    EntityId.Parse("cmd:assign-eng"), Engineer,
                    EntityId.Parse("station:boiler"), session.Revision)).Success, Is.True);
                Assert.That(session.Submit(new ConsumeCrewProvisionCommand(
                    EntityId.Parse("cmd:eat"), Protagonist, ration, session.Revision)).Success, Is.True);
                Assert.That(session.Submit(new DamageCrewCommand(
                    EntityId.Parse("cmd:wound"), Protagonist, 200, session.Revision)).Success, Is.True);

                var depart = ShoreCrewTransfer.Transfer(session, new[] { Protagonist },
                    toShore: true, EntityId.Parse("transition:shore:01"));
                Assert.That(depart.Success, Is.True);

                session.Advance(15);

                var back = ShoreCrewTransfer.Transfer(session, new[] { Protagonist },
                    toShore: false, EntityId.Parse("transition:ship:01"));
                Assert.That(back.Success, Is.True);

                var current = session.Capture().State;
                Assert.That(current.GetCrewIdentity(Protagonist), Is.EqualTo(captainIdentity));
                Assert.That(current.GetCrewIdentity(Engineer), Is.EqualTo(engineerIdentity));
                Assert.That(current.CrewDuty.Get(Protagonist).Identity, Is.EqualTo(captainIdentity));
                Assert.That(current.CrewDuty.Get(Engineer).Identity, Is.EqualTo(engineerIdentity));
                Assert.That(current.CrewDuty.Get(Protagonist).DisplayName, Is.EqualTo("Capt. Mara Solberg"));
                Assert.That(current.CrewDuty.Get(Engineer).DisplayName, Is.EqualTo("Soren Lindqvist"));
            }
        }

        [Test]
        public void RecruitCrewCommandAdmitsAuthoredSurvivorOnceAndIntegratesWithShipSystems()
        {
            var candidateIdentity = new CrewIdentity(SurvivorEngineer, "Astrid Holm", "crew.astrid");
            var candidate = new SurvivorCandidate(
                candidateIdentity, DistressSite, DutyRole.Engineering,
                primarySkill: 900, backupSkill: 250, shiftStart: 0, shiftMinutes: 1440,
                fatigue: 100, wounds: 50, hunger: 120, thirst: 80);

            var crew = CrewState.Create(
                new[]
                {
                    new CrewMember(new CrewIdentity(Protagonist, "Capt. Mara Solberg"),
                        DutyRole.Watch, 850, 100, 0, 1440)
                },
                candidates: new[] { candidate });

            var thermal = ShipThermalState.Create(
                new[] { new ThermalCompartment(BoilerRoom, 1000, 20_000_000, 2000, 700, 1000) },
                Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(BoilerRoom, 1, 10_000));

            var initial = WorldState.Create(new WorldMinute(120),
                new[] { new SupplyStock(Fuel, 100) }, new[] { Protagonist },
                thermal: thermal, crewDuty: crew);

            long unstaffedEnergyAfter10Min;
            using (var unstaffedControl = new WorldSession(initial, ambientMilliC: _ => -20_000))
            {
                unstaffedControl.Advance(10);
                unstaffedEnergyAfter10Min = unstaffedControl.Capture().State.Thermal.TotalEnergy;
            }

            using (var session = new WorldSession(initial, ambientMilliC: _ => -20_000))
            {
                // Before recruiting the engineer, the watch captain has backup < 200 so boiler is unstaffed.
                Assert.That(session.Capture().State.CrewDuty.EffectiveSkill(
                    DutyRole.Engineering, session.Minute), Is.Zero);

                var outcome = session.Submit(new RecruitCrewCommand(
                    EntityId.Parse("cmd:recruit:astrid"), SurvivorEngineer,
                    DistressSite, session.Revision));

                Assert.That(outcome.Success, Is.True);
                Assert.That(outcome.Failure, Is.EqualTo(CommandFailure.None));
                Assert.That(outcome.Events, Has.Count.EqualTo(1));
                Assert.That(outcome.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewRecruited));
                Assert.That(outcome.Events[0].SubjectId, Is.EqualTo(SurvivorEngineer));
                Assert.That(outcome.Events[0].RelatedId, Is.EqualTo(DistressSite));

                var committed = session.Capture().State;
                Assert.That(committed.CrewCount, Is.EqualTo(2));
                Assert.That(committed.HasCrew(SurvivorEngineer), Is.True);
                Assert.That(committed.GetCrewIdentity(SurvivorEngineer), Is.EqualTo(candidateIdentity));
                Assert.That(committed.CrewDuty.Get(SurvivorEngineer).DisplayName, Is.EqualTo("Astrid Holm"));
                Assert.That(committed.CrewDuty.GetCandidate(SurvivorEngineer).IsRecruited, Is.True);
                Assert.That(committed.CrewDuty.EffectiveSkill(DutyRole.Engineering, committed.Minute),
                    Is.GreaterThan(0));

                // Advancing the world clock now burns boiler fuel and staffs the recruited engineer.
                session.Advance(10);
                var afterTick = session.Capture().State;
                Assert.That(afterTick.GetSupply(Fuel), Is.EqualTo(90));
                Assert.That(afterTick.Thermal.TotalEnergy, Is.GreaterThan(unstaffedEnergyAfter10Min));
                Assert.That(afterTick.CrewDuty.Get(SurvivorEngineer).Fatigue, Is.EqualTo(110));

                // Attempting to recruit the same survivor again rejects without mutating state.
                var duplicateAdmission = session.Submit(new RecruitCrewCommand(
                    EntityId.Parse("cmd:recruit:astrid-again"), SurvivorEngineer,
                    DistressSite, session.Revision));
                Assert.That(duplicateAdmission.Success, Is.False);
                Assert.That(duplicateAdmission.Failure, Is.EqualTo(CommandFailure.CrewAlreadyRecruited));
                Assert.That(duplicateAdmission.State, Is.SameAs(afterTick));
            }
        }

        [Test]
        public void RecruitmentRejectsSpontaneousUnknownCandidateOrMismatchedSource()
        {
            var candidate = new SurvivorCandidate(
                SurvivorMedic, "Dr. Ilya Varga", DistressSite,
                DutyRole.Medical, 900, 200, 0, 1440);
            var crew = CrewState.Create(
                new[] { new CrewMember(Protagonist, DutyRole.Watch, 900, 200, 0, 1440) },
                candidates: new[] { candidate });
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Protagonist }, crewDuty: crew);
            var bus = new WorldCommandBus();

            var spontaneous = bus.Execute(world, new RecruitCrewCommand(
                EntityId.Parse("cmd:spontaneous"), EntityId.Parse("crew:invented"), 0));
            Assert.That(spontaneous.Success, Is.False);
            Assert.That(spontaneous.Failure, Is.EqualTo(CommandFailure.UnknownRecruitmentCandidate));
            Assert.That(spontaneous.State, Is.SameAs(world));

            var wrongSource = bus.Execute(world, new RecruitCrewCommand(
                EntityId.Parse("cmd:wrong-source"), SurvivorMedic, WreckSite, 0));
            Assert.That(wrongSource.Success, Is.False);
            Assert.That(wrongSource.Failure, Is.EqualTo(CommandFailure.InvalidRecruitmentSource));
            Assert.That(wrongSource.State, Is.SameAs(world));

            var invalidSource = bus.Execute(world, new RecruitCrewCommand(
                EntityId.Parse("cmd:empty-source"), SurvivorMedic, default, 0));
            Assert.That(invalidSource.Success, Is.False);
            Assert.That(invalidSource.Failure, Is.EqualTo(CommandFailure.InvalidRecruitmentSource));
            Assert.That(invalidSource.State, Is.SameAs(world));
        }

        [Test]
        public void HardCapOfTwelvePeopleIncludingProtagonistIsEnforcedEvenWhenPartyIsAshore()
        {
            Assert.That(WorldState.MaxCrewCapacity, Is.EqualTo(12));
            Assert.That(CrewState.MaxCrewCapacity, Is.EqualTo(12));

            // Start with 11 people aboard (Protagonist + 10 crew) and 2 known candidates.
            var elevenIds = Enumerable.Range(1, 11)
                .Select(i => i == 1 ? Protagonist : EntityId.Parse($"crew:{i:D2}"))
                .ToArray();
            var elevenMembers = elevenIds.Select((id, index) =>
                new CrewMember(
                    new CrewIdentity(id, index == 0 ? "Capt. Mara Solberg" : $"Crew {index + 1:D2}"),
                    DutyRole.Watch, 800, 200, 0, 1440,
                    isAway: index < 3)).ToArray(); // 3 away on shore expedition

            var candidateTwelve = new SurvivorCandidate(
                SurvivorEngineer, "Astrid Holm", DistressSite,
                DutyRole.Engineering, 900, 200, 0, 1440);
            var candidateThirteen = new SurvivorCandidate(
                SurvivorMedic, "Dr. Ilya Varga", WreckSite,
                DutyRole.Medical, 920, 200, 0, 1440);

            var crew = CrewState.Create(elevenMembers,
                candidates: new[] { candidateTwelve, candidateThirteen });
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), elevenIds, crewDuty: crew);
            var bus = new WorldCommandBus();

            // Recruiting the 12th person succeeds (reaching the 12-person ceiling).
            var twelfth = bus.Execute(world, new RecruitCrewCommand(
                EntityId.Parse("cmd:recruit:12"), SurvivorEngineer, DistressSite, world.Revision));
            Assert.That(twelfth.Success, Is.True);
            Assert.That(twelfth.State.CrewCount, Is.EqualTo(12));
            Assert.That(twelfth.State.CrewDuty.Members.Count, Is.EqualTo(12));

            // Recruiting the 13th person rejects because 12 people (including away party + protagonist) are already in the ship's roster.
            var thirteenth = bus.Execute(twelfth.State, new RecruitCrewCommand(
                EntityId.Parse("cmd:recruit:13"), SurvivorMedic, WreckSite, twelfth.State.Revision));
            Assert.That(thirteenth.Success, Is.False);
            Assert.That(thirteenth.Failure, Is.EqualTo(CommandFailure.CrewCapacityReached));
            Assert.That(thirteenth.State, Is.SameAs(twelfth.State));
            Assert.That(thirteenth.State.CrewCount, Is.EqualTo(12));
            Assert.That(thirteenth.State.CrewDuty.GetCandidate(SurvivorMedic).IsRecruited, Is.False);

            // Creating a WorldState or CrewState with 13 people directly is rejected.
            var thirteenIds = elevenIds.Concat(new[] { SurvivorEngineer, SurvivorMedic }).ToArray();
            var thirteenMembers = elevenMembers.Concat(new[]
            {
                candidateTwelve.ToCrewMember(),
                candidateThirteen.ToCrewMember()
            }).ToArray();

            Assert.Throws<ArgumentException>(() => CrewState.Create(thirteenMembers));
            Assert.Throws<ArgumentException>(() => WorldState.Create(
                WorldMinute.Zero, Array.Empty<SupplyStock>(), thirteenIds));
        }

        [Test]
        public void SaveCodecRoundTripsNamedIdentitiesAndRecruitmentStateByteIdentically()
        {
            var captainIdentity = new CrewIdentity(Protagonist, "Capt. Mara Solberg", "crew.mara");
            var engineerIdentity = new CrewIdentity(Engineer, "Soren Lindqvist", "crew.soren");
            var candidate1 = new SurvivorCandidate(
                new CrewIdentity(SurvivorEngineer, "Astrid Holm", "crew.astrid"),
                DistressSite, DutyRole.Engineering, 900, 200, 0, 1440, fatigue: 150);
            var candidate2 = new SurvivorCandidate(
                new CrewIdentity(SurvivorMedic, "Dr. Ilya Varga", "crew.ilya"),
                WreckSite, DutyRole.Medical, 920, 250, 480, 720, wounds: 100);

            var crew = CrewState.Create(
                new[]
                {
                    new CrewMember(captainIdentity, DutyRole.Watch, 900, 200, 0, 1440),
                    new CrewMember(engineerIdentity, DutyRole.Repair, 850, 200, 0, 1440)
                },
                candidates: new[] { candidate1, candidate2 });
            var world = WorldState.Create(new WorldMinute(300),
                Array.Empty<SupplyStock>(), new[] { Protagonist, Engineer },
                crewDuty: crew);

            using (var session = new WorldSession(world))
            {
                Assert.That(session.Submit(new RecruitCrewCommand(
                    EntityId.Parse("cmd:recruit:astrid"), SurvivorEngineer,
                    DistressSite, session.Revision)).Success, Is.True);

                var snapshot = session.Capture();
                byte[] bytes = SaveCodec.Encode(snapshot);
                Assert.That(BitConverter.ToUInt16(bytes, 8), Is.EqualTo(SaveCodec.CurrentVersion));

                var decoded = SaveCodec.TryDecode(bytes);
                Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
                var restoredState = decoded.Value.State;

                Assert.That(restoredState.Fingerprint(), Is.EqualTo(snapshot.State.Fingerprint()));
                Assert.That(restoredState.CrewCount, Is.EqualTo(3));
                Assert.That(restoredState.GetCrewIdentity(Protagonist), Is.EqualTo(captainIdentity));
                Assert.That(restoredState.GetCrewIdentity(Engineer), Is.EqualTo(engineerIdentity));
                Assert.That(restoredState.GetCrewIdentity(SurvivorEngineer),
                    Is.EqualTo(candidate1.Identity));
                Assert.That(restoredState.CrewDuty.GetCandidate(SurvivorEngineer).IsRecruited, Is.True);
                Assert.That(restoredState.CrewDuty.GetCandidate(SurvivorMedic).IsRecruited, Is.False);
                Assert.That(restoredState.CrewDuty.GetCandidate(SurvivorMedic).DisplayName,
                    Is.EqualTo("Dr. Ilya Varga"));
                Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(bytes));

                // After restoring into a session, already-recruited candidate cannot be recruited again,
                // while the remaining unrecruited candidate can be admitted cleanly.
                using (var restoredSession = WorldSession.Restore(decoded.Value))
                {
                    var duplicate = restoredSession.Submit(new RecruitCrewCommand(
                        EntityId.Parse("cmd:dup"), SurvivorEngineer,
                        DistressSite, restoredSession.Revision));
                    Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.CrewAlreadyRecruited));

                    var secondAdmit = restoredSession.Submit(new RecruitCrewCommand(
                        EntityId.Parse("cmd:recruit:ilya"), SurvivorMedic,
                        WreckSite, restoredSession.Revision));
                    Assert.That(secondAdmit.Success, Is.True);
                    Assert.That(restoredSession.Read().CrewCount, Is.EqualTo(4));
                    Assert.That(restoredSession.Read().GetCrewIdentity(SurvivorMedic).DisplayName,
                        Is.EqualTo("Dr. Ilya Varga"));
                }
            }
        }

        [Test]
        public void LegacyV19SaveMigratesDeterministicallyToStableDerivedCrewIdentities()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Protagonist, Engineer, Scout },
                crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(Protagonist, DutyRole.Watch, 900, 200, 0, 1440),
                    new CrewMember(Engineer, DutyRole.Engineering, 950, 150, 0, 1440),
                    new CrewMember(Scout, DutyRole.Repair, 850, 150, 0, 1440)
                }));
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 19);
            var encoded = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler));
            var v19 = (byte[])encoded.Clone();
            v19[8] = 19;
            v19[9] = 0;
            RecalculateChecksum(v19);

            var firstLoad = SaveCodec.TryDecode(v19);
            var secondLoad = SaveCodec.TryDecode(v19);
            Assert.That(firstLoad.Success, Is.True, firstLoad.Error.ToString());
            Assert.That(secondLoad.Success, Is.True, secondLoad.Error.ToString());

            Assert.That(firstLoad.Value.State.GetCrewIdentity(Protagonist).DisplayName,
                Is.EqualTo("Captain"));
            Assert.That(firstLoad.Value.State.GetCrewIdentity(Engineer).DisplayName,
                Is.EqualTo("Engineer"));
            Assert.That(firstLoad.Value.State.GetCrewIdentity(Scout).DisplayName,
                Is.EqualTo("Scout"));
            Assert.That(firstLoad.Value.State.Fingerprint(),
                Is.EqualTo(secondLoad.Value.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(firstLoad.Value), Is.EqualTo(encoded));
        }

        [Test]
        public void TamperedSaveWithOverCapacityRosterOrPhantomRecruitmentRejectsOnDecode()
        {
            // Craft a save blob claiming 13 crew members in the R0 registry and recalculate SHA-256.
            var validState = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Protagonist });
            var scheduler = new SimulationScheduler(new WorldClock(WorldMinute.Zero),
                Array.Empty<IWorldMinuteSystem>(), seed: 1);
            byte[] baseline = SaveCodec.Encode(SaveGameDto.Capture(validState, scheduler));

            byte[] forgedOverCap = BuildForgedOverCapacitySave();
            var decodedOverCap = SaveCodec.TryDecode(forgedOverCap);
            Assert.That(decodedOverCap.Success, Is.False);
            Assert.That(decodedOverCap.Error, Is.EqualTo(SaveDecodeError.InvalidData));

            // Craft a v20 blob with an empty (0, 0) v20 identity/candidate trailer and verify non-canonical rejection.
            byte[] nonCanonical = AppendEmptyV20Trailer(baseline);
            var decodedNonCanonical = SaveCodec.TryDecode(nonCanonical);
            Assert.That(decodedNonCanonical.Success, Is.False);
            Assert.That(decodedNonCanonical.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void R0CustomIdentitiesAndCandidateInvariantsArePreservedAndValidated()
        {
            var r0Identity = new CrewIdentity(Protagonist, "Capt. Mara Solberg", "crew.name.mara_solberg");
            var r0State = WorldState.Create(new WorldMinute(15),
                new[] { new SupplyStock(Fuel, 20) },
                new[] { Protagonist },
                crewIdentities: new[] { r0Identity });

            using (var session = new WorldSession(r0State))
            {
                Assert.That(session.Read().GetCrewIdentity(Protagonist), Is.EqualTo(r0Identity));
                byte[] bytes = SaveCodec.Encode(session.Capture());
                var decodedResult = SaveCodec.TryDecode(bytes);
                Assert.That(decodedResult.Success, Is.True);
                Assert.That(decodedResult.Value.State.GetCrewIdentity(Protagonist), Is.EqualTo(r0Identity));
                Assert.That(decodedResult.Value.State.Fingerprint(), Is.EqualTo(r0State.Fingerprint()));

                // RestoreSnapshot in place preserves identity on the same session instance.
                session.RestoreSnapshot(decodedResult.Value);
                Assert.That(session.Read().GetCrewIdentity(Protagonist), Is.EqualTo(r0Identity));
            }

            // CrewMember.WithIdentity rejects a mismatched EntityId.
            var member = new CrewMember(Protagonist, DutyRole.Watch, 800, 100, 0, 1440);
            Assert.That(() => member.WithIdentity(new CrewIdentity(Engineer, "Erik Lind")),
                Throws.TypeOf<ArgumentException>());

            // Duplicate candidate IDs or unrecruited candidate already in roster are rejected.
            var c1 = new SurvivorCandidate(
                new CrewIdentity(SurvivorMedic, "Dr. Nori Sato"),
                WreckSite, DutyRole.Medical, 900, 100, 0, 1440);
            var c2 = new SurvivorCandidate(
                new CrewIdentity(SurvivorMedic, "Duplicate ID"),
                DistressSite, DutyRole.Watch, 700, 100, 0, 1440);
            Assert.That(() => CrewState.Create(new[] { member }, candidates: new[] { c1, c2 }),
                Throws.TypeOf<ArgumentException>());

            var alreadyOnRosterButUnrecruited = new SurvivorCandidate(
                new CrewIdentity(Protagonist, "Capt. Mara Solberg"),
                WreckSite, DutyRole.Watch, 800, 100, 0, 1440, isRecruited: false);
            Assert.That(() => CrewState.Create(new[] { member }, candidates: new[] { alreadyOnRosterButUnrecruited }),
                Throws.TypeOf<ArgumentException>());
        }

        private static byte[] BuildForgedOverCapacitySave()
        {
            var utf8 = new UTF8Encoding(false, true);
            byte[] payload;
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, utf8, true))
            {
                writer.Write(0L); // minute
                writer.Write(0L); // revision
                writer.Write(1u); // seed
                writer.Write(0);  // supplies count
                writer.Write(13); // 13 crew IDs (exceeds MaxCrewCapacity = 12)
                for (int i = 1; i <= 13; i++)
                {
                    byte[] idBytes = utf8.GetBytes($"crew:{i:D2}");
                    writer.Write(idBytes.Length);
                    writer.Write(idBytes);
                }
                writer.Write(0); // assignments
                writer.Write(0); // processed commands
                writer.Write(0); // pending events
                writer.Write(0); // reserved events
                writer.Write(0); // overrides
                // v3 empty inventory (44 bytes: catalog=0, pickups=0, 4 long/int caps, hold=0, pack=0)
                writer.Write(0);
                writer.Write(0);
                writer.Write(0L);
                writer.Write(0);
                writer.Write(0L);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                // v4 empty thermal (28 bytes)
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(byte.MinValue);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0L);
                writer.Write(0L);
                // v5 empty crew duty (8 bytes)
                writer.Write(0);
                writer.Write(0);
                // v6 empty layout (12 bytes)
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                // v7 empty navigation (17 bytes)
                writer.Write(0);
                writer.Write((byte)0);
                writer.Write(0);
                writer.Write(0L);
                // v8 credited inventory (4 bytes)
                writer.Write(0);
                // v9 empty economy (16 bytes)
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                writer.Write(0);
                // v10 empty combat (12 bytes)
                writer.Write(0L);
                writer.Write(0);
                // v11 empty medical (4 bytes)
                writer.Write(0);
                // v12 empty site progress (4 bytes)
                writer.Write(0);
                // v13 absent scene checkpoint (1 byte)
                writer.Write((byte)0);
                // v14 empty magazine extension (4 bytes)
                writer.Write(0);
                // v15 empty recovery extension (4 bytes)
                writer.Write(0);
                // v16 empty needs extension (8 bytes)
                writer.Write(0);
                writer.Write(0);
                // v17 empty interactions extension (8 bytes)
                writer.Write(0);
                writer.Write(0);
                payload = ms.ToArray();
            }

            return WrapEnvelope(payload, SaveCodec.CurrentVersion);
        }

        private static byte[] AppendEmptyV20Trailer(byte[] validBaseline)
        {
            int oldPayloadLength = BitConverter.ToInt32(validBaseline, 10);
            byte[] payload = new byte[oldPayloadLength + 16]; // moves=0, locations=0, identities=0, candidates=0
            Array.Copy(validBaseline, 14, payload, 0, oldPayloadLength);
            return WrapEnvelope(payload, SaveCodec.CurrentVersion);
        }

        private static byte[] WrapEnvelope(byte[] payload, ushort version)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms, Encoding.ASCII, true))
            {
                writer.Write(Encoding.ASCII.GetBytes("FARKSAVE"));
                writer.Write(version);
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                byte[] prefix = ms.ToArray();
                byte[] full = new byte[prefix.Length + 32];
                Array.Copy(prefix, full, prefix.Length);
                RecalculateChecksum(full);
                return full;
            }
        }

        private static void RecalculateChecksum(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes, 0, bytes.Length - 32);
                Array.Copy(hash, 0, bytes, bytes.Length - 32, 32);
            }
        }
    }
}
