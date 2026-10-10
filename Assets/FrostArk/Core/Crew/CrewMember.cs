using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    public enum DutyRole
    {
        Engineering = 0,
        Repair = 1,
        Watch = 2,
        Cooking = 3,
        Medical = 4
    }

    public sealed class CrewMember
    {
        public EntityId Id { get; }
        public DutyRole Specialty { get; }
        public int PrimarySkill { get; }
        public int BackupSkill { get; }
        public int ShiftStart { get; }
        public int ShiftMinutes { get; }
        public int Fatigue { get; }
        public int Wounds { get; }
        public bool IsAway { get; }

        public CrewMember(EntityId id, DutyRole specialty, int primarySkill,
            int backupSkill, int shiftStart, int shiftMinutes,
            int fatigue = 0, int wounds = 0, bool away = false)
        {
            if (!id.IsValid) throw new ArgumentException("Crew needs an ID.", nameof(id));
            if (!Enum.IsDefined(typeof(DutyRole), specialty))
                throw new ArgumentOutOfRangeException(nameof(specialty));
            if (primarySkill < 0 || primarySkill > 1000)
                throw new ArgumentOutOfRangeException(nameof(primarySkill));
            if (backupSkill < 0 || backupSkill > 1000)
                throw new ArgumentOutOfRangeException(nameof(backupSkill));
            if (shiftStart < 0 || shiftStart >= 1440)
                throw new ArgumentOutOfRangeException(nameof(shiftStart));
            if (shiftMinutes < 1 || shiftMinutes > 1440)
                throw new ArgumentOutOfRangeException(nameof(shiftMinutes));
            if (fatigue < 0 || fatigue > 1000)
                throw new ArgumentOutOfRangeException(nameof(fatigue));
            if (wounds < 0 || wounds > 1000)
                throw new ArgumentOutOfRangeException(nameof(wounds));

            Id = id;
            Specialty = specialty;
            PrimarySkill = primarySkill;
            BackupSkill = backupSkill;
            ShiftStart = shiftStart;
            ShiftMinutes = shiftMinutes;
            Fatigue = fatigue;
            Wounds = wounds;
            IsAway = away;
        }

        public bool IsOnShift(WorldMinute minute)
        {
            long day = minute.Value % 1440;
            int offset = (int)((day - ShiftStart + 1440) % 1440);
            return offset < ShiftMinutes;
        }

        public int EffectiveSkill(DutyRole role, WorldMinute minute)
        {
            if (IsAway || !IsOnShift(minute) || Wounds == 1000)
                return 0;
            long baseSkill = role == Specialty ? PrimarySkill : BackupSkill;
            return (int)(baseSkill * (1000L - Fatigue / 2) *
                (1000L - Wounds) / 1_000_000L);
        }

        internal CrewMember WithAway(bool away) =>
            new CrewMember(Id, Specialty, PrimarySkill, BackupSkill,
                ShiftStart, ShiftMinutes, Fatigue, Wounds, away);

        internal CrewMember WithFatigue(int fatigue) =>
            new CrewMember(Id, Specialty, PrimarySkill, BackupSkill,
                ShiftStart, ShiftMinutes, fatigue, Wounds, IsAway);
    }

    public sealed class RepairJob
    {
        public EntityId Id { get; }
        public int RemainingWork { get; }

        public RepairJob(EntityId id, int remainingWork)
        {
            if (!id.IsValid) throw new ArgumentException("Repair job needs ID.", nameof(id));
            if (remainingWork < 0) throw new ArgumentOutOfRangeException(nameof(remainingWork));
            Id = id;
            RemainingWork = remainingWork;
        }

        internal RepairJob WithWork(int work) => new RepairJob(Id, work);
    }
}
