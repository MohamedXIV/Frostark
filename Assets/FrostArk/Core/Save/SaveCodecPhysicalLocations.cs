using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        // V19 extension: derived physical actor positions. Ship actors use a
        // canonical grid cell; shore actors use finite world X/Z coordinates.
        private static void WritePhysicalActorLocations(BinaryWriter writer,
            IReadOnlyList<PhysicalActorLocation> locations)
        {
            WriteCount(writer, locations.Count);
            foreach (var location in locations)
            {
                WriteId(writer, location.CrewId);
                writer.Write((byte)location.Theater);
                if (location.Theater == PhysicalTheater.Ship)
                {
                    WriteId(writer, location.ShipCell.RoomId);
                    writer.Write(location.ShipCell.X);
                    writer.Write(location.ShipCell.Y);
                }
                else if (location.Theater == PhysicalTheater.Shore)
                {
                    writer.Write(location.ShoreX);
                    writer.Write(location.ShoreZ);
                }
                else
                    throw new InvalidDataException("Unsupported actor location theater.");
            }
        }

        private static PhysicalActorLocation[] ReadPhysicalActorLocations(
            BinaryReader reader)
        {
            var items = new List<PhysicalActorLocation>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var crew = ReadId(reader);
                var theater = reader.ReadByte();
                if (theater == (byte)PhysicalTheater.Ship)
                    items.Add(PhysicalActorLocation.Aboard(crew,
                        new ShipGridCell(ReadId(reader),
                            reader.ReadInt32(), reader.ReadInt32())));
                else if (theater == (byte)PhysicalTheater.Shore)
                    items.Add(PhysicalActorLocation.Ashore(crew,
                        reader.ReadSingle(), reader.ReadSingle()));
                else
                    throw new InvalidDataException("Unknown actor location theater.");
            }
            return items.ToArray();
        }
    }
}
