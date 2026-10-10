using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    /// <summary>Authored integer waypoint coordinates, not physical metres.</summary>
    public sealed class SeaWaypoint
    {
        public EntityId Id { get; }
        public int X { get; }
        public int Y { get; }
        public SeaWaypoint(EntityId id, int x, int y)
        {
            if (!id.IsValid) throw new ArgumentException("Waypoint ID is required.", nameof(id));
            if (x < -1000000 || x > 1000000 || y < -1000000 || y > 1000000)
                throw new ArgumentOutOfRangeException(nameof(x));
            Id = id; X = x; Y = y;
        }
    }

    /// <summary>Directed authored connection with bounded systemic environment.</summary>
    public sealed class SeaLeg
    {
        public EntityId Id { get; }
        public EntityId FromId { get; }
        public EntityId ToId { get; }
        public int DistanceUnits { get; }
        public int IcePermille { get; }
        public int StormPermille { get; }
        public int VisibilityPermille { get; }
        public SeaLeg(EntityId id, EntityId fromId, EntityId toId,
            int distanceUnits, int icePermille, int stormPermille, int visibilityPermille)
        {
            if (!id.IsValid || !fromId.IsValid || !toId.IsValid || fromId == toId)
                throw new ArgumentException("Leg must have distinct, valid waypoint IDs.");
            if (distanceUnits < 1 || distanceUnits > 1000000 ||
                icePermille < 0 || icePermille > 1000 ||
                stormPermille < 0 || stormPermille > 1000 ||
                visibilityPermille < 0 || visibilityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(distanceUnits));
            Id = id; FromId = fromId; ToId = toId;
            DistanceUnits = distanceUnits; IcePermille = icePermille;
            StormPermille = stormPermille; VisibilityPermille = visibilityPermille;
        }
    }

    public sealed class NavigationTuning
    {
        public int SpeedUnitsPerMinute { get; }
        public int FuelPerUnit { get; }
        public int IceDragPermille { get; }
        public int StormDragPermille { get; }
        public int SafeIcePermille { get; }
        public int IceDamagePerMinute { get; }
        public int StormDamagePerMinute { get; }

        public NavigationTuning(int speedUnitsPerMinute, int fuelPerUnit,
            int iceDragPermille, int stormDragPermille, int safeIcePermille,
            int iceDamagePerMinute, int stormDamagePerMinute)
        {
            if (speedUnitsPerMinute < 1 || speedUnitsPerMinute > 10000 ||
                fuelPerUnit < 1 || fuelPerUnit > 100000)
                throw new ArgumentOutOfRangeException(nameof(speedUnitsPerMinute));
            if (iceDragPermille < 0 || iceDragPermille > 1000 ||
                stormDragPermille < 0 || stormDragPermille > 1000 ||
                safeIcePermille < 0 || safeIcePermille > 1000 ||
                iceDamagePerMinute < 0 || iceDamagePerMinute > 1000 ||
                stormDamagePerMinute < 0 || stormDamagePerMinute > 1000)
                throw new ArgumentOutOfRangeException(nameof(iceDragPermille));
            SpeedUnitsPerMinute = speedUnitsPerMinute; FuelPerUnit = fuelPerUnit;
            IceDragPermille = iceDragPermille; StormDragPermille = stormDragPermille;
            SafeIcePermille = safeIcePermille; IceDamagePerMinute = iceDamagePerMinute;
            StormDamagePerMinute = stormDamagePerMinute;
        }
    }
}
