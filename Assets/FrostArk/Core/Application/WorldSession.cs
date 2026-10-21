using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Economy;
using FrostArk.Core.Ship;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Application
{
    /// <summary>
    /// The application-owned, scoped authority for one run. Never duplicate
    /// totals into a MonoBehaviour: every read projection derives from _state.
    /// Core is Unity-independent and no callbacks determine simulation time.
    /// </summary>
    public sealed class WorldSession : IDisposable
    {
        private WorldState _state;
        private readonly SimulationScheduler _scheduler;
        private readonly WorldCommandBus _commands = new WorldCommandBus();
        private readonly IReadOnlyList<ProceduralOverride> _overrides;
        private PhysicalSceneCheckpoint _sceneCheckpoint;
        private readonly Dictionary<EntityId, PendingCrewMove> _crewMoves =
            new Dictionary<EntityId, PendingCrewMove>();
        private bool _disposed;

        public event Action<WorldSessionChange> Changed;

        /// <summary>Empty foundation fixture, not final authored starting content.</summary>
        public WorldSession() : this(WorldState.Create(WorldMinute.Zero,
            Array.Empty<SupplyStock>(), Array.Empty<EntityId>())) {}

        public WorldSession(WorldState state, uint seed = 0,
            IEnumerable<IWorldMinuteSystem> systems = null,
            Func<WorldMinute, long> ambientMilliC = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                BindCanonicalSystems(systems, ambientMilliC), seed);
            _overrides = Array.Empty<ProceduralOverride>();
            _sceneCheckpoint = null;
        }

        private WorldSession(SaveGameDto snapshot,
            IEnumerable<IWorldMinuteSystem> systems,
            Func<WorldMinute, long> ambientMilliC)
        {
            _state = snapshot.State;
            _scheduler = snapshot.CreateScheduler(BindCanonicalSystems(systems, ambientMilliC));
            _overrides = new List<ProceduralOverride>(snapshot.ProceduralOverrides).AsReadOnly();
            _sceneCheckpoint = snapshot.SceneCheckpoint;
            foreach (var move in snapshot.PendingCrewMoves) _crewMoves.Add(move.CrewId, move);
        }

        // Built-in reducers must read and commit through this ONE session-owned
        // WorldState pointer. The scheduler only owns ordering and time; it does
        // not own fuel, crew, jobs, movement, or inventory.
        private IEnumerable<IWorldMinuteSystem> BindCanonicalSystems(
            IEnumerable<IWorldMinuteSystem> additionalSystems,
            Func<WorldMinute, long> ambientMilliC)
        {
            if (_state.Thermal.Rooms.Count > 0 && ambientMilliC == null)
                throw new ArgumentException(
                    "An authored thermal world requires an explicit ambient temperature provider.",
                    nameof(ambientMilliC));

            var canonical = new List<IWorldMinuteSystem>
            {
                new ShipCrewMinuteSystem(() => _state, next => _state = next),
                new ShipThermalMinuteSystem(() => _state, next => _state = next,
                    ambientMilliC ?? (_ => 0L)),
                new WorkshopMinuteSystem(() => _state, next => _state = next),
                new ShipTravelMinuteSystem(() => _state, next => _state = next)
            };
            if (additionalSystems != null)
                canonical.AddRange(additionalSystems);
            return canonical;
        }

        public long Revision { get { Guard(); return _state.Revision; } }
        public WorldMinute Minute { get { Guard(); return _state.Minute; } }
        public PhysicalSceneCheckpoint SceneCheckpoint { get { Guard(); return _sceneCheckpoint; } }

        /// <summary>
        /// Record an authored scene/anchor re-entry checkpoint without advancing
        /// simulation time. Call after successful crew transfer and before saving.
        /// A null checkpoint means physical re-entry has not been established.
        /// </summary>
        public void SetSceneCheckpoint(PhysicalSceneCheckpoint checkpoint)
        {
            Guard();
            checkpoint?.ValidateAgainst(_state);
            _sceneCheckpoint = checkpoint;
            // A physical order belongs to the scene where it was accepted.
            // Changing theater/scene invalidates its physical route but does
            // NOT change standing crew duties or ephemeral selection.
            var stale = _crewMoves.Where(p =>
                checkpoint == null || checkpoint.Theater != PhysicalTheater.Ship ||
                p.Value.SceneId != checkpoint.SceneId).Select(p => p.Key).ToArray();
            foreach (var id in stale) _crewMoves.Remove(id);
        }

        public IReadOnlyList<PendingCrewMove> PendingCrewMoves
        {
            get
            {
                Guard();
                return Array.AsReadOnly(_crewMoves.Values
                    .OrderBy(m => m.CrewId.Value, StringComparer.Ordinal).ToArray());
            }
        }

        public PendingCrewMove GetPendingCrewMove(EntityId crewId)
        {
            Guard();
            return _crewMoves.TryGetValue(crewId, out var move) ? move : null;
        }

        /// <summary>
        /// Record only a scene-accepted physical player order. Presentation
        /// dispatch MUST succeed first. A repeated actor order replaces the
        /// earlier target; mouse selection and interpolated paths never persist.
        /// </summary>
        public void AcceptShipCrewMove(EntityId crewId, ShipGridCell goal)
        {
            Guard();
            var member = _state.CrewDuty.Get(crewId);
            if (!_state.HasCrew(crewId) || member == null || member.IsAway ||
                member.Wounds >= 1000 || _sceneCheckpoint == null ||
                _sceneCheckpoint.Theater != PhysicalTheater.Ship ||
                !_state.Layout.Rooms.Any(r => r.Id == goal.RoomId))
                throw new InvalidOperationException("Cannot persist a ship Move without eligible crew and a matching ship checkpoint.");
            _crewMoves[crewId] = new PendingCrewMove(crewId, _sceneCheckpoint.SceneId, goal);
        }

        public bool CompleteCrewMove(EntityId crewId)
        {
            Guard();
            return _crewMoves.Remove(crewId);
        }

        public WorldSessionView Read()
        {
            Guard();
            return new WorldSessionView(_state);
        }

        public CommandResult Submit(IGameCommand command)
        {
            Guard();
            var result = _commands.Execute(_state, command);
            if (result.Success)
            {
                _state = result.State;
                Notify(new WorldSessionChange(WorldSessionChangeKind.Command,
                    new WorldSessionView(_state), result.Events));
            }
            return result;
        }

        public void Schedule(ScheduledWorldEvent worldEvent)
        {
            Guard();
            _scheduler.Schedule(worldEvent);
        }

        public WorldAdvanceResult Advance(long minutes)
        {
            Guard();
            return CommitAdvance(_scheduler.Advance(minutes));
        }

        public WorldAdvanceResult AdvanceUntil(WorldMinute target,
            SleepInterruptionPolicy policy = null)
        {
            Guard();
            return CommitAdvance(_scheduler.AdvanceUntil(target, policy));
        }

        public void AdvanceCombatTick(long tick)
        {
            Guard();
            var next = _state.AtCombatTick(tick);
            if (!ReferenceEquals(next, _state))
            {
                _state = next;
                Notify(new WorldSessionChange(WorldSessionChangeKind.Time,
                    new WorldSessionView(_state), Array.Empty<FrostArk.Core.Events.WorldEvent>()));
            }
        }

        public SaveGameDto Capture()
        {
            Guard();
            // Commands, wounds or physical dispatch can invalidate an order
            // without rewriting crew selection or inventory. Never save a
            // terminal/incapacitated/away crew's stale physical command.
            var valid = _crewMoves.Values.Where(m =>
                _state.HasCrew(m.CrewId) &&
                _state.CrewDuty.Get(m.CrewId) != null &&
                !_state.CrewDuty.Get(m.CrewId).IsAway &&
                _state.CrewDuty.Get(m.CrewId).Wounds < 1000 &&
                _state.Layout.Rooms.Any(r => r.Id == m.ShipGoal.RoomId)).ToArray();
            return SaveGameDto.Capture(_state, _scheduler, _overrides,
                _sceneCheckpoint, valid);
        }

        public static WorldSession Restore(SaveGameDto snapshot,
            IEnumerable<IWorldMinuteSystem> systems = null,
            Func<WorldMinute, long> ambientMilliC = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return new WorldSession(snapshot, systems, ambientMilliC);
        }

        private WorldAdvanceResult CommitAdvance(WorldAdvanceResult result)
        {
            if (result.AdvancedMinutes > 0)
            {
                _state = _state.AtMinute(result.EndedAt);
                Notify(new WorldSessionChange(WorldSessionChangeKind.Time,
                    new WorldSessionView(_state), Array.Empty<FrostArk.Core.Events.WorldEvent>()));
            }
            return result;
        }

        private void Notify(WorldSessionChange change)
        {
            Changed?.Invoke(change);
        }

        private void Guard()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WorldSession));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Changed = null;
        }
    }
}
