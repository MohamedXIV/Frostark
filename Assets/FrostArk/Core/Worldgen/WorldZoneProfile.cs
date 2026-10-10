using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Worldgen
{
    public enum ThermalZoneBand
    {
        Coastal = 0,
        PackIce = 1,
        DeepPolar = 2
    }

    /// <summary>
    /// Stable authored zone parameters. Temperatures are milli-Celsius,
    /// environmental pressure values are integers in 0..1000.
    /// This is not live weather; weather simulation stays in its own system.
    /// </summary>
    public sealed class WorldZoneProfile
    {
        public EntityId Id { get; }
        public ThermalZoneBand Band { get; }
        public int AmbientMilliC { get; }
        public int ContaminationPermille { get; }
        public int PackIcePermille { get; }

        public WorldZoneProfile(EntityId id, ThermalZoneBand band, int ambientMilliC,
            int contaminationPermille, int packIcePermille)
        {
            if (!id.IsValid)
                throw new ArgumentException("Zone requires stable ID.", nameof(id));
            if (!Enum.IsDefined(typeof(ThermalZoneBand), band))
                throw new ArgumentOutOfRangeException(nameof(band));
            if (ambientMilliC < -100_000 || ambientMilliC > 50_000)
                throw new ArgumentOutOfRangeException(nameof(ambientMilliC));
            if (contaminationPermille < 0 || contaminationPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(contaminationPermille));
            if (packIcePermille < 0 || packIcePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(packIcePermille));
            Id = id;
            Band = band;
            AmbientMilliC = ambientMilliC;
            ContaminationPermille = contaminationPermille;
            PackIcePermille = packIcePermille;
        }
    }
}
