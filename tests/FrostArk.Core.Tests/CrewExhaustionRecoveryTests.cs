using System;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewExhaustionRecoveryTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId Backup = EntityId.Parse("crew:backup");

        private static CrewState Roster(int initialFatigue = 899) => CrewState.Create(new[]
        {
            new CrewMember(Engineer, DutyRole.Engineering, 1000, 100, 0, 1440,
                fatigue: initialFatigue),
            new CrewMember(Backup, DutyRole.Watch, 800, 600, 0, 1440)
        });

        private static WorldState World(CrewState crew) =>
            WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                new[] { Engineer, Backup }, crewDuty: crew);

        private static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> write) =>
            new SimulationScheduler(new WorldClock(read().Minute), new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(read, write),
                new ShipThermalMinuteSystem(read, write, _ => 0)
            }, seed: 11);

        [Test]
        public void ExhaustedEngineerYieldsToAvailableBackupAndRejoinsAfterFullRest()
        {
            var crew = Roster();
            Assert.That(crew.Get(Engineer).IsRecovering, Is.False);
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1)),
                Is.GreaterThan(0));

            crew = crew.Tick(new WorldMinute(1)); // 899 -> 900; recovery latches.
            Assert.That(crew.Get(Engineer).Fatigue, Is.EqualTo(900));
            Assert.That(crew.Get(Engineer).IsRecovering, Is.True);
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, new WorldMinute(2)),
                Is.GreaterThan(0), "A real trained backup should take the watch.");
            Assert.That(crew.EffectiveSkill(DutyRole.Watch, new WorldMinute(2)),
                Is.Zero, "The one backup cannot staff two duties.");

            // Recovery takes 250 unassigned minutes: 900 -> 400.
            for (int minute = 2; minute <= 251; minute++)
                crew = crew.Tick(new WorldMinute(minute));

            Assert.That(crew.Get(Engineer).Fatigue, Is.EqualTo(400));
            Assert.That(crew.Get(Engineer).IsRecovering, Is.False);
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, new WorldMinute(252)),
                Is.EqualTo(800));
        }

        [Test]
        public void NoAvailableReliefMeansUnstaffedDutyNotImaginaryWorker()
        {
            var only = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0, 0, 1440,
                    fatigue: 900)
            });
            Assert.That(only.Get(Engineer).IsRecovering, Is.True);
            Assert.That(only.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1)),
                Is.Zero);
            var next = only.Tick(new WorldMinute(1));
            Assert.That(next.Get(Engineer).Fatigue, Is.EqualTo(898));
            Assert.That(next.EffectiveSkill(DutyRole.Engineering, new WorldMinute(2)),
                Is.Zero, "Partial rest cannot cause work/rest oscillation.");
        }

        [Test]
        public void ExpeditionAbsenceDoesNotMagicallyRecoverFatigue()
        {
            var member = new CrewMember(Engineer, DutyRole.Engineering, 900, 0, 0, 1440,
                fatigue: 650, isAway: true, isRecovering: true);
            var crew = CrewState.Create(new[] { member });
            for (int minute = 1; minute <= 60; minute++)
                crew = crew.Tick(new WorldMinute(minute));
            Assert.That(crew.Get(Engineer).Fatigue, Is.EqualTo(650));
            Assert.That(crew.Get(Engineer).IsRecovering, Is.True);
            crew = crew.SetAway(Engineer, false).Tick(new WorldMinute(61));
            Assert.That(crew.Get(Engineer).Fatigue, Is.EqualTo(648));
            Assert.That(crew.Get(Engineer).IsRecovering, Is.True);
        }

        [Test]
        public void RecoveryLatchRoundTripsAtIntermediateFatigue()
        {
            // At this fatigue, a recovered worker is available but one already
            // resting still requires a full recovery; this MUST survive save.
            var recovering = Roster(600);
            recovering = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 100, 0, 1440,
                    fatigue: 600, isRecovering: true),
                recovering.Get(Backup)!
            });
            var available = Roster(600);
            Assert.That(recovering.Fingerprint(), Is.Not.EqualTo(available.Fingerprint()));

            var initial = World(recovering);
            var scheduler = Bind(() => initial, _ => { });
            var encoded = SaveCodec.Encode(SaveGameDto.Capture(initial, scheduler));
            var read = SaveCodec.TryDecode(encoded);
            Assert.That(read.Success, Is.True, read.Error.ToString());
            Assert.That(read.Value.State.CrewDuty.Get(Engineer).IsRecovering, Is.True);
            Assert.That(read.Value.State.CrewDuty.Get(Engineer).Fatigue, Is.EqualTo(600));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(read.Value.State,
                read.Value.CreateScheduler(Array.Empty<IWorldMinuteSystem>()))),
                Is.EqualTo(encoded));
        }

        [Test]
        public void InvalidRecoveryFlagIsRejectedEvenWithRecomputedChecksum()
        {
            var roster = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 900, 0, 0, 1440,
                    fatigue: 600, isRecovering: true)
            });
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer }, crewDuty: roster);
            var scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler));

            // The v15 recovery extension is the final payload section,
            // immediately before the 32-byte SHA-256 checksum.
            int v16Needs = 8 + 12 + Encoding.UTF8.GetByteCount(Engineer.Value);
            bytes[bytes.Length - 33 - 8 - v16Needs] = 2;
            using (var sha = SHA256.Create())
            {
                var checksum = sha.ComputeHash(bytes, 0, bytes.Length - 32);
                Array.Copy(checksum, 0, bytes, bytes.Length - 32, 32);
            }
            var result = SaveCodec.TryDecode(bytes);
            Assert.That(result.Success, Is.False);
        }

        [Test]
        public void V14PayloadWithoutLatchExtensionInfersSafetyForAlreadyExhaustedCrew()
        {
            var roster = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 900, 0, 0, 1440,
                    fatigue: 950)
            });
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer }, crewDuty: roster);
            var scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var newest = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler));

            // Strip the trailing v15 recovery section and construct a valid
            // v14 envelope, as shipped v14 has no recovery-latch field.
            int extensionLength = 4 + 4 + Encoding.UTF8.GetByteCount(Engineer.Value) + 1;
            int v16Needs = 8 + 12 + Encoding.UTF8.GetByteCount(Engineer.Value);
            int payloadLength = BitConverter.ToInt32(newest, 10)
                - extensionLength - v16Needs - 8;
            var legacy = new byte[14 + payloadLength + 32];
            Array.Copy(newest, legacy, legacy.Length - 32);
            legacy[8] = 14;
            legacy[9] = 0;
            Array.Copy(BitConverter.GetBytes(payloadLength), 0, legacy, 10, 4);
            using (var sha = SHA256.Create())
            {
                var checksum = sha.ComputeHash(legacy, 0, legacy.Length - 32);
                Array.Copy(checksum, 0, legacy, legacy.Length - 32, 32);
            }
            var migrated = SaveCodec.TryDecode(legacy);
            Assert.That(migrated.Success, Is.True, migrated.Error.ToString());
            Assert.That(migrated.Value.State.CrewDuty.Get(Engineer).IsRecovering,
                Is.True, "An exhausted legacy crewmember must not be staffed immediately.");
        }

        [Test]
        public void MismatchedRecoveryRosterIdRejectsEvenWithValidChecksum()
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 950, 0, 0, 1440,
                    fatigue: 650, isRecovering: true)
            });
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer }, crewDuty: crew);
            var scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, scheduler));

            // v15 extension: count (4), length-prefixed UTF-8 ID (4 + N),
            // state flag (1), then the checksum (32). Replace the first ID
            // character without changing its length, then re-sign.
            int idLength = Encoding.UTF8.GetByteCount(Engineer.Value);
            int extensionBytes = 4 + 4 + idLength + 1;
            int v16Needs = 8 + 12 + Encoding.UTF8.GetByteCount(Engineer.Value);
            int firstIdCharacter = bytes.Length - 32 - 8 - v16Needs - extensionBytes + 8;
            bytes[firstIdCharacter] = (byte)'x';
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32),
                    0, bytes, bytes.Length - 32, 32);

            var result = SaveCodec.TryDecode(bytes);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void SaveResumeAcrossRecoveryThresholdMatchesUninterruptedRun()
        {
            var full = World(Roster());
            var uninterrupted = Bind(() => full, x => full = x);
            uninterrupted.Advance(310);

            var first = World(Roster());
            var before = Bind(() => first, x => first = x);
            before.Advance(130);
            var checkpoint = SaveCodec.TryDecode(SaveCodec.Encode(SaveGameDto.Capture(first, before)));
            Assert.That(checkpoint.Success, Is.True, checkpoint.Error.ToString());
            var resumed = checkpoint.Value.State;
            var after = checkpoint.Value.CreateScheduler(new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(() => resumed, x => resumed = x),
                new ShipThermalMinuteSystem(() => resumed, x => resumed = x, _ => 0)
            });
            after.Advance(180);

            Assert.That(resumed.Fingerprint(), Is.EqualTo(full.Fingerprint()));
            Assert.That(resumed.CrewDuty.Get(Engineer).IsRecovering, Is.EqualTo(
                full.CrewDuty.Get(Engineer).IsRecovering));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(resumed, after)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(full, uninterrupted))));
        }
    }
}
