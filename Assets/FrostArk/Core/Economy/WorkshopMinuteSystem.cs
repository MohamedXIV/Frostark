using System;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Economy
{
    /// <summary>Runs after Heat commits this WorldMinute, before Travel.</summary>
    public sealed class WorkshopMinuteSystem : IWorldMinuteSystem
    {
        private readonly Func<WorldState> _read;
        private readonly Action<WorldState> _commit;
        public SimulationPhase Phase => SimulationPhase.Wellbeing;
        public string StableKey => "economy:workshop";

        public WorkshopMinuteSystem(Func<WorldState> read, Action<WorldState> commit)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        public void Tick(WorldMinute minute)
        {
            var world = _read() ?? throw new InvalidOperationException("Missing WorldState.");
            var result = world.Economy.CompleteDue(minute, world.Inventory);
            _commit(world.WithWorkshopMinute(minute, result.Economy, result.Inventory));
        }
    }
}
