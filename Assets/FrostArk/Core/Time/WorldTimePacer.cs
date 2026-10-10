using System;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// Converts integer elapsed real milliseconds into discrete simulation
    /// minutes with a retained remainder. Different frame splits with the
    /// same accumulated milliseconds yield identical world-minute budgets.
    /// The caller advances SimulationScheduler using only the returned ticks.
    /// </summary>
    public sealed class WorldTimePacer
    {
        private readonly WorldTimeRate _rate;
        private long _fractionalNumerator;

        public WorldTimePacer(WorldTimeRate rate)
        {
            if (rate.RealMillisecondsPerCycle <= 0 || rate.WorldMinutesPerCycle < 0)
                throw new ArgumentException("A valid WorldTimeRate is required.", nameof(rate));
            _rate = rate;
        }

        public long ConsumeElapsedMilliseconds(long elapsedMilliseconds)
        {
            if (elapsedMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));

            // Compute and validate before modifying internal state. If the
            // elapsed duration overflows, the current remainder is preserved.
            long amount = checked(elapsedMilliseconds * _rate.WorldMinutesPerCycle);
            long numerator = checked(_fractionalNumerator + amount);
            long completeMinutes = numerator / _rate.RealMillisecondsPerCycle;
            _fractionalNumerator = numerator % _rate.RealMillisecondsPerCycle;
            return completeMinutes;
        }
    }
}