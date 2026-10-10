using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Time;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Bounded, canonical, versioned binary save format. SHA-256 is an
    /// accidental-corruption check; it provides NO authenticity/security.
    /// No engine or host filesystem APIs are required by the codec.
    /// </summary>
    public static class SaveCodec
    {
        public const ushort CurrentVersion = 3;
        private const int HeaderSize = 14;
        private const int ChecksumSize = 32;
        private const int MaxBlobBytes = 8 * 1024 * 1024;
        private const int MaxEntryCount = 16384;
        private const int MaxStringBytes = 4096;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FARKSAVE");
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(SaveGameDto snapshot)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            byte[] payload;
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Utf8, true))
                {
                    var state = snapshot.State;
                    writer.Write(state.Minute.Value);
                    writer.Write(state.Revision);
                    writer.Write(snapshot.Seed);

                    var supplies = state.SnapshotSupplies();
                    WriteCount(writer, supplies.Count);
                    foreach (var supply in supplies)
                    {
                        WriteId(writer, supply.Id);
                        writer.Write(supply.Quantity);
                    }

                    var crew = state.SnapshotCrew();
                    WriteCount(writer, crew.Count);
                    foreach (var id in crew) WriteId(writer, id);

                    var assignments = state.SnapshotAssignments();
                    WriteCount(writer, assignments.Count);
                    foreach (var pair in assignments)
                    {
                        WriteId(writer, pair.Key);
                        WriteId(writer, pair.Value);
                    }

                    var processed = state.SnapshotProcessedCommands();
                    WriteCount(writer, processed.Count);
                    foreach (var id in processed) WriteId(writer, id);

                    var pending = snapshot.SnapshotPending();
                    WriteCount(writer, pending.Count);
                    foreach (var item in pending)
                    {
                        WriteId(writer, item.Id);
                        writer.Write(item.DueAt.Value);
                        WriteText(writer, item.Kind);
                        writer.Write(item.InterruptsSleep ? (byte)1 : (byte)0);
                    }

                    var reserved = snapshot.SnapshotReserved();
                    WriteCount(writer, reserved.Count);
                    foreach (var id in reserved) WriteId(writer, id);

                    var overrides = snapshot.ProceduralOverrides;
                    WriteCount(writer, overrides.Count);
                    foreach (var item in overrides)
                    {
                        WriteId(writer, item.Id);
                        WriteText(writer, item.Value);
                    }

                    WriteInventory(writer, state.Inventory);
                }
                payload = stream.ToArray();
            }

            if ((long)payload.Length + HeaderSize + ChecksumSize > MaxBlobBytes)
                throw new ArgumentException("Save blob exceeds the supported size.", nameof(snapshot));

            using (var envelope = new MemoryStream())
            {
                using (var writer = new BinaryWriter(envelope, Utf8, true))
                {
                    writer.Write(Magic);
                    writer.Write(CurrentVersion);
                    writer.Write(payload.Length);
                    writer.Write(payload);
                    writer.Flush();
                }

                byte[] prefix = envelope.ToArray();
                byte[] checksum;
                using (var hash = SHA256.Create())
                    checksum = hash.ComputeHash(prefix);
                envelope.Write(checksum, 0, checksum.Length);
                return envelope.ToArray();
            }
        }

        public static SaveDecodeResult TryDecode(byte[] blob)
        {
            if (blob == null || blob.Length == 0)
                return SaveDecodeResult.Failed(SaveDecodeError.Missing);
            if (blob.Length < HeaderSize + ChecksumSize || blob.Length > MaxBlobBytes)
                return SaveDecodeResult.Failed(SaveDecodeError.Corrupt);

            try
            {
                using (var stream = new MemoryStream(blob, false))
                using (var reader = new BinaryReader(stream, Utf8, true))
                {
                    byte[] magic = reader.ReadBytes(Magic.Length);
                    if (!ByteArraysEqual(magic, Magic))
                        return SaveDecodeResult.Failed(SaveDecodeError.Corrupt);
                    ushort version = reader.ReadUInt16();
                    int length = reader.ReadInt32();
                    if (length < 0 || (long)length + HeaderSize + ChecksumSize != blob.Length)
                        return SaveDecodeResult.Failed(SaveDecodeError.Corrupt);

                    byte[] checksum;
                    using (var sha = SHA256.Create())
                        checksum = sha.ComputeHash(blob, 0, blob.Length - ChecksumSize);
                    int different = 0;
                    for (int i = 0; i < ChecksumSize; i++)
                        different |= checksum[i] ^ blob[blob.Length - ChecksumSize + i];
                    if (different != 0)
                        return SaveDecodeResult.Failed(SaveDecodeError.Corrupt);

                    if (version != 1 && version != 2 && version != CurrentVersion)
                        return SaveDecodeResult.Failed(SaveDecodeError.UnsupportedVersion);

                    using (var payloadStream = new MemoryStream(blob, HeaderSize, length, false))
                    using (var payloadReader = new BinaryReader(payloadStream, Utf8, true))
                    {
                        var result = ReadPayload(payloadReader, version);
                        if (payloadStream.Position != payloadStream.Length)
                            return SaveDecodeResult.Failed(SaveDecodeError.InvalidData);
                        return SaveDecodeResult.Valid(result);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException ||
                                        ex is OverflowException || ex is DecoderFallbackException)
            {
                return SaveDecodeResult.Failed(SaveDecodeError.InvalidData);
            }
        }

        private static SaveGameDto ReadPayload(BinaryReader reader, ushort version)
        {
            var minute = new WorldMinute(reader.ReadInt64());
            long revision = reader.ReadInt64();
            uint seed = reader.ReadUInt32();

            var supplies = new List<SupplyStock>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                supplies.Add(new SupplyStock(ReadId(reader), reader.ReadInt64()));

            var crew = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                crew.Add(ReadId(reader));

            var assignments = new List<KeyValuePair<EntityId, EntityId>>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                assignments.Add(new KeyValuePair<EntityId, EntityId>(ReadId(reader), ReadId(reader)));

            var processed = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                processed.Add(ReadId(reader));

            var pending = new List<ScheduledWorldEvent>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                EntityId id = ReadId(reader);
                var due = new WorldMinute(reader.ReadInt64());
                string kind = ReadText(reader);
                byte interrupts = reader.ReadByte();
                if (interrupts > 1)
                    throw new InvalidDataException("Invalid hazard interruption flag.");
                pending.Add(new ScheduledWorldEvent(id, due, kind, interrupts == 1));
            }

            var reserved = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                reserved.Add(ReadId(reader));

            var overrides = new List<ProceduralOverride>();
            // Version 1 is the same schema minus the final override map.
            // Version 2 migration must NOT fabricate procedural changes.
            if (version >= 2)
                for (int i = 0, count = ReadCount(reader); i < count; i++)
                    overrides.Add(new ProceduralOverride(ReadId(reader), ReadText(reader)));

            var inventory = version >= 3 ? ReadInventory(reader) : InventoryState.Empty;
            var world = WorldState.RestoreSnapshot(minute, revision,
                supplies, crew, assignments, processed, inventory);
            return SaveGameDto.Rehydrate(world, seed, pending, reserved, overrides);
        }


        private static void WriteInventory(BinaryWriter writer, InventoryState inventory)
        {
            writer.Write(inventory.PackMaxWeightGrams);
            writer.Write(inventory.PackMaxSlots);
            writer.Write(inventory.ShipMaxWeightGrams);
            writer.Write(inventory.ShipMaxSlots);

            var definitions = inventory.SnapshotDefinitions();
            WriteCount(writer, definitions.Count);
            foreach (var item in definitions)
            {
                WriteId(writer, item.Id);
                writer.Write(item.ContentVersion);
                writer.Write(item.UnitWeightGrams);
                writer.Write(item.MaxStackSize);
            }

            var pickups = inventory.SnapshotPickups();
            WriteCount(writer, pickups.Count);
            foreach (var pickup in pickups)
            {
                WriteId(writer, pickup.Id);
                WriteId(writer, pickup.ItemId);
                writer.Write(pickup.Quantity);
            }

            var collected = inventory.SnapshotCollected();
            WriteCount(writer, collected.Count);
            foreach (var id in collected) WriteId(writer, id);

            foreach (var location in new[] { InventoryContainer.Pack, InventoryContainer.ShipHold })
            {
                var stock = inventory.SnapshotStock(location);
                WriteCount(writer, stock.Count);
                foreach (var stack in stock)
                {
                    WriteId(writer, stack.Key);
                    writer.Write(stack.Value);
                }
            }
        }

        private static InventoryState ReadInventory(BinaryReader reader)
        {
            long packWeight = reader.ReadInt64();
            int packSlots = reader.ReadInt32();
            long shipWeight = reader.ReadInt64();
            int shipSlots = reader.ReadInt32();

            var definitions = new List<ItemDefinition>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                definitions.Add(new ItemDefinition(ReadId(reader), reader.ReadInt32(),
                    reader.ReadInt64(), reader.ReadInt32()));
            var catalog = new ItemCatalog(definitions);

            var pickups = new List<SalvagePickup>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                pickups.Add(new SalvagePickup(ReadId(reader), ReadId(reader), reader.ReadInt64()));

            var collected = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                collected.Add(ReadId(reader));

            List<KeyValuePair<EntityId, long>> ReadStock()
            {
                var values = new List<KeyValuePair<EntityId, long>>();
                for (int i = 0, count = ReadCount(reader); i < count; i++)
                    values.Add(new KeyValuePair<EntityId, long>(ReadId(reader), reader.ReadInt64()));
                return values;
            }

            var pack = ReadStock();
            var ship = ReadStock();
            return InventoryState.RestoreSnapshot(catalog, pickups, collected, pack, ship,
                packWeight, packSlots, shipWeight, shipSlots);
        }

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
