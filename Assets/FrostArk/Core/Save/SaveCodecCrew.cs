using System.IO;
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
    }
}
