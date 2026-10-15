using System;

namespace FrostArk.Core.Combat
{
    /// <summary>
    /// Pure C# domain logic for arctic cold exposure modifiers on combat.
    /// In severe cold without insulation, weapon mechanisms cycle slower,
    /// reloads take longer due to numb extremities, and movement is impeded.
    /// </summary>
    public static class ColdCombatModifiers
    {
        public static long ApplyCooldownModifier(long baseCooldownTicks, int coldExposurePermille)
        {
            if (baseCooldownTicks <= 0) return 0;
            if (coldExposurePermille <= 0) return baseCooldownTicks;
            int clamped = Math.Min(1000, coldExposurePermille);
            return baseCooldownTicks + (baseCooldownTicks * clamped / 1000);
        }

        public static long ApplyReloadModifier(long baseReloadTicks, int coldExposurePermille)
        {
            if (baseReloadTicks <= 0) return 0;
            if (coldExposurePermille <= 0) return baseReloadTicks;
            int clamped = Math.Min(1000, coldExposurePermille);
            return baseReloadTicks + (baseReloadTicks * clamped / 1000);
        }

        public static float CalculateSpeedMultiplier(int coldExposurePermille)
        {
            if (coldExposurePermille <= 0) return 1.0f;
            int clamped = Math.Min(1000, coldExposurePermille);
            return 1.0f - (0.35f * clamped / 1000f);
        }
    }
}
