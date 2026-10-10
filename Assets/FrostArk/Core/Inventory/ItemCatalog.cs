using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Inventory
{
    /// <summary>
    /// Case-sensitive, deterministic catalog. Later content authoring can
    /// reference these versioned definitions without Unity-specific objects.
    /// </summary>
    public sealed class ItemCatalog
    {
        private readonly Dictionary<string, ItemDefinition> _items;

        public ItemCatalog(IEnumerable<ItemDefinition> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            _items = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (item == null || !_items.TryAdd(item.Id.Value, item))
                    throw new ArgumentException("Catalog entries must be non-null and have unique IDs.", nameof(items));
            }
        }

        public ItemDefinition Get(EntityId id)
        {
            if (!id.IsValid) return null;
            return _items.TryGetValue(id.Value, out var item) ? item : null;
        }

        public IReadOnlyList<ItemDefinition> Snapshot() =>
            Array.AsReadOnly(_items.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Value).ToArray());
    }
}
