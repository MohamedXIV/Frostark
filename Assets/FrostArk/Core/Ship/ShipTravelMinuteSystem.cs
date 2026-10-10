using System;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    public sealed class NavigationTickResult
    {
        public ShipNavigationState State { get; }
        public long FuelConsumed { get; }
        public int DistanceMoved { get; }
        public int HullDamage { get; }
        internal NavigationTickResult(ShipNavigationState state, long fuelConsumed,
            int distanceMoved, int hullDamage)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            FuelConsumed = fuelConsumed; DistanceMoved = distanceMoved; HullDamage = hullDamage;
        }
    }

    /// <summary>After-Heat travel with a single WorldState pointer and no own clock.</summary>
    public sealed class ShipTravelMinuteSystem : IWorldMinuteSystem
    {
        private readonly Func<WorldState> _read;
        private readonly Action<WorldState> _commit;
        public SimulationPhase Phase => SimulationPhase.Travel;
        public string StableKey => "ship:travel";
        public NavigationTickResult? LastTick { get; private set; }
        public ShipTravelMinuteSystem(Func<WorldState> read, Action<WorldState> commit)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }
        public void Tick(WorldMinute minute)
        {
            var world = _read() ?? throw new InvalidOperationException("No current WorldState.");
            if (world.Minute != minute)
                throw new InvalidOperationException(
                    "Ship travel requires the Heat phase to commit this minute first.");
            var tick = world.Navigation.Step(world.Layout.PropulsionPermille,
                world.GetSupply(EntityId.Parse("supply:fuel")));
            LastTick = tick;
            if (tick.FuelConsumed > 0)
                _commit(world.WithTravelTick(minute, tick));
        }
    }
}
