using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Ship;
namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteLayout(BinaryWriter writer, ShipLayoutState layout)
        {
            WriteCount(writer, layout.Rooms.Count);
            foreach (var room in layout.Rooms)
            { WriteId(writer, room.Id); writer.Write(room.Width); writer.Write(room.Height); }
            WriteCount(writer, layout.Blueprints.Count);
            foreach (var blueprint in layout.Blueprints)
            {
                WriteId(writer, blueprint.Id); writer.Write((byte)blueprint.Kind);
                writer.Write(blueprint.Width); writer.Write(blueprint.Height);
                WriteId(writer, blueprint.PartId); writer.Write(blueprint.BuildCost);
                writer.Write(blueprint.RepairCost);
            }
            WriteCount(writer, layout.Modules.Count);
            foreach (var module in layout.Modules)
            {
                WriteId(writer, module.Id); WriteId(writer, module.BlueprintId);
                WriteId(writer, module.RoomId); writer.Write(module.X); writer.Write(module.Y);
                writer.Write(module.IntegrityPermille);
                writer.Write(module.RepairReserved ? (byte)1 : (byte)0);
            }
        }

        private static ShipLayoutState ReadLayout(BinaryReader reader)
        {
            var rooms = new List<ShipCompartmentSlot>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                rooms.Add(new ShipCompartmentSlot(ReadId(reader), reader.ReadInt32(), reader.ReadInt32()));
            var blueprints = new List<ShipModuleBlueprint>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                blueprints.Add(new ShipModuleBlueprint(ReadId(reader), (ShipModuleKind)reader.ReadByte(),
                    reader.ReadInt32(), reader.ReadInt32(), ReadId(reader),
                    reader.ReadInt64(), reader.ReadInt64()));
            var modules = new List<ShipModule>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var id = ReadId(reader); var type = ReadId(reader); var room = ReadId(reader);
                int x = reader.ReadInt32(); int y = reader.ReadInt32();
                int integrity = reader.ReadInt32();
                byte reserved = reader.ReadByte();
                if (reserved > 1) throw new InvalidDataException("Invalid module repair reservation flag.");
                modules.Add(new ShipModule(id, type, room, x, y, integrity, reserved == 1));
            }
            return ShipLayoutState.Create(rooms, blueprints, modules);
        }
    }
}