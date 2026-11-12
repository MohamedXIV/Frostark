using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Explicit authored/discovered survivor candidate backed by a valid world
    /// source ID. Prevents spontaneous arbitrary NPC generation and enforces
    /// exactly-once admission into the ship's crew roster.
    /// </summary>
    public sealed class SurvivorCandidate
    {
        public CrewIdentity Identity { get; }
        public EntityId Id => Identity.Id;
        public string DisplayName => Identity.DisplayName;
        public string LocalizationKey => Identity.LocalizationKey;
        public EntityId SourceId { get; }
        public DutyRole Role { get; }
        public int PrimarySkill { get; }
        public int BackupSkill { get; }
        public int ShiftStart { get; }
        public int ShiftMinutes { get; }
        public int Fatigue { get; }
        public int Wounds { get; }
        public int Hunger { get; }
        public int Thirst { get; }
        public bool IsRecruited { get; }

        public SurvivorCandidate(CrewIdentity identity, EntityId sourceId,
            DutyRole role, int primarySkill, int backupSkill,
            int shiftStart, int shiftMinutes,
            int fatigue = 0, int wounds = 0,
            int hunger = 0, int thirst = 0,
            bool isRecruited = false)
        {
            Identity = identity ?? throw new ArgumentNullException(nameof(identity));
            if (!sourceId.IsValid)
                throw new ArgumentException("Recruitment candidate requires a valid source ID.",
                    nameof(sourceId));
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
            if (hunger < 0 || hunger > 1000)
                throw new ArgumentOutOfRangeException(nameof(hunger));
            if (thirst < 0 || thirst > 1000)
                throw new ArgumentOutOfRangeException(nameof(thirst));

            SourceId = sourceId;
            Role = role;
            PrimarySkill = primarySkill;
            BackupSkill = backupSkill;
            ShiftStart = shiftStart;
            ShiftMinutes = shiftMinutes;
            Fatigue = fatigue;
            Wounds = wounds;
            Hunger = hunger;
            Thirst = thirst;
            IsRecruited = isRecruited;
        }

        public SurvivorCandidate(EntityId id, string displayName, EntityId sourceId,
            DutyRole role, int primarySkill, int backupSkill,
            int shiftStart, int shiftMinutes,
            int fatigue = 0, int wounds = 0,
            int hunger = 0, int thirst = 0,
            bool isRecruited = false, string localizationKey = "")
            : this(new CrewIdentity(id, displayName, localizationKey), sourceId,
                role, primarySkill, backupSkill, shiftStart, shiftMinutes,
                fatigue, wounds, hunger, thirst, isRecruited)
        {
        }

        public CrewMember ToCrewMember() =>
            new CrewMember(Identity, Role, PrimarySkill, BackupSkill,
                ShiftStart, ShiftMinutes, Fatigue, Wounds,
                isAway: false,
                isRecovering: Fatigue >= CrewMember.ExhaustionStartsAt,
                hunger: Hunger, thirst: Thirst);

        internal SurvivorCandidate MarkRecruited()
        {
            if (IsRecruited)
                throw new InvalidOperationException("Survivor candidate is already recruited.");
            return new SurvivorCandidate(Identity, SourceId, Role,
                PrimarySkill, BackupSkill, ShiftStart, ShiftMinutes,
                Fatigue, Wounds, Hunger, Thirst, isRecruited: true);
        }
    }
}
