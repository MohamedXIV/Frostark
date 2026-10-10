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
        // Cumulative minted items from trusted crafting/buy operations. The
        // salvage-only validator must not reject legitimate provenance.
        private readonly Dictionary<string, long> _credited;

        public long PackMaxWeightGrams { get; }
        public int PackMaxSlots { get; }
        public long ShipMaxWeightGrams { get; }
        public int ShipMaxSlots { get; }

        private InventoryState(ItemCatalog catalog,
            Dictionary<string, SalvagePickup> pickups, HashSet<string> collected,
            Dictionary<string, long> pack, Dictionary<string, long> ship,
            long packWeight, int packSlots, long shipWeight, int shipSlots,
            Dictionary<string, long> credited = null)
        {
            _catalog = catalog;
            _pickups = pickups;
            _collected = collected;
            _pack = pack;
            _ship = ship;
            _credited = credited ?? new Dictionary<string, long>(StringComparer.Ordinal);
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
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots, _credited));
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
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots, _credited));
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
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots, _credited));
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
                PackMaxWeightGrams, PackMaxSlots, ShipMaxWeightGrams, ShipMaxSlots, _credited));
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
            long packWeightGrams, int packSlots, long shipWeightGrams, int shipSlots,
            IEnumerable<KeyValuePair<EntityId, long>> credited = null)
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
            var creditMap = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in credited ?? Array.Empty<KeyValuePair<EntityId, long>>())
            {
                if (!entry.Key.IsValid || entry.Value <= 0 ||
                    catalog.Get(entry.Key) == null ||
                    !creditMap.TryAdd(entry.Key.Value, entry.Value))
                    throw new ArgumentException("Duplicate, invalid or unknown credited item.");
            }

            // Stock may be sourced from consumed salvage, or legitimate minted
            // crafting/trading inflows. Both ledgers are monotonic provenance,
            // never a replacement for capacity and stock checks.
            foreach (var row in packState)
            {
                long claimed = checked(row.Value + (shipState.TryGetValue(row.Key,
                    out var shipQty) ? shipQty : 0L));
                long available = checked((collectedTotals.TryGetValue(row.Key,
                    out var salvaged) ? salvaged : 0L) +
                    (creditMap.TryGetValue(row.Key, out var generated) ? generated : 0L));
                if (claimed > available)
                    throw new ArgumentException("Loaded inventory exceeds verified inflows.");
            }
            foreach (var row in shipState)
            {
                if (packState.ContainsKey(row.Key)) continue;
                long available = checked((collectedTotals.TryGetValue(row.Key,
                    out var salvaged) ? salvaged : 0L) +
                    (creditMap.TryGetValue(row.Key, out var generated) ? generated : 0L));
                if (row.Value > available)
                    throw new ArgumentException("Loaded ship stock exceeds verified inflows.");
            }
            return new InventoryState(catalog, fresh._pickups, consumed, packState, shipState,
                packWeightGrams, packSlots, shipWeightGrams, shipSlots, creditMap);
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
            foreach (var entry in SnapshotCredited())
            {
                Add("credit"); Add(entry.Key.Value);
                Add(entry.Value.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(canonical.ToString());
        }


        /// <summary>
        /// Pure atomic exchange primitive. The return value cannot mutate
        /// WorldState by itself: only a validated command may commit it.
        /// Generated inputs are added to a monotonically persisted inflow
        /// ledger for future save validation.
        /// </summary>
        public InventoryTransactionResult TryExchangeShip(
            IEnumerable<KeyValuePair<EntityId, long>> spend,
            IEnumerable<KeyValuePair<EntityId, long>> grant)
        {
            if (spend == null) throw new ArgumentNullException(nameof(spend));
            if (grant == null) throw new ArgumentNullException(nameof(grant));
            var removed = spend.ToArray();
            var added = grant.ToArray();
            if (removed.Length == 0 && added.Length == 0)
                return Reject(InventoryError.InvalidQuantity);

            bool Validate(KeyValuePair<EntityId, long>[] rows)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var row in rows)
                {
                    if (!row.Key.IsValid || row.Value <= 0)
                        return false;
                    if (!seen.Add(row.Key.Value)) return false;
                }
                return true;
            }
            if (!Validate(removed) || !Validate(added))
                return Reject(InventoryError.InvalidQuantity);
            foreach (var row in removed.Concat(added))
                if (_catalog.Get(row.Key) == null)
                    return Reject(InventoryError.UnknownItem);

            var ship = new Dictionary<string, long>(_ship, StringComparer.Ordinal);
            foreach (var row in removed)
            {
                if (!ship.TryGetValue(row.Key.Value, out var quantity) ||
                    quantity < row.Value)
                    return Reject(InventoryError.InsufficientStock);
                if (quantity == row.Value) ship.Remove(row.Key.Value);
                else ship[row.Key.Value] = quantity - row.Value;
            }
            foreach (var row in added)
            {
                ship[row.Key.Value] = checked((ship.TryGetValue(row.Key.Value,
                    out var quantity) ? quantity : 0L) + row.Value);
            }
            var limit = CheckLimit(ship, ShipMaxWeightGrams, ShipMaxSlots);
            if (limit != InventoryError.None) return Reject(limit);

            var credited = new Dictionary<string, long>(_credited, StringComparer.Ordinal);
            foreach (var row in added)
            {
                credited[row.Key.Value] = checked((credited.TryGetValue(row.Key.Value,
                    out var old) ? old : 0L) + row.Value);
            }

            return Accepted(new InventoryState(_catalog, _pickups, _collected,
                _pack, ship, PackMaxWeightGrams, PackMaxSlots,
                ShipMaxWeightGrams, ShipMaxSlots, credited));
        }

        public IReadOnlyList<KeyValuePair<EntityId, long>> SnapshotCredited() =>
            Array.AsReadOnly(_credited.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new KeyValuePair<EntityId, long>(
                    EntityId.Parse(p.Key), p.Value)).ToArray());

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
