using System;
using System.Collections.Generic;
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

        public SaveGameDto Capture()
        {
            Guard();
            return SaveGameDto.Capture(_state, _scheduler, _overrides, _sceneCheckpoint);
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
