using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    public sealed class ThermalConnection
    {
        public EntityId A { get; }
        public EntityId B { get; }
        public int Conductance { get; }

        public ThermalConnection(EntityId a, EntityId b, int conductance)
        {
            if (!a.IsValid || !b.IsValid || a == b)
                throw new ArgumentException("Heat links require two distinct room IDs.");
            if (conductance < 0 || conductance > 100_000)
                throw new ArgumentOutOfRangeException(nameof(conductance));
            if (StringComparer.Ordinal.Compare(a.Value, b.Value) < 0)
            {
                A = a; B = b;
            }
            else
            {
                A = b; B = a;
            }
            Conductance = conductance;
        }
    }

    public sealed class BoilerConfiguration
    {
        public static BoilerConfiguration Disabled => new BoilerConfiguration(default, 0, 0);

        public EntityId RoomId { get; }
        public long FuelUnitsPerMinute { get; }
        public long EnergyPerFuelUnit { get; }
        public bool IsEnabled => RoomId.IsValid;

        public BoilerConfiguration(EntityId roomId, long fuelUnitsPerMinute, long energyPerFuelUnit)
        {
            if (fuelUnitsPerMinute < 0)
                throw new ArgumentOutOfRangeException(nameof(fuelUnitsPerMinute));
            if (energyPerFuelUnit < 0)
                throw new ArgumentOutOfRangeException(nameof(energyPerFuelUnit));
            if (roomId.IsValid && (fuelUnitsPerMinute <= 0 || energyPerFuelUnit <= 0))
                throw new ArgumentException("Enabled boiler needs a positive burn rate and heat yield.");
            if (!roomId.IsValid && (fuelUnitsPerMinute != 0 || energyPerFuelUnit != 0))
                throw new ArgumentException("Disabled boiler must not consume or create heat.");
            RoomId = roomId;
            FuelUnitsPerMinute = fuelUnitsPerMinute;
            EnergyPerFuelUnit = energyPerFuelUnit;
        }
    }

    /// <summary>
    /// Immutable snapshot containing rooms and sorted undirected edges.
    /// All mutable thermal truth must be embedded in canonical WorldState.
    /// </summary>
    public sealed class ShipThermalState
    {
        private readonly ThermalCompartment[] _rooms;
        private readonly ThermalConnection[] _edges;

        public BoilerConfiguration Boiler { get; }
        public IReadOnlyList<ThermalCompartment> Rooms => Array.AsReadOnly(_rooms);
        public IReadOnlyList<ThermalConnection> Connections => Array.AsReadOnly(_edges);
        public long TotalEnergy
        {
            get
            {
                long sum = 0;
                foreach (var room in _rooms) sum = checked(sum + room.Energy);
                return sum;
            }
        }

        private ShipThermalState(ThermalCompartment[] rooms,
            ThermalConnection[] edges, BoilerConfiguration boiler)
        {
            _rooms = rooms;
            _edges = edges;
            Boiler = boiler;
        }

        public static ShipThermalState Empty => Create(Array.Empty<ThermalCompartment>(),
            Array.Empty<ThermalConnection>(), BoilerConfiguration.Disabled);

        public static ShipThermalState Create(IEnumerable<ThermalCompartment> rooms,
            IEnumerable<ThermalConnection> edges, BoilerConfiguration boiler)
        {
            if (rooms == null) throw new ArgumentNullException(nameof(rooms));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            if (boiler == null) throw new ArgumentNullException(nameof(boiler));
            var array = rooms.ToArray();
            if (array.Any(x => x == null))
                throw new ArgumentException("Null compartment.", nameof(rooms));
            array = array.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < array.Length; i++)
                if (array[i].Id == array[i-1].Id)
                    throw new ArgumentException("Duplicate room ID.", nameof(rooms));
            var ids = new HashSet<string>(array.Select(x => x.Id.Value), StringComparer.Ordinal);
            if (boiler.IsEnabled && !ids.Contains(boiler.RoomId.Value))
                throw new ArgumentException("Boiler room is not present.", nameof(boiler));

            var links = edges.ToArray();
            if (links.Any(x => x == null))
                throw new ArgumentException("Null connection.", nameof(edges));
            links = links.OrderBy(x => x.A.Value, StringComparer.Ordinal)
                .ThenBy(x => x.B.Value, StringComparer.Ordinal).ToArray();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in links)
            {
                if (!ids.Contains(link.A.Value) || !ids.Contains(link.B.Value) ||
                    !seen.Add(link.A.Value + "\0" + link.B.Value))
                    throw new ArgumentException("Missing room or duplicate edge.", nameof(edges));
            }
            return new ShipThermalState(array, links, boiler);
        }

        public ThermalCompartment Get(EntityId id)
        {
            if (!id.IsValid) return null;
            return Array.Find(_rooms, room => room.Id == id);
        }
    }
}
