using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Events
{
    public enum WorldEventKind
    {
        SupplyWithdrawn = 0,
        CrewAssigned = 1,
        CrewUnassigned = 2,
        SalvageCollected = 3,
        SalvageUnloaded = 4,
        CrewDispatched = 5,
        CrewReturned = 6,
        ShipModuleBuilt = 7,
        ShipModuleDamaged = 8,
        ShipModuleRepairStarted = 9
    }

    /// <summary>
    /// Provenance from one committed command. No Unity scene or GameObject ID.
    /// </summary>
    public sealed class WorldEvent
    {
        public EntityId EventId { get; }
        public EntityId CommandId { get; }
        public WorldMinute Minute { get; }
        public long Revision { get; }
        public WorldEventKind Kind { get; }
        public EntityId SubjectId { get; }
        public EntityId RelatedId { get; }
        public long Quantity { get; }

        public WorldEvent(EntityId commandId, WorldMinute minute, long revision,
            WorldEventKind kind, EntityId subjectId, EntityId relatedId = default,
            long quantity = 0)
        {
            if (!commandId.IsValid)
                throw new ArgumentException("Event provenance requires a valid command ID.", nameof(commandId));
            if (!subjectId.IsValid)
                throw new ArgumentException("Event provenance requires a valid subject.", nameof(subjectId));
            if (revision <= 0)
                throw new ArgumentOutOfRangeException(nameof(revision));

            EventId = EntityId.Parse("event:" + commandId.Value + ":0");
            CommandId = commandId;
            Minute = minute;
            Revision = revision;
            Kind = kind;
            SubjectId = subjectId;
            RelatedId = relatedId;
            Quantity = quantity;
        }
    }
}