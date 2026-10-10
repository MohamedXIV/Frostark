using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Inventory
{
    /// <summary>
    /// Immutable-on-transfer salvage and two-container stock. This value is
    /// intended to live INSIDE canonical WorldState, not in a Unity manager.
    /// A pickup's original identity is retained after collection to prevent
    /// duplication across subsequent commands and future save migrations.
    /// </summary>
    public sealed class InventoryState
    {
        private readonly ItemCatalog _catalog;
        private readonly Dictionary<string, SalvagePickup> _pickups;
        private readonly HashSet<string> _collected;
        private readonly Dictionary<string, long> _pack;
        private readonly Dictionary<string, long> _ship;

        public long PackMaxWeightGrams { get; }
        public int PackMaxSlots { get; }
        public long ShipMaxWeightGrams { get; }
        public int ShipMaxSlots { get; }

        private InventoryState(ItemCatalog catalog,
            Dictionary<string, SalvagePickup> pickups, HashSet<string> collected,
            Dictionary<string, long> pack, Dictionary<string, long> ship,
            long packWeight, int packSlots, long shipWeight, int shipSlots)
        {
            _catalog = catalog;
            _pickups = pickups;
            _collected = collected;
            _pack = pack;
            _ship = ship;
            PackMaxWeightGrams = packWeight;
            PackMaxSlots = packSlots;
            ShipMaxWeightGrams = shipWeight;
            ShipMaxSlots = shipSlots;
        }

        public static InventoryState Empty => Create(new ItemCatalog(Array.Empty<ItemDefinition>()),
            Array.Empty<SalvagePickup>(), 0, 0, 0, 0);

        public static InventoryState Create(ItemCatalog catalog,
            IEnumerable<SalvagePickup> pickups, long packWeightGrams,
            int packSlots, long shipWeightGrams, int shipSlots)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (pickups == null) throw new ArgumentNullException(nameof(pickups));
            if (packWeightGrams < 0) throw new ArgumentOutOfRangeException(nameof(packWeightGrams));
            if (packSlots < 0) throw new ArgumentOutOfRangeException(nameof(packSlots));
            if (shipWeightGrams < 0) throw new ArgumentOutOfRangeException(nameof(shipWeightGrams));
            if (shipSlots < 0) throw new ArgumentOutOfRangeException(nameof(shipSlots));

            var pickupMap = new Dictionary<string, SalvagePickup>(StringComparer.Ordinal);
            foreach (var pickup in pickups)
            {
                if (pickup == null || catalog.Get(pickup.ItemId) == null ||
                    !pickupMap.TryAdd(pickup.Id.Value, pickup))
                    throw new ArgumentException("Pickup IDs must be unique and refer to authored items.", nameof(pickups));
            }

            return new InventoryState(catalog, pickupMap,
                new HashSet<string>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal),
                new Dictionary<string, long>(StringComparer.Ordinal),
                packWeightGrams, packSlots, shipWeightGrams, shipSlots);
        }

        public bool IsCollected(EntityId pickupId) =>
            pickupId.IsValid && _collected.Contains(pickupId.Value);

        public long GetAmount(InventoryContainer container, EntityId itemId)
        {
            if (!itemId.IsValid) return 0;
            var stock = GetStock(container);
            return stock.TryGetValue(itemId.Value, out var amount) ? amount : 0;
        }

        public long GetUsedWeightGrams(InventoryContainer container)
        {
            long weight = 0;
            foreach (var kvp in GetStock(container))
                weight = checked(weight + _catalog.Get(EntityId.Parse(kvp.Key)).TotalWeightGrams(kvp.Value));
            return weight;
        }

        public long GetUsedSlots(InventoryContainer container)
        {
            long slots = 0;
            foreach (var kvp in GetStock(container))
                slots = checked(slots + _catalog.Get(EntityId.Parse(kvp.Key)).RequiredSlots(kvp.Value));
            return slots;
        }

        public InventoryTransactionResult TryCollect(EntityId pickupId)
        {
            if (!pickupId.IsValid || !_pickups.TryGetValue(pickupId.Value, out var pickup))
                return Reject(InventoryError.UnknownPickup);
            if (_collected.Contains(pickupId.Value))
                return Reject(InventoryError.AlreadyCollected);

            var pack = new Dictionary<string, long>(_pack, StringComparer.Ordinal);
            pack[pickup.ItemId.Value] = checked(GetAmount(InventoryContainer.Pack, pickup.ItemId) + pickup.Quantity);
            var limit = CheckLimit(pack, PackMaxWeightGrams, PackMaxSlots);
            if (limit != InventoryError.None)
                return Reject(limit);

            var collected = new HashSet<string>(_collected, StringComparer.Ordinal);
            collected.Add(pickup.Id.Value);
            return Accepted(new InventoryState(_catalog, _pickups, collected, pack, _ship,
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots));
        }

        public InventoryTransactionResult TryUnload(EntityId itemId, long quantity)
        {
            if (quantity <= 0) return Reject(InventoryError.InvalidQuantity);
            if (!itemId.IsValid || _catalog.Get(itemId) == null)
                return Reject(InventoryError.UnknownItem);
            long available = GetAmount(InventoryContainer.Pack, itemId);
            if (available < quantity) return Reject(InventoryError.InsufficientStock);

            var ship = new Dictionary<string, long>(_ship, StringComparer.Ordinal);
            ship[itemId.Value] = checked(GetAmount(InventoryContainer.ShipHold, itemId) + quantity);
            var limit = CheckLimit(ship, ShipMaxWeightGrams, ShipMaxSlots);
            if (limit != InventoryError.None) return Reject(limit);

            var pack = new Dictionary<string, long>(_pack, StringComparer.Ordinal);
            long left = available - quantity;
            if (left == 0) pack.Remove(itemId.Value);
            else pack[itemId.Value] = left;
            return Accepted(new InventoryState(_catalog, _pickups, _collected, pack, ship,
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots));
        }

        /// <summary>
        /// Deduct only actual carried Pack stock. Intended for atomic higher-level
        /// action commits (e.g. future weapon fire); not a standalone shot command.
        /// A rejection returns the exact original state.
        /// </summary>
        public InventoryTransactionResult TryConsumePack(EntityId itemId, long quantity)
        {
            if (quantity <= 0) return Reject(InventoryError.InvalidQuantity);
            if (!itemId.IsValid || _catalog.Get(itemId) == null)
                return Reject(InventoryError.UnknownItem);

            long available = GetAmount(InventoryContainer.Pack, itemId);
            if (quantity > available) return Reject(InventoryError.InsufficientStock);
            var nextPack = new Dictionary<string, long>(_pack, StringComparer.Ordinal);
            if (quantity == available) nextPack.Remove(itemId.Value);
            else nextPack[itemId.Value] = available - quantity;
            return Accepted(new InventoryState(_catalog, _pickups, _collected, nextPack, _ship,
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots));
        }

        public InventoryTransactionResult TryConsumeShip(EntityId itemId, long quantity)
        {
            if (quantity <= 0) return Reject(InventoryError.InvalidQuantity);
            if (!itemId.IsValid || _catalog.Get(itemId) == null)
                return Reject(InventoryError.UnknownItem);
            long available = GetAmount(InventoryContainer.ShipHold, itemId);
            if (quantity > available) return Reject(InventoryError.InsufficientStock);
            var next = new Dictionary<string, long>(_ship, StringComparer.Ordinal);
            if (quantity == available) next.Remove(itemId.Value);
            else next[itemId.Value] = available - quantity;
            return Accepted(new InventoryState(_catalog, _pickups, _collected, _pack, next,
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots));
        }

        private InventoryError CheckLimit(Dictionary<string, long> stock, long maxWeight, int maxSlots)
        {
            long grams = 0;
            long slots = 0;
            foreach (var kvp in stock)
            {
                var item = _catalog.Get(EntityId.Parse(kvp.Key));
                grams = checked(grams + item.TotalWeightGrams(kvp.Value));
                slots = checked(slots + item.RequiredSlots(kvp.Value));
            }
            if (grams > maxWeight) return InventoryError.TooHeavy;
            if (slots > maxSlots) return InventoryError.NotEnoughSlots;
            return InventoryError.None;
        }

        private Dictionary<string, long> GetStock(InventoryContainer container)
        {
            switch (container)
            {
                case InventoryContainer.Pack: return _pack;
                case InventoryContainer.ShipHold: return _ship;
                default: throw new ArgumentOutOfRangeException(nameof(container));
            }
        }

        private InventoryTransactionResult Reject(InventoryError reason) =>
            new InventoryTransactionResult(this, reason);

        private static InventoryTransactionResult Accepted(InventoryState next) =>
            new InventoryTransactionResult(next, InventoryError.None);



        /// <summary>
        /// Strictly reconstruct persisted inventory without trusting string IDs,
        /// duplicate rows or fabricated collected stock. No mutable collections
        /// can escape a restored state.
        /// </summary>
        internal static InventoryState RestoreSnapshot(ItemCatalog catalog,
            IEnumerable<SalvagePickup> pickups, IEnumerable<EntityId> collected,
            IEnumerable<KeyValuePair<EntityId, long>> pack,
            IEnumerable<KeyValuePair<EntityId, long>> ship,
            long packWeightGrams, int packSlots, long shipWeightGrams, int shipSlots)
        {
            var fresh = Create(catalog, pickups, packWeightGrams, packSlots,
                shipWeightGrams, shipSlots);
            var consumed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in collected ?? throw new ArgumentNullException(nameof(collected)))
            {
                if (!id.IsValid || !fresh._pickups.ContainsKey(id.Value) ||
                    !consumed.Add(id.Value))
                    throw new ArgumentException("Unknown or duplicate consumed pickup.", nameof(collected));
            }

            Dictionary<string, long> RestoreStock(IEnumerable<KeyValuePair<EntityId, long>> source)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                var rows = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var entry in source)
                {
                    if (!entry.Key.IsValid || entry.Value <= 0 ||
                        catalog.Get(entry.Key) == null ||
                        !rows.TryAdd(entry.Key.Value, entry.Value))
                        throw new ArgumentException("Unknown, invalid or duplicate inventory stack.");
                }
                return rows;
            }

            var packState = RestoreStock(pack);
            var shipState = RestoreStock(ship);
            if (fresh.CheckLimit(packState, packWeightGrams, packSlots) != InventoryError.None ||
                fresh.CheckLimit(shipState, shipWeightGrams, shipSlots) != InventoryError.None)
                throw new ArgumentException("Loaded inventory exceeds capacity.");

            var collectedTotals = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var id in consumed)
            {
                var pickup = fresh._pickups[id];
                collectedTotals[pickup.ItemId.Value] = checked(
                    (collectedTotals.TryGetValue(pickup.ItemId.Value, out var amount) ? amount : 0) +
                    pickup.Quantity);
            }
            foreach (var row in packState)
            {
                var allocated = checked(row.Value + (shipState.TryGetValue(row.Key, out var shipQty) ? shipQty : 0));
                if (!collectedTotals.TryGetValue(row.Key, out var total) || allocated > total)
                    throw new ArgumentException("Loaded inventory exceeds collected pickup total.");
            }
            foreach (var row in shipState)
            {
                if (packState.ContainsKey(row.Key)) continue;
                if (!collectedTotals.TryGetValue(row.Key, out var total) || row.Value > total)
                    throw new ArgumentException("Loaded ship stock exceeds collected pickup total.");
            }

            return new InventoryState(catalog, fresh._pickups, consumed, packState, shipState,
                packWeightGrams, packSlots, shipWeightGrams, shipSlots);
        }

        /// <summary>Stable replay fingerprint, not a save format or security signature.</summary>
        public uint Fingerprint()
        {
            var canonical = new StringBuilder();
            void Add(string value)
            {
                canonical.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(value);
            }
            Add(PackMaxWeightGrams.ToString(CultureInfo.InvariantCulture));
            Add(PackMaxSlots.ToString(CultureInfo.InvariantCulture));
            Add(ShipMaxWeightGrams.ToString(CultureInfo.InvariantCulture));
            Add(ShipMaxSlots.ToString(CultureInfo.InvariantCulture));
            foreach (var item in SnapshotDefinitions())
            {
                Add("item"); Add(item.Id.Value);
                Add(item.ContentVersion.ToString(CultureInfo.InvariantCulture));
                Add(item.UnitWeightGrams.ToString(CultureInfo.InvariantCulture));
                Add(item.MaxStackSize.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var pickup in SnapshotPickups())
            {
                Add("pickup"); Add(pickup.Id.Value); Add(pickup.ItemId.Value);
                Add(pickup.Quantity.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var id in SnapshotCollected()) { Add("collected"); Add(id.Value); }
            foreach (var loc in new[] { InventoryContainer.Pack, InventoryContainer.ShipHold })
            {
                Add(loc.ToString());
                foreach (var stack in SnapshotStock(loc))
                {
                    Add(stack.Key.Value);
                    Add(stack.Value.ToString(CultureInfo.InvariantCulture));
                }
            }
            return StableHash32.Of(canonical.ToString());
        }

        // Canonical enumeration for the future v3 SaveCodec expansion.
        internal IReadOnlyList<ItemDefinition> SnapshotDefinitions() => _catalog.Snapshot();
        internal IReadOnlyList<SalvagePickup> SnapshotPickups() =>
            _pickups.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Value).ToArray();
        internal IReadOnlyList<EntityId> SnapshotCollected() =>
            _collected.OrderBy(x => x, StringComparer.Ordinal)
                .Select(EntityId.Parse).ToArray();
        internal IReadOnlyList<KeyValuePair<EntityId, long>> SnapshotStock(InventoryContainer container) =>
            GetStock(container).OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => new KeyValuePair<EntityId, long>(EntityId.Parse(x.Key), x.Value)).ToArray();
    }
}
