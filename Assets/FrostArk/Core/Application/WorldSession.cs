using System;
using System.Collections.Generic;
using FrostArk.Core.Commands;
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
        private bool _disposed;

        public event Action<WorldSessionChange> Changed;

        /// <summary>Empty foundation fixture, not final authored starting content.</summary>
        public WorldSession() : this(WorldState.Create(WorldMinute.Zero,
            Array.Empty<SupplyStock>(), Array.Empty<EntityId>())) {}

        public WorldSession(WorldState state, uint seed = 0,
            IEnumerable<IWorldMinuteSystem> systems = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                systems ?? Array.Empty<IWorldMinuteSystem>(), seed);
        }

        private WorldSession(WorldState state, SimulationScheduler scheduler)
        {
            _state = state;
            _scheduler = scheduler;
        }

        public long Revision { get { Guard(); return _state.Revision; } }
        public WorldMinute Minute { get { Guard(); return _state.Minute; } }

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
            return SaveGameDto.Capture(_state, _scheduler);
        }

        public static WorldSession Restore(SaveGameDto snapshot,
            IEnumerable<IWorldMinuteSystem> systems = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            var scheduler = snapshot.CreateScheduler(systems ?? Array.Empty<IWorldMinuteSystem>());
            return new WorldSession(snapshot.State, scheduler);
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
