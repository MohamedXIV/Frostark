using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    /// <summary>A cell relative to one authored ship compartment, never a sea/world coordinate.</summary>
    public readonly struct ShipGridCell : IEquatable<ShipGridCell>
    {
        public EntityId RoomId { get; }
        public int X { get; }
        public int Y { get; }

        public ShipGridCell(EntityId roomId, int x, int y)
        {
            if (!roomId.IsValid) throw new ArgumentException("Valid compartment required.", nameof(roomId));
            RoomId = roomId;
            X = x; Y = y;
        }

        public bool Equals(ShipGridCell other) =>
            RoomId == other.RoomId && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ShipGridCell c && Equals(c);
        public override int GetHashCode()
        {
            unchecked
            {
                return ((RoomId.GetHashCode() * 397) ^ X) * 397 ^ Y;
            }
        }
        public static bool operator ==(ShipGridCell a, ShipGridCell b) => a.Equals(b);
        public static bool operator !=(ShipGridCell a, ShipGridCell b) => !a.Equals(b);
        public override string ToString() => RoomId.Value + ":" + X + "," + Y;
    }

    public readonly struct ShipLocalOffset
    {
        public int X { get; }
        public int Y { get; }
        public ShipLocalOffset(int x, int y) { X = x; Y = y; }
    }

    /// <summary>
    /// Per-blueprint authoring: occupied/solid parts of the construction
    /// footprint and standing clearance cells relative to its origin.
    /// Missing profiles are conservatively all-solid. Rotated modules are not
    /// supported by the existing ShipLayoutState and are not invented here.
    /// </summary>
    public sealed class ShipModuleSpatialProfile
    {
        private readonly ShipLocalOffset[] _solid;
        private readonly ShipLocalOffset[] _clearance;
        public EntityId BlueprintId { get; }
        public IReadOnlyList<ShipLocalOffset> Solid => Array.AsReadOnly(_solid);
        public IReadOnlyList<ShipLocalOffset> Clearance => Array.AsReadOnly(_clearance);

        public ShipModuleSpatialProfile(EntityId blueprintId,
            IEnumerable<ShipLocalOffset> solid, IEnumerable<ShipLocalOffset> clearance)
        {
            if (!blueprintId.IsValid)
                throw new ArgumentException("Blueprint ID required.", nameof(blueprintId));
            BlueprintId = blueprintId;
            _solid = (solid ?? throw new ArgumentNullException(nameof(solid))).ToArray();
            _clearance = (clearance ?? throw new ArgumentNullException(nameof(clearance))).ToArray();
            if (_solid.Select(x => (x.X, x.Y)).Distinct().Count() != _solid.Length ||
                _clearance.Select(x => (x.X, x.Y)).Distinct().Count() != _clearance.Length)
                throw new ArgumentException("Duplicate authored cell offset.");
        }
    }

    /// <summary>Explicit door/stair connection between two authored room-local cells.</summary>
    public sealed class ShipRoomPortal
    {
        public EntityId Id { get; }
        public ShipGridCell A { get; }
        public ShipGridCell B { get; }
        public bool Open { get; }

        public ShipRoomPortal(EntityId id, ShipGridCell a, ShipGridCell b, bool open = true)
        {
            if (!id.IsValid || a.RoomId == b.RoomId)
                throw new ArgumentException("Portal ID and two different rooms required.");
            Id = id; A = a; B = b; Open = open;
        }
    }
}
