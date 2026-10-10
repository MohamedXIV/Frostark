using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// Reference minute-step scheduler. Batched advancement reuses the same
    /// ordered tick loop, preserving exact equivalence for supported systems.
    /// No Unity physics, wall time or unseeded randomness is involved.
    /// </summary>
    public sealed class SimulationScheduler
    {
        private readonly IWorldMinuteSystem[] _systems;
        private readonly List<ScheduledWorldEvent> _pending = new List<ScheduledWorldEvent>();
        private readonly HashSet<string> _eventIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly uint _seed;

        public WorldClock Clock { get; }

        public SimulationScheduler(WorldClock clock, IEnumerable<IWorldMinuteSystem> systems, uint seed = 0)
        {
            Clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (systems == null)
                throw new ArgumentNullException(nameof(systems));
            _seed = seed;

            _systems = systems.ToArray();
            if (_systems.Any(s => s == null || string.IsNullOrWhiteSpace(s.StableKey)))
                throw new ArgumentException("All systems must have a non-empty stable key.", nameof(systems));

            _systems = _systems.OrderBy(s => s.Phase)
                .ThenBy(s => s.StableKey, StringComparer.Ordinal)
                .ToArray();

            for (int i = 1; i < _systems.Length; ++i)
            {
                if (_systems[i - 1].Phase == _systems[i].Phase &&
                    string.Equals(_systems[i - 1].StableKey, _systems[i].StableKey, StringComparison.Ordinal))
                    throw new ArgumentException("Duplicate system phase/key identity.", nameof(systems));
            }
        }

        public void Schedule(ScheduledWorldEvent worldEvent)
        {
            if (worldEvent == null)
                throw new ArgumentNullException(nameof(worldEvent));
            if (worldEvent.DueAt <= Clock.Current)
                throw new ArgumentOutOfRangeException(nameof(worldEvent), "Due time must be after the current world minute.");
            if (!_eventIds.Add(worldEvent.Id.Value))
                throw new InvalidOperationException("Duplicate scheduled event ID: " + worldEvent.Id.Value);
            _pending.Add(worldEvent);
        }

        public WorldAdvanceResult Advance(long minutes)
        {
            if (minutes < 0)
                throw new ArgumentOutOfRangeException(nameof(minutes), "Cannot reverse simulation time.");
            return AdvanceUntil(Clock.Current.Add(minutes));
        }

        public WorldAdvanceResult AdvanceUntil(WorldMinute target, SleepInterruptionPolicy policy = null)
        {
            if (target < Clock.Current)
                throw new ArgumentOutOfRangeException(nameof(target), "Cannot advance backwards.");
            var start = Clock.Current;
            var fired = new List<ScheduledWorldEvent>();

            while (Clock.Current < target)
            {
                Clock.Step();
                var minute = Clock.Current;
                foreach (var system in _systems)
                    system.Tick(minute);

                var due = _pending.Where(e => e.DueAt == minute)
                    .OrderBy(e => StableHash32.Of(e.Id.Value, _seed))
                    .ThenBy(e => e.Id.Value, StringComparer.Ordinal).ToArray();

                foreach (var worldEvent in due)
                {
                    _pending.Remove(worldEvent);
                    fired.Add(worldEvent);
                }

                if (policy != null)
                {
                    foreach (var worldEvent in due)
                    {
                        if (policy.ShouldInterrupt(worldEvent))
                            return new WorldAdvanceResult(start, minute, worldEvent, fired);
                    }
                }
            }

            return new WorldAdvanceResult(start, Clock.Current, null, fired);
        }
    }
}