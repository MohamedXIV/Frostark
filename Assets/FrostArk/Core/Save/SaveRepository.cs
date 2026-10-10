using System;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Platform implementation MUST atomically commit the new primary and its
    /// previous valid primary backup, or leave BOTH unchanged on failure.
    /// File/I/O APIs belong in a desktop/mobile/WebGL-specific adapter.
    /// Byte arrays returned by Read* must not expose mutable internal storage.
    /// </summary>
    public interface ISaveBlobStore
    {
        byte[] ReadPrimary();
        byte[] ReadBackup();
        void Commit(byte[] primary, byte[] previousValidPrimary);
    }

    public sealed class SaveLoadResult
    {
        public SaveGameDto Value { get; }
        public bool UsedBackup { get; }
        public bool Success => Value != null;
        public SaveDecodeError Error { get; }

        internal SaveLoadResult(SaveGameDto value, bool usedBackup, SaveDecodeError error)
        {
            Value = value;
            UsedBackup = usedBackup;
            Error = error;
        }
    }

    /// <summary>
    /// A codec/recovery facade. No implicit WorldState assignment, no fallback
    /// to a brand-new empty world when a save cannot be validated.
    /// </summary>
    public sealed class SaveRepository
    {
        private readonly ISaveBlobStore _store;

        public SaveRepository(ISaveBlobStore store) =>
            _store = store ?? throw new ArgumentNullException(nameof(store));

        public void Save(SaveGameDto snapshot)
        {
            // Validation and encoding happen before touching persistent storage.
            byte[] encoded = SaveCodec.Encode(snapshot);
            byte[] previous = _store.ReadPrimary();
            byte[] validPrevious = SaveCodec.TryDecode(previous).Success ? previous : null;
            _store.Commit(encoded, validPrevious);
        }

        public SaveLoadResult Load()
        {
            var primary = SaveCodec.TryDecode(_store.ReadPrimary());
            if (primary.Success)
                return new SaveLoadResult(primary.Value, false, SaveDecodeError.None);
            var backup = SaveCodec.TryDecode(_store.ReadBackup());
            if (backup.Success)
                return new SaveLoadResult(backup.Value, true, primary.Error);
            return new SaveLoadResult(null, false,
                primary.Error == SaveDecodeError.Missing ? backup.Error : primary.Error);
        }
    }
}
