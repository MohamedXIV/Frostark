using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Economy
{
    /// <summary>Integer-rated authored equipment and environmental gates.</summary>
    public sealed class GearProtection
    {
        public EntityId GearId { get; }
        public int ColdProtection { get; }
        public int ContaminationProtection { get; }

        public GearProtection(EntityId gearId, int coldProtection, int contaminationProtection)
        {
            if (!gearId.IsValid)
                throw new ArgumentException("Gear requires stable identity.", nameof(gearId));
            if (coldProtection < 0 || coldProtection > 1000)
                throw new ArgumentOutOfRangeException(nameof(coldProtection));
            if (contaminationProtection < 0 || contaminationProtection > 1000)
                throw new ArgumentOutOfRangeException(nameof(contaminationProtection));
            GearId = gearId;
            ColdProtection = coldProtection;
            ContaminationProtection = contaminationProtection;
        }
    }

    public sealed class EnvironmentalHazard
    {
        public EntityId ZoneId { get; }
        public int RequiredColdProtection { get; }
        public int RequiredContaminationProtection { get; }

        public EnvironmentalHazard(EntityId zoneId, int requiredColdProtection,
            int requiredContaminationProtection)
        {
            if (!zoneId.IsValid) throw new ArgumentException("Zone requires stable identity.", nameof(zoneId));
            if (requiredColdProtection < 0 || requiredColdProtection > 1000)
                throw new ArgumentOutOfRangeException(nameof(requiredColdProtection));
            if (requiredContaminationProtection < 0 || requiredContaminationProtection > 1000)
                throw new ArgumentOutOfRangeException(nameof(requiredContaminationProtection));
            ZoneId = zoneId;
            RequiredColdProtection = requiredColdProtection;
            RequiredContaminationProtection = requiredContaminationProtection;
        }
    }

    public static class GearAccess
    {
        public static bool CanEnter(GearProtection gear, EnvironmentalHazard hazard)
        {
            if (hazard == null) throw new ArgumentNullException(nameof(hazard));
            return (gear?.ColdProtection ?? 0) >= hazard.RequiredColdProtection &&
                   (gear?.ContaminationProtection ?? 0) >= hazard.RequiredContaminationProtection;
        }
    }
}
