using System;
using System.IO;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    public sealed class LocalDiskSaveBlobStoreEditModeTests
    {
        private string _testDir;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Application.temporaryCachePath, "LocalDiskStoreTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
            {
                try { Directory.Delete(_testDir, true); } catch { }
            }
        }

        private static SaveGameDto CreateSampleSnapshot(long minute = 5)
        {
            var worldMinute = new WorldMinute(minute);
            var state = WorldState.Create(worldMinute,
                Array.Empty<SupplyStock>(),
                new[] { EntityId.Parse("actor:protagonist") });
            var clock = new WorldClock(worldMinute);
            var scheduler = new SimulationScheduler(clock, Array.Empty<IWorldMinuteSystem>(), seed: 42);
            return SaveGameDto.Capture(state,
                scheduler,
                Array.Empty<ProceduralOverride>(),
                PhysicalSceneCheckpoint.Ship(EntityId.Parse("scene:ship:deck"), EntityId.Parse("anchor:ship:gangway")));
        }

        [Test]
        public void Commit_AtomicallyWritesPrimaryAndSafelyCreatesBackup()
        {
            var store = new LocalDiskSaveBlobStore(_testDir, "save_slot");
            var repo = new SaveRepository(store);

            var firstSnapshot = CreateSampleSnapshot(minute: 1);
            repo.Save(firstSnapshot);

            Assert.That(File.Exists(store.PrimaryPath), Is.True);
            var loadedFirst = repo.Load();
            Assert.That(loadedFirst.Success, Is.True);
            Assert.That(loadedFirst.UsedBackup, Is.False);
            Assert.That(loadedFirst.Value.State.Minute.Value, Is.EqualTo(1));

            var secondSnapshot = CreateSampleSnapshot(minute: 2);
            repo.Save(secondSnapshot);

            Assert.That(File.Exists(store.BackupPath), Is.True, "Previous valid save must be preserved to backup.");
            var loadedSecond = repo.Load();
            Assert.That(loadedSecond.Success, Is.True);
            Assert.That(loadedSecond.UsedBackup, Is.False);
            Assert.That(loadedSecond.Value.State.Minute.Value, Is.EqualTo(2));

            // Verify backup content matches first snapshot
            var backupDecoded = SaveCodec.TryDecode(store.ReadBackup());
            Assert.That(backupDecoded.Success, Is.True);
            Assert.That(backupDecoded.Value.State.Minute.Value, Is.EqualTo(1));
        }

        [Test]
        public void InterruptedWrite_CorruptedPrimary_RecoversFromBackupSafely()
        {
            var store = new LocalDiskSaveBlobStore(_testDir, "save_slot");
            var repo = new SaveRepository(store);

            var snap1 = CreateSampleSnapshot(minute: 1);
            repo.Save(snap1);

            var snap2 = CreateSampleSnapshot(minute: 2);
            repo.Save(snap2);

            // Simulate interrupted write or crash corrupting the primary save file
            var primaryBytes = File.ReadAllBytes(store.PrimaryPath);
            // Truncate to half and corrupt header
            var corrupt = new byte[primaryBytes.Length / 2];
            Array.Copy(primaryBytes, corrupt, corrupt.Length);
            corrupt[0] = 0xFF;
            File.WriteAllBytes(store.PrimaryPath, corrupt);

            // Repository load must detect failure in primary and seamlessly recover from backup
            var result = repo.Load();
            Assert.That(result.Success, Is.True, "Corrupted primary must trigger recovery from backup.");
            Assert.That(result.UsedBackup, Is.True, "UsedBackup must be true when primary is corrupted.");
            Assert.That(result.Value.State.Minute.Value, Is.EqualTo(1));
        }

        [Test]
        public void InterruptedWrite_SimulatedMidWriteTempFile_DoesNotAffectValidPrimaryOrBackup()
        {
            var store = new LocalDiskSaveBlobStore(_testDir, "save_slot");
            var repo = new SaveRepository(store);

            var snap1 = CreateSampleSnapshot(minute: 10);
            repo.Save(snap1);

            // Simulate an interrupted write that left a dirty partial temp file on disk
            string tempFile = Path.Combine(_testDir, "save_slot.tmp");
            File.WriteAllBytes(tempFile, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            // Store must read primary cleanly without temp interference
            var primary = store.ReadPrimary();
            Assert.That(primary.Length, Is.GreaterThan(0));
            var decoded = SaveCodec.TryDecode(primary);
            Assert.That(decoded.Success, Is.True);
            Assert.That(decoded.Value.State.Minute.Value, Is.EqualTo(10));

            // Next valid save must cleanly replace stale temp file and commit revision 11
            var snap2 = CreateSampleSnapshot(minute: 11);
            repo.Save(snap2);

            var loaded = repo.Load();
            Assert.That(loaded.Success, Is.True);
            Assert.That(loaded.Value.State.Minute.Value, Is.EqualTo(11));
        }
    }
}
