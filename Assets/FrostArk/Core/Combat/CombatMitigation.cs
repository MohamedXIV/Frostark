using System;

namespace FrostArk.Core.Combat
{
    /// <summary>
    /// Pure C# domain logic for armor and defensive mitigation.
    /// Armor permille represents the fraction of incoming wound damage absorbed
    /// (0 = unarmored / full damage, 1000 = complete physical deflection).
    /// </summary>
    public static class CombatMitigation
    {
        public static int CalculateMitigatedWounds(int rawWoundPermille, int armorPermille)
        {
            if (rawWoundPermille <= 0) return 0;
            if (armorPermille <= 0) return rawWoundPermille;
            if (armorPermille >= 1000) return 0;

            int clampedArmor = Math.Min(1000, armorPermille);
            long remainingFraction = 1000 - clampedArmor;
            long mitigated = ((long)rawWoundPermille * remainingFraction) / 1000;
            return Math.Max(1, (int)mitigated);
        }
    }
}
