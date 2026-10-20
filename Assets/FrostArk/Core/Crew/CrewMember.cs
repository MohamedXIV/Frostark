using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    public enum DutyRole
    {
        Engineering = 0,
        Watch = 1,
        Cooking = 2,
        Medical = 3,
        Repair = 4
    }

    /// <summary>Immutable authored skills and persisted condition/shift snapshot.</summary>
    public sealed class CrewMember
    {
        public EntityId Id { get; }
        public DutyRole Role { get; }
        public int PrimarySkill { get; }
        public int BackupSkill { get; }
        public int ShiftStart { get; }
        public int ShiftMinutes { get; }
        public int Fatigue { get; }
        public int Wounds { get; }
        public bool IsAway { get; }
        // Fatigue is the canonical need; this flag records recovery hysteresis.
        // Both survive save/resume. It is not a second independent Needs clock.
        public bool IsRecovering { get; }

        // Engineering v1 tuning; revisit once complete crew Needs are playable.
        public const int ExhaustionStartsAt = 900;
        public const int RecoveryEndsAt = 400;

        public CrewMember(EntityId id, DutyRole role, int primarySkill, int backupSkill,
            int shiftStart, int shiftMinutes, int fatigue = 0, int wounds = 0,
            bool isAway = false, bool isRecovering = false)
        {
            if (!id.IsValid) throw new ArgumentException("Persistent crew ID required.", nameof(id));
            if (!Enum.IsDefined(typeof(DutyRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            if (primarySkill < 0 || primarySkill > 1000)
                throw new ArgumentOutOfRangeException(nameof(primarySkill));
            if (backupSkill < 0 || backupSkill > 1000)
                throw new ArgumentOutOfRangeException(nameof(backupSkill));
            if (shiftStart < 0 || shiftStart >= 1440)
                throw new ArgumentOutOfRangeException(nameof(shiftStart));
            if (shiftMinutes <= 0 || shiftMinutes > 1440)
                throw new ArgumentOutOfRangeException(nameof(shiftMinutes));
            if (fatigue < 0 || fatigue > 1000)
                throw new ArgumentOutOfRangeException(nameof(fatigue));
            if (wounds < 0 || wounds > 1000)
                throw new ArgumentOutOfRangeException(nameof(wounds));
            Id = id; Role = role; PrimarySkill = primarySkill; BackupSkill = backupSkill;
            ShiftStart = shiftStart; ShiftMinutes = shiftMinutes; Fatigue = fatigue;
            Wounds = wounds; IsAway = isAway;
            // New/legacy profiles that arrive already exhausted must recover.
            // Between the limits, preserve the prior state to avoid one-minute
            // work/rest oscillation at an exact threshold.
            IsRecovering = fatigue >= ExhaustionStartsAt ||
                (isRecovering && fatigue > RecoveryEndsAt);
        }

        public bool OnShift(WorldMinute minute) =>
            ((minute.Value % 1440 - ShiftStart + 1440) % 1440) < ShiftMinutes;

        public int SkillFor(DutyRole role)
        {
            int baseSkill = role == Role ? PrimarySkill : BackupSkill;
            if (IsAway || IsRecovering || baseSkill == 0 || Wounds == 1000) return 0;
            return (int)((long)baseSkill * (1000 - Fatigue / 2) *
                (1000 - Wounds) / 1_000_000);
        }

        public CrewMember WithAway(bool away) => away == IsAway ? this :
            new CrewMember(Id, Role, PrimarySkill, BackupSkill, ShiftStart,
                ShiftMinutes, Fatigue, Wounds, away, IsRecovering);

        public CrewMember WithFatigue(int fatigue) => fatigue == Fatigue ? this :
            new CrewMember(Id, Role, PrimarySkill, BackupSkill, ShiftStart,
                ShiftMinutes, fatigue, Wounds, IsAway, IsRecovering);

        public CrewMember WithWounds(int wounds) => wounds == Wounds ? this :
            new CrewMember(Id, Role, PrimarySkill, BackupSkill, ShiftStart,
                ShiftMinutes, Fatigue, wounds, IsAway, IsRecovering);

        internal CrewMember WithRecovering(bool recovering)
        {
            if (recovering == IsRecovering) return this;
            var next = new CrewMember(Id, Role, PrimarySkill, BackupSkill,
                ShiftStart, ShiftMinutes, Fatigue, Wounds, IsAway, recovering);
            if (next.IsRecovering != recovering)
                throw new ArgumentException("Impossible recovery state for current fatigue.",
                    nameof(recovering));
            return next;
        }
    }

    public sealed class RepairJob
    {
        public EntityId Id { get; }
        public long RemainingWork { get; }
        public RepairJob(EntityId id, long remainingWork)
        {
            if (!id.IsValid) throw new ArgumentException("Repair ID required.", nameof(id));
            if (remainingWork <= 0) throw new ArgumentOutOfRangeException(nameof(remainingWork));
            Id = id;
            RemainingWork = remainingWork;
        }
    }
}
