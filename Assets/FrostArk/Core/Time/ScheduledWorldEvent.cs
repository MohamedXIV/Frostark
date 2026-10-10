using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// An immutable event due at a specific world minute. Its ID is stable
    /// across a run and its kind is an authored event label, not an Action.
    /// </summary>
    public sealed class ScheduledWorldEvent
    {
        public EntityId Id { get; }
        public WorldMinute DueAt { get; }
        public string Kind { get; }
        public bool InterruptsSleep { get; }

        public ScheduledWorldEvent(EntityId id, WorldMinute dueAt, string kind, bool interruptsSleep = false)
        {
            if (!id.IsValid)
                throw new ArgumentException("Scheduled event must have a valid persistent ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Scheduled event kind is required.", nameof(kind));
            Id = id;
            DueAt = dueAt;
            Kind = kind;
            InterruptsSleep = interruptsSleep;
        }
    }
}