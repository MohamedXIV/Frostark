using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Crew;
using FrostArk.Core.World;

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
            int memberCount = ReadCount(reader);
            if (memberCount > CrewState.MaxCrewCapacity)
                throw new InvalidDataException(
                    $"Crew duty roster count {memberCount} exceeds maximum supported capacity of {CrewState.MaxCrewCapacity}.");
            var members = new List<CrewMember>(memberCount);
            for (int i = 0; i < memberCount; i++)
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

        private static bool HasExplicitCrewIdentitiesOrCandidates(WorldState state) =>
            state.CrewIdentities.Any(x => !x.IsDefaultDerived) ||
            state.CrewDuty.Candidates.Count > 0;

        // v20 optional extension: custom crew display names/localization keys
        // and authored survivor recruitment candidates.
        private static void WriteCrewIdentityAndCandidatesExtension(
            BinaryWriter writer, WorldState state)
        {
            var customIdentities = state.CrewIdentities
                .Where(x => !x.IsDefaultDerived).ToArray();
            WriteCount(writer, customIdentities.Length);
            foreach (var identity in customIdentities)
            {
                WriteId(writer, identity.Id);
                WriteText(writer, identity.DisplayName);
                WriteText(writer, identity.LocalizationKey);
            }

            var candidates = state.CrewDuty.Candidates;
            WriteCount(writer, candidates.Count);
            foreach (var candidate in candidates)
            {
                WriteId(writer, candidate.Id);
                WriteText(writer, candidate.Identity.DisplayName);
                WriteText(writer, candidate.Identity.LocalizationKey);
                WriteId(writer, candidate.SourceId);
                writer.Write((byte)candidate.Role);
                writer.Write(candidate.PrimarySkill);
                writer.Write(candidate.BackupSkill);
                writer.Write(candidate.ShiftStart);
                writer.Write(candidate.ShiftMinutes);
                writer.Write(candidate.Fatigue);
                writer.Write(candidate.Wounds);
                writer.Write(candidate.Hunger);
                writer.Write(candidate.Thirst);
                writer.Write(candidate.IsRecruited ? (byte)1 : (byte)0);
            }
        }

        private static CrewState ReadCrewIdentityAndCandidatesExtension(
            BinaryReader reader, IReadOnlyList<EntityId> worldCrew,
            CrewState duty, out IReadOnlyList<CrewIdentity> identities)
        {
            int identityCount = ReadCount(reader);
            if (identityCount > WorldState.MaxCrewCapacity)
                throw new InvalidDataException(
                    $"Custom crew identity count {identityCount} exceeds maximum supported capacity of {WorldState.MaxCrewCapacity}.");
            var customIdentities = new List<CrewIdentity>(identityCount);
            var worldSet = new HashSet<string>(worldCrew.Select(x => x.Value),
                StringComparer.Ordinal);
            string? previousIdentityId = null;

            for (int i = 0; i < identityCount; i++)
            {
                var id = ReadId(reader);
                if (previousIdentityId != null &&
                    StringComparer.Ordinal.Compare(previousIdentityId, id.Value) >= 0)
                    throw new InvalidDataException("Crew identities must be strictly sorted and unique.");
                previousIdentityId = id.Value;
                if (!worldSet.Contains(id.Value))
                    throw new InvalidDataException("Crew identity references unknown crew member.");

                string displayName = ReadText(reader);
                string localizationKey = ReadText(reader);
                var identity = new CrewIdentity(id, displayName, localizationKey);
                if (identity.IsDefaultDerived)
                    throw new InvalidDataException("Default-derived crew identity must not be stored in custom extension.");

                customIdentities.Add(identity);
                if (duty.Get(id) != null)
                    duty = duty.WithMemberIdentity(id, identity);
            }

            int candidateCount = ReadCount(reader);
            if (identityCount == 0 && candidateCount == 0)
                throw new InvalidDataException("Empty v20 crew identity/candidate extension is not canonical.");

            var candidates = new List<SurvivorCandidate>(candidateCount);
            string? previousCandidateId = null;
            for (int i = 0; i < candidateCount; i++)
            {
                var candidateId = ReadId(reader);
                if (previousCandidateId != null &&
                    StringComparer.Ordinal.Compare(previousCandidateId, candidateId.Value) >= 0)
                    throw new InvalidDataException("Survivor candidates must be strictly sorted and unique.");
                previousCandidateId = candidateId.Value;

                string displayName = ReadText(reader);
                string localizationKey = ReadText(reader);
                var sourceId = ReadId(reader);
                var role = (DutyRole)reader.ReadByte();
                int primary = reader.ReadInt32();
                int backup = reader.ReadInt32();
                int shiftStart = reader.ReadInt32();
                int shiftMinutes = reader.ReadInt32();
                int fatigue = reader.ReadInt32();
                int wounds = reader.ReadInt32();
                int hunger = reader.ReadInt32();
                int thirst = reader.ReadInt32();
                byte recruited = reader.ReadByte();
                if (recruited > 1)
                    throw new InvalidDataException("Invalid survivor candidate recruited flag.");

                candidates.Add(new SurvivorCandidate(
                    new CrewIdentity(candidateId, displayName, localizationKey),
                    sourceId, role, primary, backup, shiftStart, shiftMinutes,
                    fatigue, wounds, hunger, thirst, recruited == 1));
            }

            identities = customIdentities;
            return duty.WithCandidates(candidates);
        }
    }
}
