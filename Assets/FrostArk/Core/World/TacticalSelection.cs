using System;
using System.Collections.Generic;
using System.Linq;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Immutable transient set of selected actor IDs. No gameplay authority,
    /// Unity input binding, selection gesture, player location or save state.
    /// Every new snapshot is normalized to an ordinal ID order.
    /// </summary>
    public sealed class TacticalSelection
    {
        private readonly IReadOnlyList<EntityId> _actorIds;

        public static TacticalSelection Empty { get; } =
            new TacticalSelection(Array.Empty<EntityId>());

        private TacticalSelection(EntityId[] sortedUniqueActors)
        {
            _actorIds = Array.AsReadOnly(sortedUniqueActors);
        }

        public IReadOnlyList<EntityId> ActorIds => _actorIds;
        public int Count => _actorIds.Count;

        public bool Contains(EntityId actorId) =>
            actorId.IsValid && _actorIds.Contains(actorId);

        /// <summary>
        /// Replace the entire selection. A source enumerable is materialized
        /// immediately so later caller mutation cannot alter the snapshot.
        /// Duplicate clicks are idempotent; invalid entity IDs are not.
        /// </summary>
        public TacticalSelection Replace(IEnumerable<EntityId> actorIds)
        {
            if (actorIds == null)
                throw new ArgumentNullException(nameof(actorIds));

            var actors = actorIds.ToArray();
            if (actors.Any(x => !x.IsValid))
                throw new ArgumentException("Selection contains an invalid actor ID.",
                    nameof(actorIds));

            var unique = actors.Distinct()
                .OrderBy(x => x.Value, StringComparer.Ordinal).ToArray();
            if (unique.Length == 0)
                return Empty;
            if (unique.SequenceEqual(_actorIds))
                return this;
            return new TacticalSelection(unique);
        }

        /// <summary>
        /// Add one selected actor without mutating the previous snapshot.
        /// UI may map any pointer/controller gesture to this operation.
        /// </summary>
        public TacticalSelection Include(EntityId actorId)
        {
            RequireValid(actorId);
            if (Contains(actorId))
                return this;
            return Replace(_actorIds.Concat(new[] { actorId }));
        }

        public TacticalSelection Exclude(EntityId actorId)
        {
            RequireValid(actorId);
            if (!Contains(actorId))
                return this;
            return Replace(_actorIds.Where(x => x != actorId));
        }

        public TacticalSelection Clear() => Empty;

        private static void RequireValid(EntityId id)
        {
            if (!id.IsValid)
                throw new ArgumentException("Selected actor must have a valid stable ID.",
                    nameof(id));
        }
    }
}
