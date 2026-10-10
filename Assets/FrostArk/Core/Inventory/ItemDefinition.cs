using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Inventory
{
    /// <summary>
    /// Authored immutable item type definition with a versioned content contract.
    /// Mass is measured in integer grams; no Unity assets or frame types.
    /// </summary>
    public sealed class ItemDefinition
    {
        public EntityId Id { get; }
        public int ContentVersion { get; }
        public long UnitWeightGrams { get; }
        public int MaxStackSize { get; }

        public ItemDefinition(EntityId id, int contentVersion, long unitWeightGrams,
            int maxStackSize)
        {
            if (!id.IsValid) throw new ArgumentException("Item ID is required.", nameof(id));
            if (contentVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(contentVersion));
            if (unitWeightGrams < 0)
                throw new ArgumentOutOfRangeException(nameof(unitWeightGrams));
            if (maxStackSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStackSize));
            Id = id;
            ContentVersion = contentVersion;
            UnitWeightGrams = unitWeightGrams;
            MaxStackSize = maxStackSize;
        }

        public long RequiredSlots(long quantity)
        {
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            return quantity / MaxStackSize + (quantity % MaxStackSize == 0 ? 0L : 1L);
        }

        public long TotalWeightGrams(long quantity)
        {
            if (quantity < 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            return checked(quantity * UnitWeightGrams);
        }
    }
}
