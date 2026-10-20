using System;
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
    public sealed class StartWorkshopJobCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId JobId { get; }
        public EntityId RecipeId { get; }
        public long ExpectedRevision { get; }
        public StartWorkshopJobCommand(EntityId id, EntityId jobId,
            EntityId recipeId, long expectedRevision)
        {
            Id = id; JobId = jobId; RecipeId = recipeId; ExpectedRevision = expectedRevision;
        }
    }

    public sealed class BuyMarketItemCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId MarketId { get; }
        public EntityId ItemId { get; }
        public long Quantity { get; }
        public long ExpectedRevision { get; }
        public BuyMarketItemCommand(EntityId id, EntityId marketId, EntityId itemId,
            long quantity, long expectedRevision)
        {
            Id = id; MarketId = marketId; ItemId = itemId;
            Quantity = quantity; ExpectedRevision = expectedRevision;
        }
    }

    public sealed class SellMarketItemCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId MarketId { get; }
        public EntityId ItemId { get; }
        public long Quantity { get; }
        public long ExpectedRevision { get; }
        public SellMarketItemCommand(EntityId id, EntityId marketId, EntityId itemId,
            long quantity, long expectedRevision)
        {
            Id = id; MarketId = marketId; ItemId = itemId;
            Quantity = quantity; ExpectedRevision = expectedRevision;
        }
    }

    /// <summary>One validated fire intent; target physics is external G3 evidence.</summary>
    public sealed class FireWeaponCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId WeaponId { get; }
        public long ExpectedRevision { get; }
        public FireWeaponCommand(EntityId id, EntityId weaponId, long expectedRevision)
        {
            Id = id;
            WeaponId = weaponId;
            ExpectedRevision = expectedRevision;
        }
    }

    public sealed class ReloadWeaponCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId WeaponId { get; }
        public long ExpectedRevision { get; }

        public ReloadWeaponCommand(EntityId id, EntityId weaponId, long expectedRevision)
        {
            if (!id.IsValid) throw new ArgumentException("Command ID required.", nameof(id));
            if (!weaponId.IsValid) throw new ArgumentException("Weapon ID required.", nameof(weaponId));
            if (expectedRevision < 0) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
            Id = id;
            WeaponId = weaponId;
            ExpectedRevision = expectedRevision;
        }
    }
    public sealed class TreatCrewWoundCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public EntityId TreatmentItemId { get; }
        public long ExpectedRevision { get; }
        public TreatCrewWoundCommand(EntityId id, EntityId crewId,
            EntityId treatmentItemId, long expectedRevision)
        {
            Id = id; CrewId = crewId; TreatmentItemId = treatmentItemId;
            ExpectedRevision = expectedRevision;
        }
    }

    public sealed class LootSiteCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId SiteId { get; }
        public long ExpectedRevision { get; }
        public LootSiteCommand(EntityId id, EntityId siteId, long expectedRevision)
        {
            Id = id; SiteId = siteId; ExpectedRevision = expectedRevision;
        }
    }

    public sealed class DamageSiteCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId SiteId { get; }
        public int DamagePermille { get; }
        public long ExpectedRevision { get; }
        public DamageSiteCommand(EntityId id, EntityId siteId, int damagePermille,
            long expectedRevision)
        {
            Id = id; SiteId = siteId; DamagePermille = damagePermille;
            ExpectedRevision = expectedRevision;
        }
    }

    /// <summary>Authoritative combat consequence inflicts persistent wounds on crew.</summary>
    public sealed class DamageCrewCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public int WoundPermille { get; }
        public long ExpectedRevision { get; }

        public DamageCrewCommand(EntityId id, EntityId crewId, int woundPermille,
            long expectedRevision)
        {
            Id = id;
            CrewId = crewId;
            WoundPermille = woundPermille;
            ExpectedRevision = expectedRevision;
        }
    }
    /// <summary>Consume one authored food/drink item held by the ship.</summary>
    public sealed class ConsumeCrewProvisionCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public EntityId ItemId { get; }
        public long ExpectedRevision { get; }

        public ConsumeCrewProvisionCommand(EntityId id, EntityId crewId,
            EntityId itemId, long expectedRevision)
        {
            Id = id; CrewId = crewId; ItemId = itemId;
            ExpectedRevision = expectedRevision;
        }
    }

    /// <summary>Claim one authored interaction position before navigation.</summary>
    public sealed class ClaimCrewInteractionCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public EntityId SlotId { get; }
        public int LifetimeMinutes { get; }
        public long ExpectedRevision { get; }

        public ClaimCrewInteractionCommand(EntityId id, EntityId crewId,
            EntityId slotId, int lifetimeMinutes, long expectedRevision)
        {
            Id = id; CrewId = crewId; SlotId = slotId;
            LifetimeMinutes = lifetimeMinutes; ExpectedRevision = expectedRevision;
        }
    }

    /// <summary>Cancel/complete occupancy; only the current claim owner can release.</summary>
    public sealed class ReleaseCrewInteractionCommand : IGameCommand
    {
        public EntityId Id { get; }
        public EntityId CrewId { get; }
        public EntityId SlotId { get; }
        public long ExpectedRevision { get; }
        public ReleaseCrewInteractionCommand(EntityId id, EntityId crewId,
            EntityId slotId, long expectedRevision)
        {
            Id = id; CrewId = crewId; SlotId = slotId;
            ExpectedRevision = expectedRevision;
        }
    }

}
