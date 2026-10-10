using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Ship;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Canonical immutable-on-commit world aggregate for early R0 systems.
    /// Collections never escape to consumers. A successful command creates a
    /// new revision; a rejected command retains the identical prior object.
    /// </summary>
    public sealed class WorldState
    {
        private readonly Dictionary<string, long> _supplies;
        private readonly HashSet<string> _crewIds;
        private readonly Dictionary<string, string> _crewStations;
        private readonly HashSet<string> _processedCommands;
        public InventoryState Inventory { get; }
        public ShipThermalState Thermal { get; }
        public CrewState CrewDuty { get; }
        public ShipLayoutState Layout { get; }

        public WorldMinute Minute { get; }
        public long Revision { get; }

        private WorldState(WorldMinute minute, long revision,
            Dictionary<string, long> supplies, HashSet<string> crewIds,
            Dictionary<string, string> crewStations, HashSet<string> processedCommands,
            InventoryState inventory, ShipThermalState thermal, CrewState crewDuty, ShipLayoutState layout)
        {
            Minute = minute;
            Revision = revision;
            _supplies = supplies;
            _crewIds = crewIds;
            _crewStations = crewStations;
            _processedCommands = processedCommands;
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
            CrewDuty = crewDuty ?? throw new ArgumentNullException(nameof(crewDuty));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public static WorldState Create(WorldMinute minute,
            IEnumerable<SupplyStock> supplies, IEnumerable<EntityId> crew,
            InventoryState inventory = null, ShipThermalState thermal = null,
            CrewState? crewDuty = null, ShipLayoutState? layout = null)
        {
            if (supplies == null)
                throw new ArgumentNullException(nameof(supplies));
            if (crew == null)
                throw new ArgumentNullException(nameof(crew));

            var supplyMap = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var supply in supplies)
            {
                if (!supply.Id.IsValid || supply.Quantity < 0 ||
                    !supplyMap.TryAdd(supply.Id.Value, supply.Quantity))
                    throw new ArgumentException("Supply identities must be unique and non-negative.", nameof(supplies));
            }

            var crews = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in crew)
            {
                if (!member.IsValid || !crews.Add(member.Value))
                    throw new ArgumentException("Crew identities must be valid and unique.", nameof(crew));
            }

            var duty = crewDuty ?? CrewState.Empty;
            foreach (var member in duty.Members)
                if (!crews.Contains(member.Id.Value))
                    throw new ArgumentException("Authored crew member is absent from world registry.", nameof(crewDuty));

            return new WorldState(minute, 0, supplyMap, crews,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal), inventory ?? InventoryState.Empty,
                thermal ?? ShipThermalState.Empty, duty, layout ?? ShipLayoutState.Empty);
        }

        public bool HasSupply(EntityId id) => id.IsValid && _supplies.ContainsKey(id.Value);
        public long GetSupply(EntityId id) =>
            id.IsValid && _supplies.TryGetValue(id.Value, out var amount) ? amount : 0;
        public bool HasCrew(EntityId id) => id.IsValid && _crewIds.Contains(id.Value);

        public EntityId GetCrewStation(EntityId crewId) =>
            crewId.IsValid && _crewStations.TryGetValue(crewId.Value, out var station)
                ? EntityId.Parse(station) : default;

        internal bool HasProcessed(EntityId commandId) =>
            commandId.IsValid && _processedCommands.Contains(commandId.Value);

        private WorldState CopyWithCommand(string commandId,
            Dictionary<string, long> supplies, Dictionary<string, string> crewStations,
            InventoryState inventory = null, CrewState? crewDuty = null, ShipLayoutState? layout = null)
        {
            long nextRevision = checked(Revision + 1);
            var commands = new HashSet<string>(_processedCommands, StringComparer.Ordinal);
            commands.Add(commandId);
            return new WorldState(Minute, nextRevision, supplies,
                _crewIds, crewStations, commands, inventory ?? Inventory, Thermal,
                crewDuty ?? CrewDuty, layout ?? Layout);
        }

        internal WorldState WithWithdrawal(EntityId supplyId, long amount, EntityId commandId)
        {
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            supplies[supplyId.Value] = checked(supplies[supplyId.Value] - amount);
            return CopyWithCommand(commandId.Value, supplies, _crewStations);
        }

        internal WorldState WithAssignment(EntityId crewId, EntityId stationId, EntityId commandId)
        {
            var stations = new Dictionary<string, string>(_crewStations, StringComparer.Ordinal);
            stations.Add(crewId.Value, stationId.Value);
            return CopyWithCommand(commandId.Value, _supplies, stations);
        }

        internal WorldState WithUnassignment(EntityId crewId, EntityId commandId)
        {
            var stations = new Dictionary<string, string>(_crewStations, StringComparer.Ordinal);
            stations.Remove(crewId.Value);
            return CopyWithCommand(commandId.Value, _supplies, stations);
        }


        internal WorldState WithInventoryChange(InventoryState inventory, EntityId commandId)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations, inventory);
        }

        internal WorldState WithCrewAway(EntityId crewId, bool away, EntityId commandId) =>
            CopyWithCommand(commandId.Value, _supplies, _crewStations,
                crewDuty: CrewDuty.SetAway(crewId, away));

        internal WorldState WithShipLayoutChange(ShipLayoutState layout,
            InventoryState inventory, CrewState crewDuty, EntityId commandId) =>
            CopyWithCommand(commandId.Value, _supplies, _crewStations, inventory, crewDuty, layout);

        // Crew phase runs before Heat and does not advance world time itself.
        // The heat adapter commits the same minute once after all crew effects.
        internal WorldState WithCrewTick(WorldMinute minute, CrewState nextCrew)
        {
            if (nextCrew == null) throw new ArgumentNullException(nameof(nextCrew));
            if (minute.Value != checked(Minute.Value + 1))
                throw new ArgumentOutOfRangeException(nameof(minute));
            return new WorldState(Minute, Revision, _supplies, _crewIds,
                _crewStations, _processedCommands, Inventory, Thermal, nextCrew, Layout);
        }

        /// <summary>
        /// Commit one *simulation* minute without manufacturing a command ID or
        /// incrementing a player-command revision. Existing command/replay
        /// provenance remains bounded across weeks of thermal ticks.
        /// </summary>
        internal WorldState WithThermalMinute(WorldMinute minute, ThermalTickResult tick)
        {
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            if (minute.Value != checked(Minute.Value + 1))
                throw new ArgumentOutOfRangeException(nameof(minute), "Thermal steps require consecutive minutes.");
            if (Thermal.Rooms.Count == 0)
                throw new InvalidOperationException("Cannot apply thermal step to an empty ship.");
            if (tick.FuelConsumed < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            var fuelId = EntityId.Parse("supply:fuel");
            var amount = GetSupply(fuelId);
            if (tick.FuelConsumed > amount)
                throw new InvalidOperationException("Boiler cannot burn fuel not held by canonical WorldState.");
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            if (tick.FuelConsumed > 0)
                supplies[fuelId.Value] = checked(amount - tick.FuelConsumed);
            return new WorldState(minute, Revision, supplies, _crewIds,
                _crewStations, _processedCommands, Inventory, tick.State, CrewDuty, Layout);
        }

        /// <summary>
        /// Explicit world-minute projection; commands remain revision controlled.
        /// No existing snapshot is mutated by advancing the authoritative clock.
        /// </summary>
        public WorldState AtMinute(WorldMinute minute)
        {
            if (minute < Minute)
                throw new ArgumentOutOfRangeException(nameof(minute), "World time cannot go backwards.");
            return new WorldState(minute, Revision, _supplies, _crewIds, _crewStations, _processedCommands, Inventory, Thermal, CrewDuty, Layout);
        }

        internal IReadOnlyList<SupplyStock> SnapshotSupplies() =>
            _supplies.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new SupplyStock(EntityId.Parse(p.Key), p.Value)).ToArray();

        internal IReadOnlyList<EntityId> SnapshotCrew() =>
            _crewIds.OrderBy(s => s, StringComparer.Ordinal).Select(EntityId.Parse).ToArray();

        internal IReadOnlyList<KeyValuePair<EntityId, EntityId>> SnapshotAssignments() =>
            _crewStations.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new KeyValuePair<EntityId, EntityId>(
                    EntityId.Parse(p.Key), EntityId.Parse(p.Value))).ToArray();

        internal IReadOnlyList<EntityId> SnapshotProcessedCommands() =>
            _processedCommands.OrderBy(s => s, StringComparer.Ordinal).Select(EntityId.Parse).ToArray();

        internal static WorldState RestoreSnapshot(WorldMinute minute, long revision,
            IEnumerable<SupplyStock> supplies, IEnumerable<EntityId> crew,
            IEnumerable<KeyValuePair<EntityId, EntityId>> assignments,
            IEnumerable<EntityId> processed, InventoryState inventory = null,
            ShipThermalState thermal = null, CrewState? crewDuty = null, ShipLayoutState? layout = null)
        {
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            var baseline = Create(minute, supplies, crew, crewDuty: crewDuty, layout: layout);
            var stations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in assignments ?? throw new ArgumentNullException(nameof(assignments)))
            {
                if (!baseline.HasCrew(pair.Key) || !pair.Value.IsValid ||
                    !stations.TryAdd(pair.Key.Value, pair.Value.Value))
                    throw new ArgumentException("Invalid or duplicate crew assignment.", nameof(assignments));
            }

            var commands = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in processed ?? throw new ArgumentNullException(nameof(processed)))
            {
                if (!id.IsValid || !commands.Add(id.Value))
                    throw new ArgumentException("Duplicate or invalid command provenance.", nameof(processed));
            }
            if (commands.Count != revision)
                throw new ArgumentException("Revision and processed command count disagree.", nameof(revision));
            return new WorldState(minute, revision, baseline._supplies,
                baseline._crewIds, stations, commands, inventory ?? InventoryState.Empty,
                thermal ?? ShipThermalState.Empty, baseline.CrewDuty, baseline.Layout);
        }

        /// <summary>
        /// Stable FNV-1a replay checkpoint, NOT a cryptographic signature or a
        /// persistence substitute. Length prefixes avoid ambiguous delimiters.
        /// </summary>
        public uint Fingerprint()
        {
            var canonical = new StringBuilder();

            void Add(string part)
            {
                canonical.Append(part.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(part);
            }

            Add(Minute.Value.ToString(CultureInfo.InvariantCulture));
            Add(Revision.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in _supplies.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Add("s"); Add(pair.Key); Add(pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var id in _crewIds.OrderBy(s => s, StringComparer.Ordinal))
            {
                Add("c"); Add(id);
            }
            foreach (var pair in _crewStations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Add("j"); Add(pair.Key); Add(pair.Value);
            }
            foreach (var id in _processedCommands.OrderBy(s => s, StringComparer.Ordinal))
            {
                Add("x"); Add(id);
            }
            Add("thermal");
            Add(Thermal.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("inventory");
            Add(Inventory.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("crew-duty");
            Add(CrewDuty.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("ship-layout"); Add(Layout.Fingerprint().ToString(CultureInfo.InvariantCulture));
            return StableHash32.Of(canonical.ToString());
        }
    }
}