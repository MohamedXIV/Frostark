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
