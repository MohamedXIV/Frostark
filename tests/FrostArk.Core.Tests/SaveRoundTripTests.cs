using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FrostArk.Core.Commands;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class SaveRoundTripTests
    {
        private static readonly EntityId Crew = EntityId.Parse("crew:01");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");

        private static WorldState Start() => WorldState.Create(new WorldMinute(1320),
            new[] { new SupplyStock(Fuel, 40) }, new[] { Crew });

        private static SaveGameDto MakeSnapshot()
        {
            var state = Start();
            var bus = new WorldCommandBus();
            state = bus.Execute(state, new WithdrawSupplyCommand(EntityId.Parse("cmd:withdraw"),
                Fuel, 3, state.Revision)).State;
            state = bus.Execute(state, new AssignCrewCommand(EntityId.Parse("cmd:assign"),
                Crew, EntityId.Parse("station:boiler"), state.Revision)).State;
            var scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 712);
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                new WorldMinute(1635), "raider-alert", interruptsSleep: true));
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:storm"),
                new WorldMinute(1550), "storm"));
            return SaveGameDto.Capture(state, scheduler, new[]
            {
                new ProceduralOverride(EntityId.Parse("site:ice-1"), "salvaged"),
                new ProceduralOverride(EntityId.Parse("site:ice-2"), "door-open")
            });
        }

        [Test]
        public void IdenticalSnapshotsProduceIdenticalBytesAndRestoreCompleteTruth()
        {
            var original = MakeSnapshot();
            var bytes = SaveCodec.Encode(original);
            var sameBytes = SaveCodec.Encode(MakeSnapshot());
            var decoded = SaveCodec.TryDecode(bytes);

            Assert.That(bytes, Is.EqualTo(sameBytes));
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.Fingerprint(), Is.EqualTo(original.State.Fingerprint()));
            Assert.That(decoded.Value.State.Revision, Is.EqualTo(2));
            Assert.That(decoded.Value.State.GetSupply(Fuel), Is.EqualTo(37));
            Assert.That(decoded.Value.State.GetCrewStation(Crew),
                Is.EqualTo(EntityId.Parse("station:boiler")));
            Assert.That(decoded.Value.ProceduralOverrides.Single(x => x.Id.Value == "site:ice-1").Value,
                Is.EqualTo("salvaged"));
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(bytes));
        }

        [Test]
        public void ResumeAtInterruptedHazardMatchesUninterruptedExecution()
        {
            var before = MakeSnapshot();
            var uninterrupted = before.CreateScheduler(Array.Empty<IWorldMinuteSystem>());
            var uninterruptedResult = uninterrupted.AdvanceUntil(new WorldMinute(1800),
                new SleepInterruptionPolicy());
            Assert.That(uninterruptedResult.WasInterrupted, Is.True);
            Assert.That(uninterruptedResult.EndedAt.Value, Is.EqualTo(1635));

            var partial = before.CreateScheduler(Array.Empty<IWorldMinuteSystem>());
            partial.Advance(100);
            var atCheckpoint = SaveGameDto.Capture(before.State.AtMinute(partial.Clock.Current),
                partial, before.ProceduralOverrides);
            var restored = SaveCodec.TryDecode(SaveCodec.Encode(atCheckpoint));
            Assert.That(restored.Success, Is.True, restored.Error.ToString());
            var resumed = restored.Value.CreateScheduler(Array.Empty<IWorldMinuteSystem>());
            var result = resumed.AdvanceUntil(new WorldMinute(1800), new SleepInterruptionPolicy());

            Assert.That(result.WasInterrupted, Is.True);
            Assert.That(result.EndedAt, Is.EqualTo(uninterruptedResult.EndedAt));
            Assert.That(result.Interruption.Id, Is.EqualTo(uninterruptedResult.Interruption.Id));
            Assert.That(restored.Value.State.AtMinute(result.EndedAt).Fingerprint(),
                Is.EqualTo(before.State.AtMinute(uninterruptedResult.EndedAt).Fingerprint()));
        }

        [Test]
        public void AlreadyConsumedCommandAndAlreadyFiredEventRemainReservedAfterReload()
        {
            var snapshot = MakeSnapshot();
            var oldScheduler = snapshot.CreateScheduler(Array.Empty<IWorldMinuteSystem>());
            oldScheduler.Advance(230); // minute 1550: storm has fired
            var save = SaveGameDto.Capture(snapshot.State.AtMinute(oldScheduler.Clock.Current),
                oldScheduler, snapshot.ProceduralOverrides);
            var restored = SaveCodec.TryDecode(SaveCodec.Encode(save)).Value;

            var cmd = new WithdrawSupplyCommand(EntityId.Parse("cmd:withdraw"), Fuel, 1,
                restored.State.Revision);
            var replay = new WorldCommandBus().Execute(restored.State, cmd);
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));

            var scheduler = restored.CreateScheduler(Array.Empty<IWorldMinuteSystem>());
            Assert.Throws<InvalidOperationException>(() => scheduler.Schedule(
                new ScheduledWorldEvent(EntityId.Parse("event:storm"),
                    new WorldMinute(1700), "storm-again")));
            var advance = scheduler.AdvanceUntil(new WorldMinute(1800),
                new SleepInterruptionPolicy());
            Assert.That(advance.FiredEvents.Select(e => e.Id.Value), Is.EqualTo(new[] { "event:raid" }));
        }

        [Test]
        public void MutatedAndTruncatedBlobsRejectWithoutChangingLiveState()
        {
            var state = MakeSnapshot().State;
            byte[] bytes = SaveCodec.Encode(MakeSnapshot());
            var bad = (byte[])bytes.Clone();
            bad[bad.Length - 33] ^= 0x40;
            var corrupt = SaveCodec.TryDecode(bad);
            var cut = SaveCodec.TryDecode(bytes.Take(bytes.Length - 13).ToArray());

            Assert.That(corrupt.Success, Is.False);
            Assert.That(corrupt.Error, Is.EqualTo(SaveDecodeError.Corrupt));
            Assert.That(cut.Success, Is.False);
            Assert.That(state.Fingerprint(), Is.EqualTo(MakeSnapshot().State.Fingerprint()));
        }

        [Test]
        public void UnsupportedSchemaVersionFailsExplicitly()
        {
            var bytes = SaveCodec.Encode(MakeSnapshot());
            var unsupported = (byte[])bytes.Clone();
            unsupported[8] = 99;
            unsupported[9] = 0;
            RecalculateChecksum(unsupported);

            var decoded = SaveCodec.TryDecode(unsupported);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.Error, Is.EqualTo(SaveDecodeError.UnsupportedVersion));
        }

        [Test]
        public void LegacyV1WithoutOverridesMigratesToEmptyOverrideCatalog()
        {
            // A legitimate v1 payload ends immediately after reserved event IDs.
            // Build an otherwise-current snapshot with zero overrides, then
            // remove only the trailing v2 empty-overrides count.
            var current = MakeSnapshot();
            var withoutOverrides = SaveGameDto.Capture(current.State,
                current.CreateScheduler(Array.Empty<IWorldMinuteSystem>()));
            byte[] v2 = SaveCodec.Encode(withoutOverrides);
            var legacy = new byte[v2.Length - 4];
            Array.Copy(v2, legacy, 14 + (BitConverter.ToInt32(v2, 10) - 4));
            legacy[8] = 1; legacy[9] = 0;
            int length = BitConverter.ToInt32(v2, 10) - 4;
            Array.Copy(BitConverter.GetBytes(length), 0, legacy, 10, 4);
            RecalculateChecksum(legacy);

            var result = SaveCodec.TryDecode(legacy);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(result.Value.ProceduralOverrides, Is.Empty);
            Assert.That(result.Value.State.Fingerprint(),
                Is.EqualTo(MakeSnapshot().State.Fingerprint()));
            Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(result.Value)).Success, Is.True);
        }

        [Test]
        public void InvalidHeaderAndOversizedDataFailWithoutThrowing()
        {
            var bytes = SaveCodec.Encode(MakeSnapshot());
            bytes[0] ^= 0x1;
            Assert.That(SaveCodec.TryDecode(bytes).Success, Is.False);
            Assert.That(SaveCodec.TryDecode(null).Success, Is.False);
            Assert.That(SaveCodec.TryDecode(new byte[32]).Success, Is.False);
            Assert.That(SaveCodec.TryDecode(new byte[9_000_000]).Success, Is.False);
        }

        [Test]
        public void PrimaryCorruptionRecoversFromBackupAndReportsRecovery()
        {
            var store = new MemoryStore();
            var repo = new SaveRepository(store);
            var initial = MakeSnapshot();
            repo.Save(initial);
            var next = SaveGameDto.Capture(initial.State.AtMinute(new WorldMinute(1325)),
                new SimulationScheduler(new WorldClock(new WorldMinute(1325)),
                    Array.Empty<IWorldMinuteSystem>(), seed: 712));
            repo.Save(next);
            Assert.That(store.Backup, Is.EqualTo(SaveCodec.Encode(initial)));
            store.Primary[store.Primary.Length - 1] ^= 0x1;

            var load = repo.Load();

            Assert.That(load.Success, Is.True);
            Assert.That(load.UsedBackup, Is.True);
            Assert.That(load.Value.State.Fingerprint(), Is.EqualTo(initial.State.Fingerprint()));
        }

        [Test]
        public void BothCorruptSaveSlotsReportFailureNotFactoryReset()
        {
            var store = new MemoryStore();
            var repo = new SaveRepository(store);
            repo.Save(MakeSnapshot());
            repo.Save(MakeSnapshot());
            store.Primary[0] ^= 0x1;
            store.Backup[0] ^= 0x1;

            var result = repo.Load();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Value, Is.Null);
            Assert.That(result.UsedBackup, Is.False);
        }

        [Test]
        public void StorageCommitIsOneAtomicBoundaryAndEncodeFailureNeverCallsIt()
        {
            var store = new MemoryStore();
            var repo = new SaveRepository(store);
            repo.Save(MakeSnapshot());
            byte[] old = (byte[])store.Primary.Clone();
            store.ThrowOnCommit = true;
            Assert.Throws<IOException>(() => repo.Save(MakeSnapshot()));
            Assert.That(store.Primary, Is.EqualTo(old));
            Assert.That(store.Commits, Is.EqualTo(1));
            Assert.Throws<ArgumentNullException>(() => repo.Save(null));
            Assert.That(store.Commits, Is.EqualTo(1));
        }

        private static void RecalculateChecksum(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(bytes, 0, bytes.Length - 32);
                Array.Copy(hash, 0, bytes, bytes.Length - 32, 32);
            }
        }

        private sealed class MemoryStore : ISaveBlobStore
        {
            public byte[] Primary { get; set; }
            public byte[] Backup { get; set; }
            public int Commits { get; private set; }
            public bool ThrowOnCommit { get; set; }

            public byte[] ReadPrimary() => Primary == null ? null : (byte[])Primary.Clone();
            public byte[] ReadBackup() => Backup == null ? null : (byte[])Backup.Clone();

            public void Commit(byte[] primary, byte[] previousValidPrimary)
            {
                if (ThrowOnCommit) throw new IOException("simulated atomic write rejection");
                Primary = (byte[])primary.Clone();
                Backup = previousValidPrimary == null ? Backup : (byte[])previousValidPrimary.Clone();
                Commits++;
            }
        }
    }
}
