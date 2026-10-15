using System;
using System.Collections.Generic;
using System.Linq;

namespace FrostArk.Core.World
{
    /// <summary>
    /// A transient tactical input request. Actor selection is explicit, and a
    /// scene-resolved click becomes either a world destination or a persistent
    /// entity target. This is NOT a validated game command, navigation result,
    /// actor state, clock, or Unity screen coordinate.
    /// </summary>
    public enum TacticalOrderKind
    {
        Move = 0,
        Interact = 1,
        Attack = 2,
        Retreat = 3
    }

    /// <summary>
    /// Immutable, bounded selection + target envelope for issuing click
    /// commands to either the protagonist alone or a group of crew.
    /// The Unity adapter must still check path, reachability, line of sight,
    /// range, authority, and actor capabilities before executing actions.
    /// </summary>
    public sealed class TacticalOrderIntent
    {
        private readonly IReadOnlyList<EntityId> _actorIds;

        public IReadOnlyList<EntityId> ActorIds => _actorIds;
        public TacticalOrderKind Kind { get; }
        public bool HasDestination => Kind == TacticalOrderKind.Move || Kind == TacticalOrderKind.Retreat;
        public double DestinationX { get; }
        public double DestinationZ { get; }
        public bool HasEntityTarget => Kind == TacticalOrderKind.Interact ||
                                      Kind == TacticalOrderKind.Attack;
        public EntityId TargetId { get; }

        private TacticalOrderIntent(IEnumerable<EntityId> actorIds,
            TacticalOrderKind kind, double destinationX, double destinationZ, EntityId targetId)
        {
            if (actorIds == null)
                throw new ArgumentNullException(nameof(actorIds));

            var actors = actorIds.ToArray();
            if (actors.Length == 0 || actors.Any(actor => !actor.IsValid))
                throw new ArgumentException("At least one valid selected actor is required.",
                    nameof(actorIds));
            var sorted = actors.OrderBy(actor => actor.Value, StringComparer.Ordinal).ToArray();
            for (var i = 1; i < sorted.Length; i++)
                if (sorted[i] == sorted[i - 1])
                    throw new ArgumentException("Duplicate selected actor ID.", nameof(actorIds));

            _actorIds = Array.AsReadOnly(sorted);
            Kind = kind;
            DestinationX = destinationX;
            DestinationZ = destinationZ;
            TargetId = targetId;
        }

        /// <summary>
        /// World X/Z must be resolved from a Unity click *before* creating
        /// the intent. Finite does not mean walkable; scene navigation checks
        /// are mandatory at execution time.
        /// </summary>
        public static TacticalOrderIntent MoveTo(IEnumerable<EntityId> actorIds,
            double worldX, double worldZ)
        {
            if (double.IsNaN(worldX) || double.IsInfinity(worldX))
                throw new ArgumentOutOfRangeException(nameof(worldX));
            if (double.IsNaN(worldZ) || double.IsInfinity(worldZ))
                throw new ArgumentOutOfRangeException(nameof(worldZ));

            return new TacticalOrderIntent(actorIds, TacticalOrderKind.Move,
                worldX, worldZ, default);
        }

        public static TacticalOrderIntent RetreatTo(IEnumerable<EntityId> actorIds,
            double worldX, double worldZ)
        {
            if (double.IsNaN(worldX) || double.IsInfinity(worldX))
                throw new ArgumentOutOfRangeException(nameof(worldX));
            if (double.IsNaN(worldZ) || double.IsInfinity(worldZ))
                throw new ArgumentOutOfRangeException(nameof(worldZ));

            return new TacticalOrderIntent(actorIds, TacticalOrderKind.Retreat,
                worldX, worldZ, default);
        }

        public static TacticalOrderIntent InteractWith(IEnumerable<EntityId> actorIds,
            EntityId targetId) =>
            ForEntity(actorIds, TacticalOrderKind.Interact, targetId);

        public static TacticalOrderIntent AttackTarget(IEnumerable<EntityId> actorIds,
            EntityId targetId) =>
            ForEntity(actorIds, TacticalOrderKind.Attack, targetId);

        private static TacticalOrderIntent ForEntity(IEnumerable<EntityId> actorIds,
            TacticalOrderKind kind, EntityId targetId)
        {
            if (!targetId.IsValid)
                throw new ArgumentException("Entity orders need a valid target ID.",
                    nameof(targetId));
            return new TacticalOrderIntent(actorIds, kind, 0, 0, targetId);
        }
    }
}
