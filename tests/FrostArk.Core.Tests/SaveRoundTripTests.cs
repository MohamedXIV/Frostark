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
            byte[] v4 = SaveCodec.Encode(withoutOverrides);
            // v5 adds 8 bytes for an empty crew roster, v4 adds 28 bytes for
            // an empty thermal graph, v3 adds 44
            // for an empty inventory, and v2 adds 4 for overrides.
            byte[] legacy = DowngradeEmptyInventory(v4, version: 1, removedBytes: 142);

            var result = SaveCodec.TryDecode(legacy);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(result.Value.ProceduralOverrides, Is.Empty);
            Assert.That(result.Value.State.Fingerprint(),
                Is.EqualTo(MakeSnapshot().State.Fingerprint()));
            Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(result.Value)).Success, Is.True);
        }

        [Test]
        public void LegacyV2WithoutInventoryMigratesToEmptyInventory()
        {
            var original = MakeSnapshot();
            byte[] v4 = SaveCodec.Encode(original);
            Assert.That(v4[8], Is.EqualTo(14));
            byte[] v2 = DowngradeEmptyInventory(v4, version: 2, removedBytes: 138);
            var loaded = SaveCodec.TryDecode(v2);

            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.ProceduralOverrides, Has.Count.EqualTo(2));
            Assert.That(loaded.Value.State.Inventory.PackMaxSlots, Is.Zero);
            Assert.That(loaded.Value.State.Inventory.GetUsedWeightGrams(
                FrostArk.Core.Inventory.InventoryContainer.ShipHold), Is.Zero);
            Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(loaded.Value)).Success, Is.True);
        }

        [Test]
        public void LegacyV3InventoryMigratesWithoutInventingThermalState()
        {
            var current = MakeSnapshot();
            var v4 = SaveCodec.Encode(current);
            Assert.That(v4[8], Is.EqualTo(14));
            var v3 = DowngradeEmptyInventory(v4, version: 3, removedBytes: 94);
            var result = SaveCodec.TryDecode(v3);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(result.Value.State.Thermal.Rooms, Is.Empty);
            Assert.That(result.Value.State.Fingerprint(),
                Is.EqualTo(current.State.Fingerprint()));
            Assert.That(SaveCodec.TryDecode(SaveCodec.Encode(result.Value)).Success, Is.True);
        }

        [Test]
        public void LegacyV7SnapshotWithoutCreditsMigratesWithIdenticalWorld()
        {
            var original = MakeSnapshot();
            var current = SaveCodec.Encode(original);
            Assert.That(current[8], Is.EqualTo(14));
            // The new v8 tail is exactly one credited-inflow entry count,
            // zero for an old world. Strip it to form the original v7 bytes.
            var old = DowngradeEmptyInventory(current, version: 7, removedBytes: 45);
            var decoded = SaveCodec.TryDecode(old);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.Inventory.SnapshotCredited(), Is.Empty);
            Assert.That(decoded.Value.State.Fingerprint(),
                Is.EqualTo(original.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(current));
        }

        [Test]
        public void LegacyV8WithoutWorkshopOrMarketsMigratesToEmptyEconomy()
        {
            var current = MakeSnapshot();
            var v9 = SaveCodec.Encode(current);
            Assert.That(v9[8], Is.EqualTo(14));
            // V9 added 4 empty collection counts for recipes, stations,
            // pending prepaid jobs and finite merchants.
            var v8 = DowngradeEmptyInventory(v9, version: 8, removedBytes: 41);
            var restored = SaveCodec.TryDecode(v8);
            Assert.That(restored.Success, Is.True, restored.Error.ToString());
            Assert.That(restored.Value.State.Economy.Jobs, Is.Empty);
            Assert.That(restored.Value.State.Economy.Markets, Is.Empty);
            Assert.That(restored.Value.State.Fingerprint(),
                Is.EqualTo(current.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(restored.Value), Is.EqualTo(v9));
        }

        [Test]
        public void LegacyV9WithoutCombatMigratesToEmptyWeaponsAndZeroTacticalTick()
        {
            var original = MakeSnapshot();
            var v10 = SaveCodec.Encode(original);
            Assert.That(v10[8], Is.EqualTo(14));
            // V10 appends one int64 tactical tick plus one empty weapon count.
            var v9 = DowngradeEmptyInventory(v10, version: 9, removedBytes: 25);
            var recovered = SaveCodec.TryDecode(v9);
            Assert.That(recovered.Success, Is.True, recovered.Error.ToString());
            Assert.That(recovered.Value.State.Combat.Tick, Is.Zero);
            Assert.That(recovered.Value.State.Combat.Weapons, Is.Empty);
            Assert.That(recovered.Value.State.Fingerprint(),
                Is.EqualTo(original.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(recovered.Value), Is.EqualTo(v10));
        }

        [Test]
        public void LegacyV10WithoutMedicalProtocolsMigratesWithOriginalCrewWounds()
        {
            var current = MakeSnapshot();
            var v11 = SaveCodec.Encode(current);
            Assert.That(v11[8], Is.EqualTo(14));
            // Empty v11 medical catalog = one 4-byte count after v10 combat.
            var v10 = DowngradeEmptyInventory(v11, version: 10, removedBytes: 13);
            var result = SaveCodec.TryDecode(v10);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(result.Value.State.CrewDuty.Treatments, Is.Empty);
            Assert.That(result.Value.State.Fingerprint(),
                Is.EqualTo(current.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(result.Value), Is.EqualTo(v11));
        }

        [Test]
        public void LegacyV11WithoutIslandSiteProgressMigratesEmpty()
        {
            var now = MakeSnapshot();
            var v12 = SaveCodec.Encode(now);
            Assert.That(v12[8], Is.EqualTo(14));
            // V12 appended 4-byte site count, v13 the 1-byte absent-checkpoint flag, and v14 a 4-byte empty magazine-extension count.
            var v11 = DowngradeEmptyInventory(v12, version: 11, removedBytes: 9);
            var loaded = SaveCodec.TryDecode(v11);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.SiteProgress.Sites, Is.Empty);
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(now.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(loaded.Value), Is.EqualTo(v12));
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


        [Test]
        public void RecalculatedChecksumCannotAuthorizeDuplicateOverrideIds()
        {
            byte[] encoded = SaveCodec.Encode(MakeSnapshot());
            byte[] marker = System.Text.Encoding.UTF8.GetBytes("site:ice-2");
            int position = -1;
            for (int index = 0; index <= encoded.Length - marker.Length - 32; index++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                    if (encoded[index + j] != marker[j]) { match = false; break; }
                if (match) { position = index; break; }
            }

            Assert.That(position, Is.GreaterThanOrEqualTo(0));
            encoded[position + marker.Length - 1] = (byte)'1';
            RecalculateChecksum(encoded);

            var result = SaveCodec.TryDecode(encoded);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SaveDecodeError.InvalidData));
            Assert.That(result.Value, Is.Null);
        }

        [Test]
        public void StateAndTimelineMinuteMustMatchAtCapture()
        {
            var snapshot = MakeSnapshot();
            var ahead = new SimulationScheduler(new WorldClock(new WorldMinute(1321)),
                Array.Empty<IWorldMinuteSystem>());
            Assert.Throws<ArgumentException>(() => SaveGameDto.Capture(snapshot.State, ahead));
            Assert.That(snapshot.State.Minute.Value, Is.EqualTo(1320));
        }

        private static byte[] DowngradeEmptyInventory(byte[] encoded, ushort version, int removedBytes)
        {
            const int header = 14;
            const int checksum = 32;
            int payloadLength = BitConverter.ToInt32(encoded, 10) - removedBytes;
            var legacy = new byte[header + payloadLength + checksum];
            Array.Copy(encoded, legacy, header + payloadLength);
            legacy[8] = (byte)version;
            legacy[9] = 0;
            Array.Copy(BitConverter.GetBytes(payloadLength), 0, legacy, 10, 4);
            RecalculateChecksum(legacy);
            return legacy;
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
