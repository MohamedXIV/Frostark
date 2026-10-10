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

    public sealed class SetCrewAwayCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public bool Away { get; }
        public long ExpectedRevision { get; }

        public SetCrewAwayCommand(EntityId id, EntityId crewId, bool away, long expectedRevision)
        {
            Id = id; CrewId = crewId; Away = away; ExpectedRevision = expectedRevision;
        }
    }

    public sealed class BuildShipModuleCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId ModuleId { get; }
        public EntityId BlueprintId { get; }
        public EntityId RoomId { get; }
        public int X { get; }
        public int Y { get; }
        public long ExpectedRevision { get; }
        public BuildShipModuleCommand(EntityId id, EntityId moduleId, EntityId blueprintId,
            EntityId roomId, int x, int y, long expectedRevision)
        { Id = id; ModuleId = moduleId; BlueprintId = blueprintId; RoomId = roomId;
          X = x; Y = y; ExpectedRevision = expectedRevision; }
    }

    public sealed class DamageShipModuleCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId ModuleId { get; }
        public int DamagePermille { get; }
        public long ExpectedRevision { get; }
        public DamageShipModuleCommand(EntityId id, EntityId moduleId, int damagePermille,
            long expectedRevision)
        { Id = id; ModuleId = moduleId; DamagePermille = damagePermille;
          ExpectedRevision = expectedRevision; }
    }

    public sealed class StartShipModuleRepairCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId ModuleId { get; }
        public long ExpectedRevision { get; }
        public StartShipModuleRepairCommand(EntityId id, EntityId moduleId, long expectedRevision)
        { Id = id; ModuleId = moduleId; ExpectedRevision = expectedRevision; }
    }

    public sealed class PlanShipRouteCommand : IGameCommand
    {
        private readonly EntityId[] _legs;
        public EntityId Id { get; }
        public long ExpectedRevision { get; }
        public System.Collections.Generic.IReadOnlyList<EntityId> LegIds => System.Array.AsReadOnly(_legs);
        public PlanShipRouteCommand(EntityId id, System.Collections.Generic.IReadOnlyList<EntityId> legIds,
            long expectedRevision)
        {
            Id = id; ExpectedRevision = expectedRevision;
            _legs = legIds == null ? System.Array.Empty<EntityId>() :
                new System.Collections.Generic.List<EntityId>(legIds).ToArray();
        }
    }

    public sealed class CancelShipRouteCommand : IGameCommand
    {
        public EntityId Id { get; }
        public long ExpectedRevision { get; }
        public CancelShipRouteCommand(EntityId id, long expectedRevision)
        { Id = id; ExpectedRevision = expectedRevision; }
    }

    public sealed class CollectSalvageCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId PickupId { get; }
        public long ExpectedRevision { get; }

        public CollectSalvageCommand(EntityId id, EntityId pickupId, long expectedRevision)
        {
            Id = id;
            PickupId = pickupId;
            ExpectedRevision = expectedRevision;
        }
    }

    public sealed class UnloadToShipCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId ItemId { get; }
        public long Quantity { get; }
        public long ExpectedRevision { get; }

        public UnloadToShipCommand(EntityId id, EntityId itemId, long quantity, long expectedRevision)
        {
            Id = id;
            ItemId = itemId;
            Quantity = quantity;
            ExpectedRevision = expectedRevision;
        }
    }
}
