using System;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// Immutable conversion ratio for presentation-side pacing.
    /// A zero numerator means paused; a zero denominator is always invalid.
    /// No wall clock, Unity deltaTime or float is stored in world state.
    /// </summary>
    public readonly struct WorldTimeRate
    {
        public long WorldMinutesPerCycle { get; }
        public long RealMillisecondsPerCycle { get; }

        public WorldTimeRate(long worldMinutesPerCycle, long realMillisecondsPerCycle)
        {
            if (worldMinutesPerCycle < 0)
                throw new ArgumentOutOfRangeException(nameof(worldMinutesPerCycle));
            if (realMillisecondsPerCycle <= 0)
                throw new ArgumentOutOfRangeException(nameof(realMillisecondsPerCycle));

            WorldMinutesPerCycle = worldMinutesPerCycle;
            RealMillisecondsPerCycle = realMillisecondsPerCycle;
        }
    }
}