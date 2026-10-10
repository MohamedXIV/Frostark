using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    public enum ThermalWarning
    {
        None = 0,
        Cold = 1,
        CriticalCold = 2
    }

    /// <summary>
    /// Fixed-point thermal energy with positive capacity (energy/milli-C).
    /// Room integrity modifies environmental losses, not conservation.
    /// Exposure history remains part of the persistent room snapshot.
    /// </summary>
    public sealed class ThermalCompartment
    {
        public EntityId Id { get; }
        public long HeatCapacity { get; }
        public long Energy { get; }
        public int ExteriorConductance { get; }
        public int InsulationPermille { get; }
        public int IntegrityPermille { get; }
        public int ColdExposureMinutes { get; }
        public long TemperatureMilliC => Energy / HeatCapacity;

        public ThermalWarning Warning => TemperatureMilliC < -5_000 && ColdExposureMinutes >= 90
            ? ThermalWarning.CriticalCold
            : TemperatureMilliC < 5_000 && ColdExposureMinutes >= 30
                ? ThermalWarning.Cold : ThermalWarning.None;

        public ThermalCompartment(EntityId id, long heatCapacity, long energy,
            int exteriorConductance, int insulationPermille, int integrityPermille,
            int coldExposureMinutes = 0)
        {
            if (!id.IsValid) throw new ArgumentException("Room requires a stable ID.", nameof(id));
            if (heatCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(heatCapacity));
            if (exteriorConductance < 0 || exteriorConductance > 100_000)
                throw new ArgumentOutOfRangeException(nameof(exteriorConductance));
            if (insulationPermille < 0 || insulationPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(insulationPermille));
            if (integrityPermille < 0 || integrityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(integrityPermille));
            if (coldExposureMinutes < 0)
                throw new ArgumentOutOfRangeException(nameof(coldExposureMinutes));
            Id = id;
            HeatCapacity = heatCapacity;
            Energy = energy;
            ExteriorConductance = exteriorConductance;
            InsulationPermille = insulationPermille;
            IntegrityPermille = integrityPermille;
            ColdExposureMinutes = coldExposureMinutes;
        }

        internal ThermalCompartment AtEnergy(long energy)
        {
            int minutes = energy / HeatCapacity < 5_000
                ? checked(ColdExposureMinutes + 1) : 0;
            return new ThermalCompartment(Id, HeatCapacity, energy, ExteriorConductance,
                InsulationPermille, IntegrityPermille, minutes);
        }
    }
}
