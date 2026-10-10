using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Inventory
{
    /// <summary>
    /// A persistent world pickup identity independent of GameObject instance IDs.
    /// Quantity is positive and stable; collected status belongs to InventoryState.
    /// </summary>
    public sealed class SalvagePickup
    {
        public EntityId Id { get; }
        public EntityId ItemId { get; }
        public long Quantity { get; }

        public SalvagePickup(EntityId id, EntityId itemId, long quantity)
        {
            if (!id.IsValid)
                throw new ArgumentException("Persistent pickup ID is required.", nameof(id));
            if (!itemId.IsValid)
                throw new ArgumentException("Item definition ID is required.", nameof(itemId));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            Id = id;
            ItemId = itemId;
            Quantity = quantity;
        }
    }
}
