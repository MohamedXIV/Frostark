using System;
using System.IO;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteCount(BinaryWriter writer, int count)
        {
            if (count < 0 || count > MaxEntryCount)
                throw new ArgumentOutOfRangeException(nameof(count));
            writer.Write(count);
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > MaxEntryCount)
                throw new InvalidDataException("Unreasonable collection length.");
            return count;
        }

        private static void WriteId(BinaryWriter writer, EntityId id)
        {
            if (!id.IsValid)
                throw new ArgumentException("A save identity cannot be empty.");
            WriteText(writer, id.Value);
        }

        private static EntityId ReadId(BinaryReader reader) => EntityId.Parse(ReadText(reader));

        private static void WriteText(BinaryWriter writer, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            byte[] utf8 = Utf8.GetBytes(value);
            if (utf8.Length > MaxStringBytes)
                throw new ArgumentException("Save text is too long.");
            writer.Write(utf8.Length);
            writer.Write(utf8);
        }

        private static string ReadText(BinaryReader reader)
        {
            int bytes = reader.ReadInt32();
            if (bytes < 0 || bytes > MaxStringBytes)
                throw new InvalidDataException("Unreasonable UTF-8 string size.");
            byte[] utf8 = reader.ReadBytes(bytes);
            if (utf8.Length != bytes)
                throw new EndOfStreamException("Truncated UTF-8 string.");
            return Utf8.GetString(utf8);
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++)
                difference |= left[i] ^ right[i];
            return difference == 0;
        }
    }
}
