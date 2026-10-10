using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;
namespace FrostArk.Core.Ship
{
    public sealed class ShipLayoutState
    {
        private readonly ShipCompartmentSlot[] _rooms;
        private readonly ShipModuleBlueprint[] _types;
        private readonly ShipModule[] _modules;
        public IReadOnlyList<ShipCompartmentSlot> Rooms => Array.AsReadOnly(_rooms);
        public IReadOnlyList<ShipModuleBlueprint> Blueprints => Array.AsReadOnly(_types);
        public IReadOnlyList<ShipModule> Modules => Array.AsReadOnly(_modules);
        private ShipLayoutState(ShipCompartmentSlot[] rooms,
            ShipModuleBlueprint[] types, ShipModule[] modules)
        { _rooms = rooms; _types = types; _modules = modules; }
        public static ShipLayoutState Empty => Create(Array.Empty<ShipCompartmentSlot>(),
            Array.Empty<ShipModuleBlueprint>(), Array.Empty<ShipModule>());
        public static ShipLayoutState Create(IEnumerable<ShipCompartmentSlot> rooms,
            IEnumerable<ShipModuleBlueprint> blueprints, IEnumerable<ShipModule> modules)
        {
            if (rooms == null || blueprints == null || modules == null)
                throw new ArgumentNullException(nameof(rooms));
            var rs = rooms.ToArray(); var ts = blueprints.ToArray(); var ms = modules.ToArray();
            if (rs.Any(x => x == null) || ts.Any(x => x == null) || ms.Any(x => x == null))
                throw new ArgumentException("Null layout member.");
            rs = rs.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            ts = ts.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            ms = ms.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            if (rs.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != rs.Length ||
                ts.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != ts.Length ||
                ms.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != ms.Length)
                throw new ArgumentException("Duplicate layout ID.");
            var result = new ShipLayoutState(rs, ts, ms);
            foreach (var m in ms)
                if (!result.Fits(m, ms.Where(x => x.Id != m.Id)))
                    throw new ArgumentException("Invalid module footprint.");
            return result;
        }
        public ShipModule? GetModule(EntityId id) =>
            id.IsValid ? Array.Find(_modules, x => x.Id == id) : null;
        public ShipModuleBlueprint? GetBlueprint(EntityId id) =>
            id.IsValid ? Array.Find(_types, x => x.Id == id) : null;
        private bool Fits(ShipModule m, IEnumerable<ShipModule> others)
        {
            var room = Array.Find(_rooms, x => x.Id == m.RoomId);
            var type = GetBlueprint(m.BlueprintId);
            if (room == null || type == null || m.X > room.Width - type.Width ||
                m.Y > room.Height - type.Height) return false;
            foreach (var other in others)
            {
                if (other.RoomId != m.RoomId) continue;
                var ot = GetBlueprint(other.BlueprintId);
                if (ot == null) return false;
                if (m.X < other.X + ot.Width && other.X < m.X + type.Width &&
                    m.Y < other.Y + ot.Height && other.Y < m.Y + type.Height) return false;
            }
            return true;
        }
        public bool CanPlace(EntityId id, EntityId blueprint, EntityId room, int x, int y)
        {
            if (!id.IsValid || !blueprint.IsValid || !room.IsValid || x < 0 || y < 0 ||
                GetModule(id) != null) return false;
            return Fits(new ShipModule(id, blueprint, room, x, y), _modules);
        }
        public ShipLayoutState Place(EntityId id, EntityId blueprint, EntityId room, int x, int y)
        {
            if (!CanPlace(id, blueprint, room, x, y)) throw new ArgumentException("Invalid placement.");
            return Create(_rooms, _types, _modules.Concat(new[] { new ShipModule(id, blueprint, room, x, y) }));
        }
        public ShipLayoutState Damage(EntityId id, int amount)
        {
            if (GetModule(id) == null) throw new ArgumentException("Unknown module.");
            if (amount <= 0 || amount > 1000) throw new ArgumentOutOfRangeException(nameof(amount));
            return Create(_rooms, _types, _modules.Select(m => m.Id == id ?
                m.WithIntegrity(Math.Max(0, m.IntegrityPermille - amount)) : m));
        }
        public ShipLayoutState ReserveRepair(EntityId id)
        {
            var m = GetModule(id) ?? throw new ArgumentException("Unknown module.");
            if (m.IntegrityPermille == 1000 || m.RepairReserved)
                throw new InvalidOperationException("Repair unavailable.");
            return Create(_rooms, _types, _modules.Select(x => x.Id == id ? x.WithReservation() : x));
        }
        public ShipLayoutState CompleteRepair(EntityId id)
        {
            var m = GetModule(id);
            if (m == null || !m.RepairReserved) return this;
            return Create(_rooms, _types, _modules.Select(x => x.Id == id ? x.Repaired() : x));
        }
        private int Efficiency(ShipModuleKind kind)
        {
            var values = _modules.Where(m => GetBlueprint(m.BlueprintId)!.Kind == kind)
                .Select(m => m.IntegrityPermille).ToArray();
            return values.Length == 0 ? 1000 : values.Min();
        }
        public int PropulsionPermille => Efficiency(ShipModuleKind.Engine);
        public int BoilerOutputPermille => Math.Min(PropulsionPermille, Efficiency(ShipModuleKind.Boiler));
        public uint Fingerprint()
        {
            var b = new StringBuilder();
            void Add(string s) => b.Append(s.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(s);
            foreach (var r in _rooms) { Add("room"); Add(r.Id.Value); Add(r.Width.ToString(CultureInfo.InvariantCulture)); Add(r.Height.ToString(CultureInfo.InvariantCulture)); }
            foreach (var t in _types) { Add("type"); Add(t.Id.Value); Add(((int)t.Kind).ToString(CultureInfo.InvariantCulture)); Add(t.Width.ToString(CultureInfo.InvariantCulture)); Add(t.Height.ToString(CultureInfo.InvariantCulture)); Add(t.PartId.Value); Add(t.BuildCost.ToString(CultureInfo.InvariantCulture)); Add(t.RepairCost.ToString(CultureInfo.InvariantCulture)); }
            foreach (var m in _modules) { Add("module"); Add(m.Id.Value); Add(m.BlueprintId.Value); Add(m.RoomId.Value); Add(m.X.ToString(CultureInfo.InvariantCulture)); Add(m.Y.ToString(CultureInfo.InvariantCulture)); Add(m.IntegrityPermille.ToString(CultureInfo.InvariantCulture)); Add(m.RepairReserved ? "1" : "0"); }
            return StableHash32.Of(b.ToString());
        }
    }
}