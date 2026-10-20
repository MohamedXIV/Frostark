using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    /// <summary>
    /// Immutable derived room-local navigation/read-only build-clearance map.
    /// ShipLayoutState still owns modules and placements. Rebuild the projection
    /// when a module, door or authoring profile changes; it stores no shadow
    /// inventory, crew positions or simulation clock.
    /// </summary>
    public sealed class ShipSpatialMap
    {
        private readonly Dictionary<EntityId, ShipCompartmentSlot> _rooms;
        private readonly HashSet<ShipGridCell> _solid;
        private readonly HashSet<ShipGridCell> _constructionReserved;
        private readonly Dictionary<EntityId, bool> _moduleClearanceOpen;
        private readonly ShipRoomPortal[] _portals;

        private ShipSpatialMap(Dictionary<EntityId, ShipCompartmentSlot> rooms,
            HashSet<ShipGridCell> solid, HashSet<ShipGridCell> reserved,
            Dictionary<EntityId, bool> clearanceOpen, ShipRoomPortal[] portals)
        {
            _rooms = rooms;
            _solid = solid;
            _constructionReserved = reserved;
            _moduleClearanceOpen = clearanceOpen;
            _portals = portals;
        }

        public static ShipSpatialMap Derive(ShipLayoutState layout,
            IEnumerable<ShipModuleSpatialProfile> authoredProfiles,
            IEnumerable<ShipRoomPortal> authoredPortals)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (authoredProfiles == null) throw new ArgumentNullException(nameof(authoredProfiles));
            if (authoredPortals == null) throw new ArgumentNullException(nameof(authoredPortals));
            var rooms = layout.Rooms.ToDictionary(x => x.Id);
            var profiles = authoredProfiles.ToArray();
            if (profiles.Any(x => x == null) ||
                profiles.Select(x => x.BlueprintId).Distinct().Count() != profiles.Length ||
                profiles.Any(x => layout.GetBlueprint(x.BlueprintId) == null))
                throw new ArgumentException("Profile references unknown/duplicate blueprint.");
            var byType = profiles.ToDictionary(x => x.BlueprintId);
            var solid = new HashSet<ShipGridCell>();
            var reserved = new HashSet<ShipGridCell>();
            var footprint = new HashSet<ShipGridCell>();
            var moduleClearances = new Dictionary<EntityId, ShipGridCell[]>();

            foreach (var module in layout.Modules)
            {
                var blueprint = layout.GetBlueprint(module.BlueprintId);
                var room = rooms[module.RoomId];
                for (int x = 0; x < blueprint.Width; x++)
                    for (int y = 0; y < blueprint.Height; y++)
                    {
                        var cell = new ShipGridCell(room.Id, module.X + x, module.Y + y);
                        footprint.Add(cell);
                        reserved.Add(cell);
                    }
                if (byType.TryGetValue(module.BlueprintId, out var profile))
                {
                    foreach (var offset in profile.Solid)
                    {
                        if (offset.X < 0 || offset.Y < 0 ||
                            offset.X >= blueprint.Width || offset.Y >= blueprint.Height)
                            throw new ArgumentException("Solid offsets must lie inside the module footprint.");
                        solid.Add(new ShipGridCell(room.Id,
                            module.X + offset.X, module.Y + offset.Y));
                    }
                    var clearances = new List<ShipGridCell>();
                    foreach (var offset in profile.Clearance)
                    {
                        var cell = new ShipGridCell(room.Id,
                            module.X + offset.X, module.Y + offset.Y);
                        if (!Inside(rooms, cell))
                            throw new ArgumentException("Interaction clearance exceeds the room boundary.");
                        reserved.Add(cell);
                        clearances.Add(cell);
                    }
                    moduleClearances.Add(module.Id, clearances.ToArray());
                }
                else
                {
                    // No authored walkable footprint: don't infer that a
                    // boiler/engine machine is transparent to walking crew.
                    for (int x = 0; x < blueprint.Width; x++)
                        for (int y = 0; y < blueprint.Height; y++)
                            solid.Add(new ShipGridCell(room.Id, module.X + x, module.Y + y));
                    moduleClearances.Add(module.Id, Array.Empty<ShipGridCell>());
                }
            }

            var validClearance = new Dictionary<EntityId, bool>();
            foreach (var item in moduleClearances)
                validClearance.Add(item.Key,
                    item.Value.All(cell => !footprint.Contains(cell) && !solid.Contains(cell)));

            var portals = authoredPortals.ToArray();
            if (portals.Any(x => x == null) ||
                portals.Select(x => x.Id).Distinct().Count() != portals.Length)
                throw new ArgumentException("Duplicate or null ship portal.");
            foreach (var portal in portals)
                if (!Inside(rooms, portal.A) || !Inside(rooms, portal.B))
                    throw new ArgumentException("Door/ladder endpoint lies outside an authored compartment.");
            portals = portals.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            return new ShipSpatialMap(rooms, solid, reserved, validClearance, portals);
        }

        private static bool Inside(Dictionary<EntityId, ShipCompartmentSlot> rooms,
            ShipGridCell cell) =>
            rooms.TryGetValue(cell.RoomId, out var room) &&
            cell.X >= 0 && cell.Y >= 0 && cell.X < room.Width && cell.Y < room.Height;

        public bool Contains(ShipGridCell cell) => Inside(_rooms, cell);
        public bool Walkable(ShipGridCell cell) =>
            Inside(_rooms, cell) && !_solid.Contains(cell);
        public bool BuildReserved(ShipGridCell cell) =>
            Inside(_rooms, cell) && _constructionReserved.Contains(cell);
        public bool ModuleClearanceAvailable(EntityId moduleId) =>
            _moduleClearanceOpen.TryGetValue(moduleId, out bool available) && available;

        private IEnumerable<ShipGridCell> Neighbors(ShipGridCell from)
        {
            foreach (var (dx, dy) in new[] { (0, 1), (1, 0), (0, -1), (-1, 0) })
            {
                var next = new ShipGridCell(from.RoomId, from.X + dx, from.Y + dy);
                if (Walkable(next)) yield return next;
            }
            foreach (var portal in _portals)
            {
                if (!portal.Open) continue;
                if (portal.A == from && Walkable(portal.B)) yield return portal.B;
                if (portal.B == from && Walkable(portal.A)) yield return portal.A;
            }
        }

        // A* uses an admissible zero cross-room heuristic because an authored
        // portal may shortcut arbitrary distances. Cell count is bounded by
        // ShipCompartmentSlot; optimality is more important than a guess.
        private int Heuristic(ShipGridCell cell, ShipGridCell target) =>
            _portals.Length == 0 && cell.RoomId == target.RoomId
                ? Math.Abs(cell.X - target.X) + Math.Abs(cell.Y - target.Y) : 0;

        private static int Compare(ShipGridCell a, ShipGridCell b)
        {
            int c = string.CompareOrdinal(a.RoomId.Value, b.RoomId.Value);
            if (c != 0) return c;
            c = a.Y.CompareTo(b.Y);
            return c != 0 ? c : a.X.CompareTo(b.X);
        }

        public IReadOnlyList<ShipGridCell> FindPath(ShipGridCell start, ShipGridCell goal)
        {
            if (!Walkable(start) || !Walkable(goal))
                return Array.Empty<ShipGridCell>();
            if (start == goal) return Array.AsReadOnly(new[] { start });
            var open = new List<ShipGridCell> { start };
            var closed = new HashSet<ShipGridCell>();
            var previous = new Dictionary<ShipGridCell, ShipGridCell>();
            var g = new Dictionary<ShipGridCell, int> { [start] = 0 };
            while (open.Count > 0)
            {
                int best = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    var a = open[i]; var b = open[best];
                    int fa = g[a] + Heuristic(a, goal);
                    int fb = g[b] + Heuristic(b, goal);
                    if (fa < fb || (fa == fb &&
                        (g[a] < g[b] || (g[a] == g[b] && Compare(a, b) < 0))))
                        best = i;
                }
                var current = open[best];
                open.RemoveAt(best);
                if (current == goal)
                {
                    var path = new List<ShipGridCell> { current };
                    while (previous.TryGetValue(current, out var prev))
                    {
                        path.Add(prev);
                        current = prev;
                    }
                    path.Reverse();
                    return Array.AsReadOnly(path.ToArray());
                }
                closed.Add(current);
                foreach (var next in Neighbors(current))
                {
                    if (closed.Contains(next)) continue;
                    int candidate = g[current] + 1;
                    if (g.TryGetValue(next, out var existing) && candidate >= existing)
                        continue;
                    g[next] = candidate;
                    previous[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return Array.Empty<ShipGridCell>();
        }
    }
}
