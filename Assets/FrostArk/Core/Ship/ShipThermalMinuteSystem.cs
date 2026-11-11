using System;
using FrostArk.Core.Crew;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    /// <summary>
    /// The scheduler's deterministic Heat phase adapter. The caller (session)
    /// retains the ONE authoritative WorldState pointer; this type only reads
    /// and proposes the next immutable snapshot through injected callbacks.
    /// No Update/FixedUpdate, no independent fuel cache or hidden time source.
    /// </summary>
    public sealed class ShipThermalMinuteSystem : IWorldMinuteSystem
    {
        private readonly Func<WorldState> _read;
        private readonly Action<WorldState> _commit;
        private readonly Func<WorldMinute, long> _ambient;
        private readonly Func<EntityId, DutyRole, WorldMinute, bool> _mayProduce;

        public SimulationPhase Phase => SimulationPhase.Heat;
        public string StableKey => "ship:thermal";
        public ThermalTickResult LastTick { get; private set; }

        public ShipThermalMinuteSystem(Func<WorldState> read, Action<WorldState> commit,
            Func<WorldMinute, long> ambientMilliC,
            Func<EntityId, DutyRole, WorldMinute, bool> mayProduce = null)
        {
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
            _ambient = ambientMilliC ?? throw new ArgumentNullException(nameof(ambientMilliC));
            _mayProduce = mayProduce;
        }

        public void Tick(WorldMinute minute)
        {
            var current = _read() ?? throw new InvalidOperationException("No authoritative world state.");
            if (minute.Value != checked(current.Minute.Value + 1))
                throw new InvalidOperationException("Thermal tick and WorldClock diverged.");
            if (current.Thermal.Rooms.Count == 0)
            {
                _commit(current.AtMinute(minute));
                LastTick = null;
                return;
            }

            var engineering = current.CrewDuty.Members.Count == 0 ? 1000 :
                current.CrewDuty.EffectiveSkill(DutyRole.Engineering, minute);
            var staffedEngineer = current.CrewDuty.StaffedCrewFor(DutyRole.Engineering, minute);
            if (staffedEngineer.IsValid && _mayProduce != null &&
                !_mayProduce(staffedEngineer, DutyRole.Engineering, minute))
                engineering = 0; // Physically unstaffed authored boiler station.
            var result = ShipThermalModel.Step(current.Thermal, _ambient(minute),
                current.GetSupply(EntityId.Parse("supply:fuel")),
                checked(engineering * current.Layout.BoilerOutputPermille) / 1000);
            var next = current.WithThermalMinute(minute, result);
            _commit(next);
            LastTick = result;
        }
    }
}
