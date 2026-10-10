using FrostArk.Core.World;

namespace FrostArk.Core.Commands
{
    public sealed class WithdrawSupplyCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId SupplyId { get; }
        public long Quantity { get; }
        public long ExpectedRevision { get; }

        public WithdrawSupplyCommand(EntityId id, EntityId supplyId, long quantity, long expectedRevision)
        {
            Id = id;
            SupplyId = supplyId;
            Quantity = quantity;
            ExpectedRevision = expectedRevision;
        }
    }

    public sealed class AssignCrewCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public EntityId StationId { get; }
        public long ExpectedRevision { get; }

        public AssignCrewCommand(EntityId id, EntityId crewId, EntityId stationId, long expectedRevision)
        {
            Id = id;
            CrewId = crewId;
            StationId = stationId;
            ExpectedRevision = expectedRevision;
        }
    }

    public sealed class UnassignCrewCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public long ExpectedRevision { get; }

        public UnassignCrewCommand(EntityId id, EntityId crewId, long expectedRevision)
        {
            Id = id;
            CrewId = crewId;
            ExpectedRevision = expectedRevision;
        }
    }
}