using System;
using System.IO;
using FrostArk.Core.Save;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Atomic, thread-safe local disk storage adapter for SaveRepository.
    /// Writes to a temporary file before atomically committing to primary
    /// and retaining the previous valid primary as a backup file.
    /// </summary>
    public sealed class LocalDiskSaveBlobStore : ISaveBlobStore
    {
        private readonly string _primaryPath;
        private readonly string _backupPath;
        private readonly string _tempPath;
        private readonly object _lock = new object();

        public string PrimaryPath => _primaryPath;
        public string BackupPath => _backupPath;

        public LocalDiskSaveBlobStore(string directoryPath = null, string slotName = "slot_0")
        {
            string dir = string.IsNullOrEmpty(directoryPath)
                ? Path.Combine(Application.persistentDataPath, "Saves")
                : directoryPath;
            Directory.CreateDirectory(dir);

            _primaryPath = Path.Combine(dir, slotName + ".sav");
            _backupPath = Path.Combine(dir, slotName + ".bak");
            _tempPath = Path.Combine(dir, slotName + ".tmp");
        }

        public byte[] ReadPrimary()
        {
            lock (_lock)
            {
                return File.Exists(_primaryPath)
                    ? File.ReadAllBytes(_primaryPath)
                    : Array.Empty<byte>();
            }
        }

        public byte[] ReadBackup()
        {
            lock (_lock)
            {
                return File.Exists(_backupPath)
                    ? File.ReadAllBytes(_backupPath)
                    : Array.Empty<byte>();
            }
        }

        public void Commit(byte[] primary, byte[] previousValidPrimary)
        {
            if (primary == null) throw new ArgumentNullException(nameof(primary));
            lock (_lock)
            {
                // Write new content to temp file first
                File.WriteAllBytes(_tempPath, primary);

                // If a previous valid primary exists, preserve it to backup
                if (previousValidPrimary != null && previousValidPrimary.Length > 0)
                {
                    File.WriteAllBytes(_backupPath, previousValidPrimary);
                }
                else if (File.Exists(_primaryPath))
                {
                    File.Copy(_primaryPath, _backupPath, overwrite: true);
                }

                // Atomically replace primary file
                if (File.Exists(_primaryPath))
                {
                    File.Delete(_primaryPath);
                }
                File.Move(_tempPath, _primaryPath);
            }
        }
    }
}
