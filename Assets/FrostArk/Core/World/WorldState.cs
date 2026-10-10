using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

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

        public WorldMinute Minute { get; }
        public long Revision { get; }

        private WorldState(WorldMinute minute, long revision,
            Dictionary<string, long> supplies, HashSet<string> crewIds,
            Dictionary<string, string> crewStations, HashSet<string> processedCommands)
        {
            Minute = minute;
            Revision = revision;
            _supplies = supplies;
            _crewIds = crewIds;
            _crewStations = crewStations;
            _processedCommands = processedCommands;
        }

        public static WorldState Create(WorldMinute minute,
            IEnumerable<SupplyStock> supplies, IEnumerable<EntityId> crew)
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

            return new WorldState(minute, 0, supplyMap, crews,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal));
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
            Dictionary<string, long> supplies, Dictionary<string, string> crewStations)
        {
            long nextRevision = checked(Revision + 1);
            var commands = new HashSet<string>(_processedCommands, StringComparer.Ordinal);
            commands.Add(commandId);
            return new WorldState(Minute, nextRevision, supplies,
                _crewIds, crewStations, commands);
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
            return StableHash32.Of(canonical.ToString());
        }
    }
}