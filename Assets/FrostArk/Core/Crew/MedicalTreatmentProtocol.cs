using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Authored onboard treatment, never an amount submitted by a player.
    /// ItemId references a real ShipHold consumable; potency is capped.
    /// </summary>
    public sealed class MedicalTreatmentProtocol
    {
        public EntityId ItemId { get; }
        public int Version { get; }
        public int HealPermille { get; }
        public int MinimumMedicalSkill { get; }

        public MedicalTreatmentProtocol(EntityId itemId, int healPermille,
            int minimumMedicalSkill, int version = 1)
        {
            if (!itemId.IsValid)
                throw new ArgumentException("Medical item ID is required.", nameof(itemId));
            if (healPermille <= 0 || healPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(healPermille));
            if (minimumMedicalSkill < 0 || minimumMedicalSkill > 1000)
                throw new ArgumentOutOfRangeException(nameof(minimumMedicalSkill));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            ItemId = itemId;
            HealPermille = healPermille;
            MinimumMedicalSkill = minimumMedicalSkill;
            Version = version;
        }
    }
}
