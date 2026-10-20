using System;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Stable-ID scene adapter for a shipboard crew member. Does not create a
    /// second crew entity, NavMeshAgent or tick clock. ShipGridWalker owns
    /// ship-local presentation motion and rejects unreachable targets.
    /// </summary>
    public sealed class ShipGridCrewExecutor : ICrewTheaterMoveExecutor
    {
        private readonly ShipGridWalker _walker;

        public ShipGridCrewExecutor(EntityId actorId, EntityId sceneId,
            ShipGridWalker walker)
        {
            if (!actorId.IsValid || !sceneId.IsValid)
                throw new ArgumentException("Stable actor and scene IDs required.");
            _walker = walker != null ? walker :
                throw new ArgumentNullException(nameof(walker));
            ActorId = actorId;
            SceneId = sceneId;
        }

        public EntityId ActorId { get; }
        public EntityId SceneId { get; }
        public PhysicalTheater Theater => PhysicalTheater.Ship;

        public bool TryMove(CrewMoveGoal destination) =>
            destination.Theater == PhysicalTheater.Ship &&
            _walker != null && _walker.isActiveAndEnabled &&
            _walker.gameObject.activeInHierarchy &&
            _walker.IsConfigured &&
            _walker.TryMoveTo(destination.ShipCell);
    }
}
