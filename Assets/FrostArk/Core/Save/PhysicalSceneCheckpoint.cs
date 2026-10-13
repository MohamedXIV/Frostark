using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Persisted physical re-entry theater. This does NOT capture a camera,
    /// Unity GameObject, NavMesh position, movement path or UI selection.
    /// </summary>
    public enum PhysicalTheater : byte
    {
        Ship = 1,
        Shore = 2
    }

    /// <summary>
    /// Stable authored scene/site/anchor IDs used to resolve the correct
    /// physical theater after loading a save. Shore members refer only to
    /// persistent crew whose canonical CrewDuty.IsAway flag is true.
    /// </summary>
    public sealed class PhysicalSceneCheckpoint
    {
        private readonly IReadOnlyList<EntityId> _partyIds;
        public PhysicalTheater Theater { get; }
        public EntityId SceneId { get; }
        public EntityId SiteId { get; }
        public EntityId SpawnAnchorId { get; }
        public IReadOnlyList<EntityId> PartyIds => _partyIds;

        private PhysicalSceneCheckpoint(PhysicalTheater theater,
            EntityId sceneId, EntityId siteId, EntityId spawnAnchorId,
            EntityId[] partyIds)
        {
            Theater = theater;
            SceneId = sceneId;
            SiteId = siteId;
            SpawnAnchorId = spawnAnchorId;
            _partyIds = Array.AsReadOnly(partyIds);
        }

        public static PhysicalSceneCheckpoint Ship(EntityId sceneId, EntityId spawnAnchorId)
        {
            RequireId(sceneId, nameof(sceneId));
            RequireId(spawnAnchorId, nameof(spawnAnchorId));
            return new PhysicalSceneCheckpoint(PhysicalTheater.Ship,
                sceneId, default, spawnAnchorId, Array.Empty<EntityId>());
        }

        public static PhysicalSceneCheckpoint Shore(EntityId sceneId, EntityId siteId,
            EntityId spawnAnchorId, IEnumerable<EntityId> partyIds)
        {
            RequireId(sceneId, nameof(sceneId));
            RequireId(siteId, nameof(siteId));
            RequireId(spawnAnchorId, nameof(spawnAnchorId));
            if (partyIds == null)
                throw new ArgumentNullException(nameof(partyIds));
            var ordered = partyIds.ToArray();
            if (ordered.Length == 0 || ordered.Any(id => !id.IsValid))
                throw new ArgumentException("Shore checkpoint requires valid crew IDs.", nameof(partyIds));
            ordered = ordered.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                if (ordered[i] == ordered[i - 1])
                    throw new ArgumentException("Duplicate physical party actor.", nameof(partyIds));

            return new PhysicalSceneCheckpoint(PhysicalTheater.Shore,
                sceneId, siteId, spawnAnchorId, ordered);
        }

        internal void ValidateAgainst(WorldState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (Theater != PhysicalTheater.Shore) return;
            foreach (var id in _partyIds)
            {
                if (!state.HasCrew(id))
                    throw new ArgumentException("Physical party actor is absent from the world.");
                var crew = state.CrewDuty.Get(id);
                if (crew == null || !crew.IsAway)
                    throw new ArgumentException(
                        "Physical shore party actor must be marked away in the canonical crew roster.");
            }
        }

        private static void RequireId(EntityId id, string parameter)
        {
            if (!id.IsValid)
                throw new ArgumentException("Stable authored physical reference ID required.", parameter);
        }
    }
}
