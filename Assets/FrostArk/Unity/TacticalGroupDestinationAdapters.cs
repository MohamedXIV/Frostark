using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Geometry-specific goal candidate adapters for the ONE shared group
    /// allocator. No new order authority or route executor is created here.
    /// </summary>
    public static class TacticalGroupDestinationAdapters
    {
        /// <summary>
        /// Adaptively allocate distinct reachable cells around a ship target.
        /// Cell parking is transient: do not mix it with module build-clearance
        /// or authoritative work-slot claims. Moving traffic is owned by #113.
        /// </summary>
        public static IReadOnlyDictionary<EntityId, CrewMoveGoal> PlanShipGoals(
            ShipSpatialMap map, IReadOnlyList<EntityId> ids,
            IEnumerable<ShipCrewSelectionProxy> actors, ShipGridCell center)
        {
            var selected = new HashSet<EntityId>(ids);
            var positions = actors.Where(a => a != null && a.Walker != null &&
                a.Walker.IsConfigured && a.isActiveAndEnabled)
                .GroupBy(a => a.ActorId).ToDictionary(g => g.Key, g => g.First().Walker);
            var reserved = new HashSet<ShipGridCell>(actors
                .Where(a => a != null && a.Walker != null && a.Walker.IsConfigured &&
                            !selected.Contains(a.ActorId))
                .SelectMany(a => a.Walker.IsMoving
                    ? new[] { a.Walker.LastReachedCell, a.Walker.OrderedCell }
                    : new[] { a.Walker.LastReachedCell }));
            var selectedOccupancy = new Dictionary<ShipGridCell, EntityId>();
            foreach (var pair in positions)
            {
                if (!selected.Contains(pair.Key)) continue;
                selectedOccupancy[pair.Value.LastReachedCell] = pair.Key;
                if (pair.Value.IsMoving)
                    selectedOccupancy[pair.Value.OrderedCell] = pair.Key;
            }

            var candidates = new List<ShipGridCell>();
            // Expand toward the nearest legal parking spots; always favor a
            // compact cluster, even when some members start in another room.
            for (int radius = 0; radius <= 4; radius++)
                for (int dx = -radius; dx <= radius; dx++)
                    for (int dy = -radius; dy <= radius; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius) continue;
                        var spot = new ShipGridCell(center.RoomId,
                            center.X + dx, center.Y + dy);
                        if (map.Walkable(spot) && !map.IsPortalEndpoint(spot) &&
                            !reserved.Contains(spot))
                            candidates.Add(spot);
                    }
            var chosen = TacticalGroupGoalPlanner.Allocate(
                ids, candidates,
                (EntityId id, ShipGridCell spot) =>
                {
                    if (!positions.TryGetValue(id, out var walker)) return (float?)null;
                    if (selectedOccupancy.TryGetValue(spot, out var occupant) &&
                        occupant != id)
                        return (float?)null;
                    var route = map.FindPath(walker.LastReachedCell, spot);
                    if (route.Count == 0) return (float?)null;
                    float travel = 0f;
                    for (int i = 1; i < route.Count; i++)
                        travel += route[i].RoomId != route[i - 1].RoomId ? 1f :
                            Mathf.Sqrt(Mathf.Pow(route[i].X - route[i - 1].X, 2) +
                                       Mathf.Pow(route[i].Y - route[i - 1].Y, 2));
                    return travel;
                },
                (ShipGridCell a, ShipGridCell b) => a == b,
                spot => Mathf.Sqrt((spot.X - center.X) * (spot.X - center.X) +
                    (spot.Y - center.Y) * (spot.Y - center.Y)) * 1.25f);
            return chosen.ToDictionary(p => p.Key, p => CrewMoveGoal.ShipCellGoal(p.Value));
        }

        /// <summary>
        /// Same shared allocation contract on shore, but use NavMesh sample +
        /// complete-path checks and native agent avoidance (never ship A*).
        /// </summary>
        public static IReadOnlyDictionary<EntityId, CrewMoveGoal> PlanShoreGoals(
            IReadOnlyList<EntityId> ids, IEnumerable<TacticalActorProxy> actorsInScene,
            Vector3 center)
        {
            var candidates = new List<Vector3>();
            for (int ring = 0; ring <= 3; ring++)
            {
                int slots = ring == 0 ? 1 : 8 * ring;
                for (int i = 0; i < slots; i++)
                {
                    float angle = (2f * Mathf.PI * i) / slots;
                    var test = center + new Vector3(
                        Mathf.Cos(angle) * ring * 1.4f, 0f,
                        Mathf.Sin(angle) * ring * 1.4f);
                    if (NavMesh.SamplePosition(test, out var hit, 0.65f, NavMesh.AllAreas) &&
                        !candidates.Any(p => Vector3.Distance(p, hit.position) < 0.95f))
                        candidates.Add(hit.position);
                }
            }
            var selected = new HashSet<EntityId>(ids);
            var stationary = actorsInScene.Where(a => a != null &&
                !selected.Contains(a.ActorId) && a.isActiveAndEnabled)
                .Select(a => a.transform.position).ToArray();
            candidates.RemoveAll(p => stationary.Any(q =>
                Vector3.Distance(p, q) < 1.1f));
            var actors = actorsInScene.Where(a => a != null)
                .GroupBy(a => a.ActorId).ToDictionary(g => g.Key, g => g.First());
            var chosen = TacticalGroupGoalPlanner.Allocate(
                ids, candidates,
                (EntityId id, Vector3 spot) =>
                {
                    if (!actors.TryGetValue(id, out var actor)) return (float?)null;
                    var agent = actor.Agent;
                    if (agent == null || !agent.isOnNavMesh || !agent.isActiveAndEnabled)
                        return (float?)null;
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(agent.transform.position, spot,
                            NavMesh.AllAreas, path) ||
                        path.status != NavMeshPathStatus.PathComplete) return (float?)null;
                    float travel = 0f;
                    for (int i = 1; i < path.corners.Length; i++)
                        travel += Vector3.Distance(path.corners[i - 1], path.corners[i]);
                    return travel;
                },
                (Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1.2f,
                spot => Vector3.Distance(spot, center) * 1.25f);
            return chosen.ToDictionary(p => p.Key, p => CrewMoveGoal.ShorePoint(p.Value));
        }

    }
}
