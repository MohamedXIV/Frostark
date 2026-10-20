using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Small, authoring-driven ship-local A* navigator. This is deliberately NOT
    /// sea-route ShipNavigationState or shore NavMesh. The fixture grid is local
    /// presentation data, not a second canonical crew inventory/Needs store.
    /// </summary>
    public sealed class CrewShipGridPath
    {
        private readonly bool[,] _blocked;
        private static readonly Vector2Int[] Neighbors =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
        };

        public int Width { get; }
        public int Height { get; }

        public CrewShipGridPath(int width, int height, IEnumerable<Vector2Int> blocked)
        {
            if (width <= 0 || height <= 0 || width > 128 || height > 128)
                throw new ArgumentOutOfRangeException(nameof(width));
            Width = width;
            Height = height;
            _blocked = new bool[width, height];
            if (blocked == null) throw new ArgumentNullException(nameof(blocked));
            foreach (var cell in blocked)
            {
                if (!Contains(cell))
                    throw new ArgumentException("Blocked grid cell is outside the ship.", nameof(blocked));
                _blocked[cell.x, cell.y] = true;
            }
        }

        public bool Contains(Vector2Int cell) =>
            cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public bool CanStand(Vector2Int cell) =>
            Contains(cell) && !_blocked[cell.x, cell.y];

        public Vector3 WorldCenter(Vector2Int cell, float height = 0f) =>
            new Vector3(cell.x - (Width - 1) * 0.5f, height,
                cell.y - (Height - 1) * 0.5f);

        public IReadOnlyList<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
        {
            if (!CanStand(start) || !CanStand(goal))
                return Array.Empty<Vector2Int>();
            if (start == goal) return new[] { start };

            var open = new List<Vector2Int> { start };
            var closed = new HashSet<Vector2Int>();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };

            while (open.Count > 0)
            {
                // Deterministic tie breaking prevents branch-dependent routing.
                int winner = 0;
                for (int i = 1; i < open.Count; i++)
                {
                    var a = open[i];
                    var b = open[winner];
                    int fa = gScore[a] + Manhattan(a, goal);
                    int fb = gScore[b] + Manhattan(b, goal);
                    if (fa < fb || (fa == fb &&
                        (gScore[a] < gScore[b] ||
                         (gScore[a] == gScore[b] &&
                          (a.y < b.y || (a.y == b.y && a.x < b.x))))))
                        winner = i;
                }

                var current = open[winner];
                open.RemoveAt(winner);
                if (current == goal)
                {
                    var route = new List<Vector2Int> { current };
                    while (cameFrom.TryGetValue(current, out var previous))
                    {
                        current = previous;
                        route.Add(current);
                    }
                    route.Reverse();
                    return route.AsReadOnly();
                }

                closed.Add(current);
                foreach (var offset in Neighbors)
                {
                    var next = current + offset;
                    if (!CanStand(next) || closed.Contains(next)) continue;
                    int tentative = gScore[current] + 1;
                    if (gScore.TryGetValue(next, out int previousCost) &&
                        tentative >= previousCost)
                        continue;
                    cameFrom[next] = current;
                    gScore[next] = tentative;
                    if (!open.Contains(next)) open.Add(next);
                }
            }

            return Array.Empty<Vector2Int>();
        }

        private static int Manhattan(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
