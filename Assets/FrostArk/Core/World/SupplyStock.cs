using System;

namespace FrostArk.Core.World
{
    /// <summary>Positive-identity, non-negative starting supply stock.</summary>
    public readonly struct SupplyStock
    {
        public EntityId Id { get; }
        public long Quantity { get; }

        public SupplyStock(EntityId id, long quantity)
        {
            if (!id.IsValid)
                throw new ArgumentException("Supply ID is required.", nameof(id));
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            Id = id;
            Quantity = quantity;
        }
    }
}