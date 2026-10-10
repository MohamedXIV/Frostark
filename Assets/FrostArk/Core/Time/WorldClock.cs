using FrostArk.Core.World;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// One authoritative integer-minute clock per simulation. Only a scheduler
    /// may step this clock; physics, Update and the wall clock do not own it.
    /// </summary>
    public sealed class WorldClock
    {
        public WorldMinute Current { get; private set; }

        public WorldClock(WorldMinute start = default)
        {
            Current = start;
        }

        internal void Step()
        {
            Current = Current.Add(1);
        }
    }
}