using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    /// <summary>
    /// Persisted procedural alteration keyed by stable site/entity identity,
    /// never by a transient Unity GameObject instance ID.
    /// </summary>
    public readonly struct ProceduralOverride
    {
        public EntityId Id { get; }
        public string Value { get; }

        public ProceduralOverride(EntityId id, string value)
        {
            if (!id.IsValid) throw new ArgumentException("Override ID is required.", nameof(id));
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Override value is required.", nameof(value));
            Id = id;
            Value = value;
        }
    }

    /// <summary>
    /// A frozen capture of canonical world truth, pending scheduler events,
    /// consumed event IDs, world RNG seed and permanent site overrides.
    /// Engine scene assets are deliberately absent.
    /// </summary>
    public sealed class SaveGameDto
    {
        private readonly ScheduledWorldEvent[] _pending;
        private readonly EntityId[] _reserved;
        private readonly ProceduralOverride[] _overrides;
        private readonly PendingCrewMove[] _crewMoves;

        public WorldState State { get; }
        public uint Seed { get; }
        public PhysicalSceneCheckpoint SceneCheckpoint { get; }
        public IReadOnlyList<ProceduralOverride> ProceduralOverrides =>
            Array.AsReadOnly(_overrides);
        public IReadOnlyList<PendingCrewMove> PendingCrewMoves =>
            Array.AsReadOnly(_crewMoves);

        private SaveGameDto(WorldState state, uint seed,
            IEnumerable<ScheduledWorldEvent> pending, IEnumerable<EntityId> reserved,
            IEnumerable<ProceduralOverride> overrides,
            PhysicalSceneCheckpoint sceneCheckpoint,
            IEnumerable<PendingCrewMove> crewMoves)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Seed = seed;
            sceneCheckpoint?.ValidateAgainst(state);
            SceneCheckpoint = sceneCheckpoint;
            // Ensure restored schedule data has the same strict validation
            // as a normally scheduled live session before it can be observed.
            var verifier = SimulationScheduler.FromSave(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed, pending, reserved);
            _pending = verifier.SnapshotPending().ToArray();
            _reserved = verifier.SnapshotReservedIds().ToArray();

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<ProceduralOverride>();
            foreach (var item in overrides ?? throw new ArgumentNullException(nameof(overrides)))
            {
                if (!item.Id.IsValid || string.IsNullOrEmpty(item.Value) ||
                    !seen.Add(item.Id.Value))
                    throw new ArgumentException("Duplicate or invalid procedural override.", nameof(overrides));
                list.Add(item);
            }
            _overrides = list.OrderBy(o => o.Id.Value, StringComparer.Ordinal).ToArray();
            var moves = (crewMoves ?? Array.Empty<PendingCrewMove>()).ToArray();
            if (moves.Length > 0 && sceneCheckpoint == null)
                throw new ArgumentException("Pending ship Moves require a saved ship checkpoint.",
                    nameof(crewMoves));
            var seenCrew = new HashSet<string>(StringComparer.Ordinal);
            var expectedShipScene = moves.Length > 0
                ? (sceneCheckpoint.Theater == PhysicalTheater.Ship
                    ? sceneCheckpoint.SceneId
                    : moves[0]?.SceneId ?? default)
                : default;
            foreach (var move in moves)
            {
                if (move == null || !seenCrew.Add(move.CrewId.Value) ||
                    !expectedShipScene.IsValid ||
                    move.SceneId != expectedShipScene ||
                    (sceneCheckpoint.Theater == PhysicalTheater.Shore &&
                     move.SceneId == sceneCheckpoint.SceneId) ||
                    !state.HasCrew(move.CrewId) ||
                    state.CrewDuty.Get(move.CrewId) == null ||
                    state.CrewDuty.Get(move.CrewId).IsAway ||
                    state.CrewDuty.Get(move.CrewId).Wounds >= 1000 ||
                    !state.Layout.Rooms.Any(r => r.Id == move.ShipGoal.RoomId))
                    throw new ArgumentException("Pending Move must belong to unique available ship crew in an authored room of the saved scene.",
                        nameof(crewMoves));
            }
            _crewMoves = moves.OrderBy(m => m.CrewId.Value, StringComparer.Ordinal).ToArray();
        }

        public static SaveGameDto Capture(WorldState state, SimulationScheduler scheduler,
            IEnumerable<ProceduralOverride> overrides = null,
            PhysicalSceneCheckpoint sceneCheckpoint = null,
            IEnumerable<PendingCrewMove> crewMoves = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (scheduler == null) throw new ArgumentNullException(nameof(scheduler));
            if (state.Minute != scheduler.Clock.Current)
                throw new ArgumentException("The world state and scheduler must share the same authoritative minute.");
            return new SaveGameDto(state, scheduler.Seed,
                scheduler.SnapshotPending(), scheduler.SnapshotReservedIds(),
                overrides ?? Array.Empty<ProceduralOverride>(), sceneCheckpoint, crewMoves);
        }

        internal static SaveGameDto Rehydrate(WorldState state, uint seed,
            IEnumerable<ScheduledWorldEvent> pending, IEnumerable<EntityId> reserved,
            IEnumerable<ProceduralOverride> overrides,
            PhysicalSceneCheckpoint sceneCheckpoint = null,
            IEnumerable<PendingCrewMove> crewMoves = null) =>
            new SaveGameDto(state, seed, pending, reserved, overrides, sceneCheckpoint, crewMoves);

        internal IReadOnlyList<ScheduledWorldEvent> SnapshotPending() =>
            Array.AsReadOnly(_pending);

        internal IReadOnlyList<EntityId> SnapshotReserved() =>
            Array.AsReadOnly(_reserved);

        public SimulationScheduler CreateScheduler(IEnumerable<IWorldMinuteSystem> systems) =>
            SimulationScheduler.FromSave(new WorldClock(State.Minute), systems, Seed, _pending, _reserved);
    }
}
