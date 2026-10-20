using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Crew;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        /// <summary>
        /// v17 trailing extension. The slot catalog is authored state, and claims
        /// are runtime state. Unlike a job assignment, a claim expires and has
        /// exactly one valid owner. Empty registry costs only two int32 counts.
        /// </summary>
        private static void WriteCrewInteractionExtension(BinaryWriter writer,
            CrewInteractionState interactions)
        {
            WriteCount(writer, interactions.Slots.Count);
            foreach (var slot in interactions.Slots)
            {
                WriteId(writer, slot.Id);
                WriteId(writer, slot.SourceId);
                writer.Write((byte)slot.Verb);
                writer.Write(slot.LocalX);
                writer.Write(slot.LocalY);
                writer.Write(slot.Facing);
            }
            WriteCount(writer, interactions.Claims.Count);
            foreach (var claim in interactions.Claims)
            {
                WriteId(writer, claim.SlotId);
                WriteId(writer, claim.CrewId);
                writer.Write(claim.ExpiresAt.Value);
            }
        }

        private static CrewState ReadCrewInteractionExtension(BinaryReader reader,
            CrewState duty)
        {
            var slots = new List<CrewInteractionSlot>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var id = ReadId(reader);
                var source = ReadId(reader);
                byte verb = reader.ReadByte();
                int x = reader.ReadInt32();
                int y = reader.ReadInt32();
                int facing = reader.ReadInt32();
                slots.Add(new CrewInteractionSlot(id, source,
                    (CrewInteractionVerb)verb, x, y, facing));
            }
            var claims = new List<CrewInteractionClaim>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var slotId = ReadId(reader);
                var crewId = ReadId(reader);
                var expiresAt = new WorldMinute(reader.ReadInt64());
                claims.Add(new CrewInteractionClaim(slotId, crewId, expiresAt));
            }
            return duty.RestoreInteractions(CrewInteractionState.Create(slots, claims));
        }
    }
}
