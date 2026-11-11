using System;
using System.IO;
using FrostArk.Core.Save;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Atomic local disk storage adapter for SaveRepository. Writes a temporary
    /// file before atomically committing the primary and retaining the previous
    /// valid primary as a backup.
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
                try
                {
                    return File.Exists(_primaryPath)
                        ? File.ReadAllBytes(_primaryPath)
                        : Array.Empty<byte>();
                }
                catch
                {
                    return Array.Empty<byte>();
                }
            }
        }

        public byte[] ReadBackup()
        {
            lock (_lock)
            {
                try
                {
                    return File.Exists(_backupPath)
                        ? File.ReadAllBytes(_backupPath)
                        : Array.Empty<byte>();
                }
                catch
                {
                    return Array.Empty<byte>();
                }
            }
        }

        public void Commit(byte[] primary, byte[] previousValidPrimary)
        {
            if (primary == null) throw new ArgumentNullException(nameof(primary));
            lock (_lock)
            {
                if (File.Exists(_tempPath))
                {
                    try { File.Delete(_tempPath); } catch { }
                }

                File.WriteAllBytes(_tempPath, primary);

                if (previousValidPrimary != null && previousValidPrimary.Length > 0)
                {
                    string tempBackup = _backupPath + ".tmp";
                    if (File.Exists(tempBackup))
                    {
                        try { File.Delete(tempBackup); } catch { }
                    }
                    File.WriteAllBytes(tempBackup, previousValidPrimary);
                    if (File.Exists(_backupPath))
                    {
                        File.Replace(tempBackup, _backupPath, null,
                            ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempBackup, _backupPath);
                    }
                }
                else if (File.Exists(_primaryPath) && !File.Exists(_backupPath))
                {
                    string tempBackup = _backupPath + ".tmp";
                    if (File.Exists(tempBackup))
                    {
                        try { File.Delete(tempBackup); } catch { }
                    }
                    File.Copy(_primaryPath, tempBackup, overwrite: true);
                    if (File.Exists(_backupPath))
                    {
                        File.Replace(tempBackup, _backupPath, null,
                            ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempBackup, _backupPath);
                    }
                }

                if (File.Exists(_primaryPath))
                {
                    File.Replace(_tempPath, _primaryPath, null,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(_tempPath, _primaryPath);
                }
            }
        }
    }
}
