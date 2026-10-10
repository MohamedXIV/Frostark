namespace FrostArk.Core.Save
{
    public enum SaveDecodeError
    {
        None = 0,
        Missing = 1,
        Corrupt = 2,
        UnsupportedVersion = 3,
        InvalidData = 4
    }

    /// <summary>Explicit decode failure; a bad blob cannot replace live state.</summary>
    public sealed class SaveDecodeResult
    {
        public SaveGameDto Value { get; }
        public SaveDecodeError Error { get; }
        public bool Success => Error == SaveDecodeError.None;

        internal SaveDecodeResult(SaveGameDto value, SaveDecodeError error)
        {
            Value = value;
            Error = error;
        }

        internal static SaveDecodeResult Valid(SaveGameDto value) =>
            new SaveDecodeResult(value, SaveDecodeError.None);

        internal static SaveDecodeResult Failed(SaveDecodeError error) =>
            new SaveDecodeResult(null, error);
    }
}
