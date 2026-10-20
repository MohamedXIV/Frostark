using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Crew;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        // v16 follows v15 recovery: member hunger/thirst, then approved
        // authored consumable effects. Never trust values from the caller.
        private static void WriteCrewNeedsExtension(BinaryWriter writer, CrewState duty)
        {
            WriteCount(writer, duty.Members.Count);
            foreach (var member in duty.Members)
            {
                WriteId(writer, member.Id);
                writer.Write(member.Hunger);
                writer.Write(member.Thirst);
            }
            WriteCount(writer, duty.Consumables.Count);
            foreach (var food in duty.Consumables)
            {
                WriteId(writer, food.ItemId);
                writer.Write(food.Version);
                writer.Write(food.Nutrition);
                writer.Write(food.Hydration);
            }
        }

        private static CrewState ReadCrewNeedsExtension(BinaryReader reader, CrewState duty)
        {
            int count = ReadCount(reader);
            if (count != duty.Members.Count)
                throw new InvalidDataException("Needs count differs from roster.");
            for (int i = 0; i < count; i++)
            {
                var id = ReadId(reader);
                if (id != duty.Members[i].Id)
                    throw new InvalidDataException("Needs roster must be sorted and identical.");
                int hunger = reader.ReadInt32();
                int thirst = reader.ReadInt32();
                duty = duty.WithNeeds(id, hunger, thirst);
            }
            var consumables = new List<CrewConsumable>();
            int foodCount = ReadCount(reader);
            for (int i = 0; i < foodCount; i++)
                consumables.Add(new CrewConsumable(ReadId(reader),
                    reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            return duty.WithConsumables(consumables);
        }
    }
}
