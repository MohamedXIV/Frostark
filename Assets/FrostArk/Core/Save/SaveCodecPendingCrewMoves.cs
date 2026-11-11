using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        // V18 extension: actor, scene, typed ship target. No mouse selection,
        // interpolated frame coordinates, cached paths or traffic reservations.
        private static void WritePendingCrewMoves(BinaryWriter writer,
            IReadOnlyList<PendingCrewMove> moves)
        {
            WriteCount(writer, moves.Count);
            foreach (var move in moves)
            {
                if (move.Theater != PhysicalTheater.Ship)
                    throw new InvalidDataException("Unsupported persisted movement theater.");
                WriteId(writer, move.CrewId);
                WriteId(writer, move.SceneId);
                writer.Write((byte)move.Theater);
                WriteId(writer, move.ShipGoal.RoomId);
                writer.Write(move.ShipGoal.X);
                writer.Write(move.ShipGoal.Y);
            }
        }

        private static PendingCrewMove[] ReadPendingCrewMoves(BinaryReader reader)
        {
            var items = new List<PendingCrewMove>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var crew = ReadId(reader);
                var scene = ReadId(reader);
                var theater = reader.ReadByte();
                if (theater != (byte)PhysicalTheater.Ship)
                    throw new InvalidDataException("Unknown pending movement theater.");
                var goal = new ShipGridCell(ReadId(reader),
                    reader.ReadInt32(), reader.ReadInt32());
                items.Add(new PendingCrewMove(crew, scene, goal));
            }
            return items.ToArray();
        }
    }
}
