using System;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Saved presentation position for one persistent crew actor. Aboard
    /// actors use an authored grid cell; away actors use a plain shore X/Z
    /// point resolved against the loaded environment NavMesh. This is not
    /// canonical simulation state and never carries a Unity object reference.
    /// </summary>
    public readonly struct PhysicalActorLocation
    {
        public EntityId CrewId { get; }
        public PhysicalTheater Theater { get; }
        public ShipGridCell ShipCell { get; }
        public float ShoreX { get; }
        public float ShoreZ { get; }

        private PhysicalActorLocation(EntityId crewId, PhysicalTheater theater,
            ShipGridCell shipCell, float shoreX, float shoreZ)
        {
            CrewId = crewId;
            Theater = theater;
            ShipCell = shipCell;
            ShoreX = shoreX;
            ShoreZ = shoreZ;
        }

        public static PhysicalActorLocation Aboard(EntityId crewId, ShipGridCell cell)
        {
            if (!crewId.IsValid)
                throw new ArgumentException("Persistent crew ID required.", nameof(crewId));
            return new PhysicalActorLocation(crewId, PhysicalTheater.Ship, cell, 0f, 0f);
        }

        public static PhysicalActorLocation Ashore(EntityId crewId, float x, float z)
        {
            if (!crewId.IsValid)
                throw new ArgumentException("Persistent crew ID required.", nameof(crewId));
            if (float.IsNaN(x) || float.IsInfinity(x) ||
                float.IsNaN(z) || float.IsInfinity(z))
                throw new ArgumentOutOfRangeException(nameof(x),
                    "Shore positions must be finite.");
            return new PhysicalActorLocation(crewId, PhysicalTheater.Shore, default, x, z);
        }
    }
}
