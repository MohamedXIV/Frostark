using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Crew;
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

        /// <summary>
        /// Validate standing cells for the actual authored CrewInteractionSlots
        /// whose SourceId resolves to a placed ShipModule. A slot's LocalX/Y
        /// is relative to that module origin in its room; an unplaced fixture
        /// source is ignored, never guessed. This checks *physical standing*
        /// clearance, not door connectivity, approach/arrival or slot ownership.
        /// Build placement uses the conservative all-solid footprint until
        /// spatial profiles are part of the canonical layout/content contract.
        /// </summary>
        public bool PreservesModuleInteractionSlots(ShipLayoutState layout,
            CrewInteractionState interactions)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            if (interactions == null) throw new ArgumentNullException(nameof(interactions));
            foreach (var slot in interactions.Slots)
            {
                var source = layout.GetModule(slot.SourceId);
                if (source == null) continue;
                var cell = new ShipGridCell(source.RoomId,
                    source.X + slot.LocalX, source.Y + slot.LocalY);
                if (!Walkable(cell)) return false;
            }
            return true;
        }

        // Orthogonal = 10, diagonal = 14. The extra four diagonals are
        // legal only when BOTH adjacent cardinal cells are walkable, so a
        // character never clips through a blocked module/bulkhead corner.
        private bool Traversable(ShipGridCell cell, HashSet<ShipGridCell> temporarilyBlocked) =>
            Walkable(cell) &&
            (temporarilyBlocked == null || !temporarilyBlocked.Contains(cell));

        private IEnumerable<(ShipGridCell Cell, int Cost)> Neighbors(
            ShipGridCell from, HashSet<ShipGridCell> temporarilyBlocked)
        {
            foreach (var (dx, dy) in new[]
            {
                (0, 1), (1, 0), (0, -1), (-1, 0),
                (1, 1), (1, -1), (-1, -1), (-1, 1)
            })
            {
                var next = new ShipGridCell(from.RoomId, from.X + dx, from.Y + dy);
                if (!Traversable(next, temporarilyBlocked)) continue;
                if (dx != 0 && dy != 0 &&
                    (!Traversable(new ShipGridCell(from.RoomId, from.X + dx, from.Y),
                        temporarilyBlocked) ||
                     !Traversable(new ShipGridCell(from.RoomId, from.X, from.Y + dy),
                        temporarilyBlocked)))
                    continue;
                yield return (next, dx != 0 && dy != 0 ? 14 : 10);
            }
            foreach (var portal in _portals)
            {
                if (!portal.Open) continue;
                if (portal.A == from && Traversable(portal.B, temporarilyBlocked))
                    yield return (portal.B, 10);
                if (portal.B == from && Traversable(portal.A, temporarilyBlocked))
                    yield return (portal.A, 10);
            }
        }

        // Same-room octile distance is admissible ONLY when the route graph
        // has no arbitrary cross-room shortcuts. Otherwise zero remains safe.
        private int Heuristic(ShipGridCell cell, ShipGridCell target)
        {
            if (_portals.Length != 0 || cell.RoomId != target.RoomId) return 0;
            int dx = Math.Abs(cell.X - target.X);
            int dy = Math.Abs(cell.Y - target.Y);
            return 10 * Math.Max(dx, dy) + 4 * Math.Min(dx, dy);
        }

        /// <summary>
        /// Conservative grid-supercover line check between two cell centers.
        /// The diagonal branch tests both adjacent cells to prevent smoothing
        /// through the corner of a wall. Never smooth across room portals.
        /// </summary>
        public bool CanTraverseStraight(ShipGridCell from, ShipGridCell to)
        {
            if (from.RoomId != to.RoomId || !Walkable(from) || !Walkable(to))
                return false;
            int x = from.X, y = from.Y;
            int dx = to.X - x, dy = to.Y - y;
            int nx = Math.Abs(dx), ny = Math.Abs(dy);
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            int ix = 0, iy = 0;
            while (ix < nx || iy < ny)
            {
                long horizontal = (1L + 2L * ix) * ny;
                long vertical = (1L + 2L * iy) * nx;
                if (horizontal == vertical)
                {
                    if (!Walkable(new ShipGridCell(from.RoomId, x + sx, y)) ||
                        !Walkable(new ShipGridCell(from.RoomId, x, y + sy)))
                        return false;
                    x += sx; y += sy; ix++; iy++;
                }
                else if (horizontal < vertical)
                {
                    x += sx; ix++;
                }
                else
                {
                    y += sy; iy++;
                }
                if (!Walkable(new ShipGridCell(from.RoomId, x, y))) return false;
            }
            return true;
        }

        private static int Compare(ShipGridCell a, ShipGridCell b)
        {
            int c = string.CompareOrdinal(a.RoomId.Value, b.RoomId.Value);
            if (c != 0) return c;
            c = a.Y.CompareTo(b.Y);
            return c != 0 ? c : a.X.CompareTo(b.X);
        }

        public IReadOnlyList<ShipGridCell> FindPath(ShipGridCell start, ShipGridCell goal,
            IEnumerable<ShipGridCell> temporarilyBlocked = null)
        {
            var blocked = temporarilyBlocked == null ? null :
                new HashSet<ShipGridCell>(temporarilyBlocked);
            if (!Walkable(start) || !Traversable(goal, blocked))
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
                foreach (var edge in Neighbors(current, blocked))
                {
                    var next = edge.Cell;
                    if (closed.Contains(next)) continue;
                    int candidate = g[current] + edge.Cost;
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
