using System;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Crew-phase adapter bound to the session's canonical WorldState.
    /// Heat phase commits the same authoritative minute after Crew.
    /// </summary>
    public sealed class ShipCrewMinuteSystem : IWorldMinuteSystem
    {
        private readonly Func<WorldState> _read;
        private readonly Action<WorldState> _commit;
        public SimulationPhase Phase => SimulationPhase.Crew;
        public string StableKey => "ship:crew";

        public ShipCrewMinuteSystem(Func<WorldState> read, Action<WorldState> commit)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        public void Tick(WorldMinute minute)
        {
            var current = _read() ?? throw new InvalidOperationException("No authoritative world state.");
            if (minute.Value != checked(current.Minute.Value + 1))
                throw new InvalidOperationException("Crew phase and WorldClock diverged.");
            if (current.CrewDuty.Members.Count == 0 &&
                current.CrewDuty.RepairJobs.Count == 0)
                return;
            _commit(current.WithCrewTick(minute, current.CrewDuty.Tick(minute)));
        }
    }
}
