using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Time;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Bounded, canonical, versioned binary save format. SHA-256 is an
    /// accidental-corruption check; it provides NO authenticity/security.
    /// No engine or host filesystem APIs are required by the codec.
    /// </summary>
    public static partial class SaveCodec
    {
        public const ushort CurrentVersion = 8;
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
                    WriteThermal(writer, state.Thermal);
                    WriteCrew(writer, state.CrewDuty);
                    WriteLayout(writer, state.Layout);
                    WriteNavigation(writer, state.Navigation);
                    WriteCreditedInventory(writer, state.Inventory);
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

                    if (version != 1 && version != 2 && version != 3 && version != 4 && version != 5 && version != 6 && version != 7 && version != CurrentVersion)
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
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException ||
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

            var inventory = version >= 3 ? ReadInventory(reader, version >= 8) : InventoryState.Empty;
            var thermal = version >= 4 ? ReadThermal(reader) : ShipThermalState.Empty;
            var crewDuty = version >= 5 ? ReadCrew(reader) : CrewState.Empty;
            var layout = version >= 6 ? ReadLayout(reader) : ShipLayoutState.Empty;
            var navigation = version >= 7 ? ReadNavigation(reader) : ShipNavigationState.Empty;
            if (version >= 8) inventory = ReadCreditedInventory(reader, inventory);
            var world = WorldState.RestoreSnapshot(minute, revision,
                supplies, crew, assignments, processed, inventory, thermal, crewDuty, layout, navigation);
            return SaveGameDto.Rehydrate(world, seed, pending, reserved, overrides);
        }


    }
}
