using System;
using FrostArk.Core.World;
namespace FrostArk.Core.Ship
{
    public enum ShipModuleKind { Engine = 0, Boiler = 1, Auxiliary = 2 }
    public sealed class ShipCompartmentSlot
    {
        public EntityId Id { get; }
        public int Width { get; }
        public int Height { get; }
        public ShipCompartmentSlot(EntityId id, int width, int height)
        {
            if (!id.IsValid) throw new ArgumentException("Room ID required.", nameof(id));
            if (width < 1 || width > 64 || height < 1 || height > 64)
                throw new ArgumentOutOfRangeException(nameof(width));
            Id = id; Width = width; Height = height;
        }
    }
    public sealed class ShipModuleBlueprint
    {
        public EntityId Id { get; }
        public ShipModuleKind Kind { get; }
        public int Width { get; }
        public int Height { get; }
        public EntityId PartId { get; }
        public long BuildCost { get; }
        public long RepairCost { get; }
        public ShipModuleBlueprint(EntityId id, ShipModuleKind kind, int width, int height,
            EntityId partId, long buildCost, long repairCost)
        {
            if (!id.IsValid || !partId.IsValid) throw new ArgumentException("IDs required.");
            if (!Enum.IsDefined(typeof(ShipModuleKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (width < 1 || width > 64 || height < 1 || height > 64)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (buildCost <= 0 || repairCost <= 0)
                throw new ArgumentOutOfRangeException(nameof(buildCost));
            Id = id; Kind = kind; Width = width; Height = height;
            PartId = partId; BuildCost = buildCost; RepairCost = repairCost;
        }
    }
    public sealed class ShipModule
    {
        public EntityId Id { get; }
        public EntityId BlueprintId { get; }
        public EntityId RoomId { get; }
        public int X { get; }
        public int Y { get; }
        public int IntegrityPermille { get; }
        public bool RepairReserved { get; }
        public ShipModule(EntityId id, EntityId blueprintId, EntityId roomId,
            int x, int y, int integrityPermille = 1000)
            : this(id, blueprintId, roomId, x, y, integrityPermille, false) { }

        // Only internal command or vetted save hydration code may set this flag.
        internal ShipModule(EntityId id, EntityId blueprintId, EntityId roomId,
            int x, int y, int integrityPermille, bool repairReserved)
        {
            if (!id.IsValid || !blueprintId.IsValid || !roomId.IsValid)
                throw new ArgumentException("Module IDs required.");
            if (x < 0 || y < 0 || integrityPermille < 0 || integrityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(x));
            if (repairReserved && integrityPermille == 1000)
                throw new ArgumentException("Cannot reserve undamaged module.");
            Id = id; BlueprintId = blueprintId; RoomId = roomId; X = x; Y = y;
            IntegrityPermille = integrityPermille; RepairReserved = repairReserved;
        }
        internal ShipModule WithIntegrity(int integrity) =>
            new ShipModule(Id, BlueprintId, RoomId, X, Y, integrity, RepairReserved);
        internal ShipModule WithReservation() =>
            new ShipModule(Id, BlueprintId, RoomId, X, Y, IntegrityPermille, true);
        internal ShipModule Repaired() =>
            new ShipModule(Id, BlueprintId, RoomId, X, Y, 1000);
    }
}