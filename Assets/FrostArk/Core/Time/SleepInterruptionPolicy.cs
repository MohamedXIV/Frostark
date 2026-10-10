using System;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// Default sleep wake-up rule: committed scheduled hazards flagged as
    /// interrupts stop fast-forward at the exact minute they become due.
    /// </summary>
    public sealed class SleepInterruptionPolicy
    {
        public bool ShouldInterrupt(ScheduledWorldEvent worldEvent)
        {
            if (worldEvent == null)
                throw new ArgumentNullException(nameof(worldEvent));
            return worldEvent.InterruptsSleep;
        }
    }
}