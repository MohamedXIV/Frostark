using System;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Persistent, accepted player Move intent. Not mouse selection, an
    /// interpolated transform, a cached route or an interaction-slot claim.
    /// At reentry the theater adapter must validate terrain and restart
    /// locomotion from its scene-authoritative safe checkpoint.
    /// </summary>
    public sealed class PendingCrewMove
    {
        public EntityId CrewId { get; }
        public EntityId SceneId { get; }
        public PhysicalTheater Theater { get; }
        public ShipGridCell ShipGoal { get; }

        public PendingCrewMove(EntityId crewId, EntityId sceneId, ShipGridCell shipGoal)
        {
            if (!crewId.IsValid || !sceneId.IsValid || !shipGoal.RoomId.IsValid ||
                shipGoal.X < 0 || shipGoal.Y < 0)
                throw new ArgumentException("Pending ship Move requires stable crew, scene and grid destination.");
            CrewId = crewId;
            SceneId = sceneId;
            Theater = PhysicalTheater.Ship;
            ShipGoal = shipGoal;
        }
    }
}
