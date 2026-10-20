using System.IO;
using System.Collections.Generic;
using FrostArk.Core.Crew;
namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteCrew(BinaryWriter writer, CrewState duty)
        {
            WriteCount(writer, duty.Members.Count);
            foreach (var member in duty.Members)
            {
                WriteId(writer, member.Id);
                writer.Write((byte)member.Role);
                writer.Write(member.PrimarySkill);
                writer.Write(member.BackupSkill);
                writer.Write(member.ShiftStart);
                writer.Write(member.ShiftMinutes);
                writer.Write(member.Fatigue);
                writer.Write(member.Wounds);
                writer.Write(member.IsAway ? (byte)1 : (byte)0);
            }
            WriteCount(writer, duty.RepairJobs.Count);
            foreach (var job in duty.RepairJobs)
            {
                WriteId(writer, job.Id);
                writer.Write(job.RemainingWork);
            }
        }
        // v15 trailing extension: maintains the fatigue recovery latch across
        // reload without disturbing the layout of v5-v14 crew records.
        private static void WriteCrewRecoveryExtension(BinaryWriter writer, CrewState duty)
        {
            WriteCount(writer, duty.Members.Count);
            foreach (var member in duty.Members)
            {
                WriteId(writer, member.Id);
                writer.Write(member.IsRecovering ? (byte)1 : (byte)0);
            }
        }

        private static CrewState ReadCrewRecoveryExtension(BinaryReader reader, CrewState duty)
        {
            int count = ReadCount(reader);
            if (count != duty.Members.Count)
                throw new InvalidDataException("Crew recovery count does not match roster.");
            for (int i = 0; i < count; i++)
            {
                var id = ReadId(reader);
                if (id != duty.Members[i].Id)
                    throw new InvalidDataException("Crew recovery IDs must match sorted roster.");
                byte flag = reader.ReadByte();
                if (flag > 1)
                    throw new InvalidDataException("Invalid crew recovery flag.");
                try
                {
                    duty = duty.WithRecovering(id, flag == 1);
                }
                catch (System.ArgumentException ex)
                {
                    throw new InvalidDataException("Recovery flag conflicts with fatigue.", ex);
                }
            }
            return duty;
        }

        private static CrewState ReadCrew(BinaryReader reader)
        {
            var members = new List<CrewMember>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var id = ReadId(reader);
                var role = (DutyRole)reader.ReadByte();
                int primary = reader.ReadInt32();
                int backup = reader.ReadInt32();
                int shiftStart = reader.ReadInt32();
                int shiftMinutes = reader.ReadInt32();
                int fatigue = reader.ReadInt32();
                int wounds = reader.ReadInt32();
                byte away = reader.ReadByte();
                if (away > 1)
                    throw new InvalidDataException("Invalid crew absence flag.");
                members.Add(new CrewMember(id, role, primary, backup, shiftStart,
                    shiftMinutes, fatigue, wounds, away == 1));
            }
            var jobs = new List<RepairJob>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                jobs.Add(new RepairJob(ReadId(reader), reader.ReadInt64()));
            return CrewState.Create(members, jobs);
        }
    }
}
