using System;
using System.Collections.Generic;
using FrostArk.Core.World;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// Immutable outcome of one world advance. Events are an ordered copy
    /// of events actually fired, suitable for later command/event projections.
    /// </summary>
    public sealed class WorldAdvanceResult
    {
        public WorldMinute StartedAt { get; }
        public WorldMinute EndedAt { get; }
        public long AdvancedMinutes => EndedAt.ElapsedSince(StartedAt);
        public ScheduledWorldEvent Interruption { get; }
        public bool WasInterrupted => Interruption != null;
        public IReadOnlyList<ScheduledWorldEvent> FiredEvents { get; }

        internal WorldAdvanceResult(WorldMinute startedAt, WorldMinute endedAt,
            ScheduledWorldEvent interruption, List<ScheduledWorldEvent> firedEvents)
        {
            StartedAt = startedAt;
            EndedAt = endedAt;
            Interruption = interruption;
            FiredEvents = Array.AsReadOnly(firedEvents.ToArray());
        }
    }
}