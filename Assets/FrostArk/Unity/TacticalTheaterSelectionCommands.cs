using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Reuses the same transient TacticalSelectionController for ship and
    /// shore orders. Pointer resolution is performed upstream by the real
    /// tactical camera/scene adapter; ship targets are typed local cells.
    /// This has no scene-object cache or independent order/actor state.
    /// </summary>
    /// <summary>
    /// Shared, deterministic group-to-arrival-slot allocation. Navigation and
    /// crowd feasibility are theater-specific delegates. A click is the CENTER
    /// of the desired group, never the final cell of every member.
    /// </summary>
    public static class TacticalGroupGoalPlanner
    {
        public static IReadOnlyDictionary<EntityId, TGoal> Allocate<TGoal>(
            IEnumerable<EntityId> actors, IReadOnlyList<TGoal> candidates,
            Func<EntityId, TGoal, float?> travelCost,
            Func<TGoal, TGoal, bool> overlaps,
            Func<TGoal, float> centerPenalty)
        {
            if (actors == null || candidates == null ||
                travelCost == null || overlaps == null || centerPenalty == null)
                throw new ArgumentNullException("A group allocation input is null.");
            var ids = actors.Distinct().OrderBy(x => x.Value, StringComparer.Ordinal).ToArray();
            var scored = new List<(EntityId Id, int Index, float Score)>();
            foreach (var id in ids)
                for (int i = 0; i < candidates.Count; i++)
                {
                    var travel = travelCost(id, candidates[i]);
                    if (!travel.HasValue || float.IsNaN(travel.Value) ||
                        float.IsInfinity(travel.Value) || travel.Value < 0f) continue;
                    float score = travel.Value + centerPenalty(candidates[i]);
                    if (float.IsNaN(score) || float.IsInfinity(score)) continue;
                    scored.Add((id, i, score));
                }
            var chosen = new Dictionary<EntityId, TGoal>();
            foreach (var option in scored.OrderBy(x => x.Score)
                .ThenBy(x => x.Id.Value, StringComparer.Ordinal).ThenBy(x => x.Index))
            {
                if (chosen.ContainsKey(option.Id)) continue;
                var goal = candidates[option.Index];
                if (chosen.Values.Any(other => overlaps(other, goal))) continue;
                chosen.Add(option.Id, goal);
                if (chosen.Count == ids.Length) break;
            }
            return chosen;
        }
    }

    public static class TacticalTheaterSelectionCommands
    {
        public static IReadOnlyList<CrewMoveDispatchDecision> MoveSelectedOnShip(
            WorldSession session, TacticalSelectionController selection,
            ShipGridCell destination, Vector3 clickedWorldPoint,
            IEnumerable<ICrewTheaterMoveExecutor> sceneExecutors)
        {
            return MoveSelected(session, selection,
                CrewMoveGoal.ShipCellGoal(destination), clickedWorldPoint,
                sceneExecutors);
        }

        public static IReadOnlyList<CrewMoveDispatchDecision> MoveSelectedOnShore(
            WorldSession session, TacticalSelectionController selection,
            Vector3 worldDestination,
            IEnumerable<ICrewTheaterMoveExecutor> sceneExecutors)
        {
            return MoveSelected(session, selection,
                CrewMoveGoal.ShorePoint(worldDestination), worldDestination,
                sceneExecutors);
        }

        /// <summary>
        /// Shared ship/shore per-actor destinations. Keep authoritative theater,
        /// crew and scene checks in the existing dispatcher for EVERY actor.
        /// A missing legal parking spot is a real individual rejection.
        /// </summary>
        public static IReadOnlyList<CrewMoveDispatchDecision> MoveSelectedAssigned(
            WorldSession session, TacticalSelectionController selection,
            Vector3 center, PhysicalTheater theater,
            IReadOnlyDictionary<EntityId, CrewMoveGoal> actorGoals,
            IEnumerable<ICrewTheaterMoveExecutor> sceneExecutors)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (actorGoals == null) throw new ArgumentNullException(nameof(actorGoals));
            if (sceneExecutors == null) throw new ArgumentNullException(nameof(sceneExecutors));
            var executors = sceneExecutors.ToArray();
            var result = new List<CrewMoveDispatchDecision>();
            var world = session.Capture();
            foreach (var id in selection.SelectedActorIds)
            {
                var intent = TacticalOrderIntent.MoveTo(new[] { id }, center.x, center.z);
                if (actorGoals.TryGetValue(id, out var goal) && goal.Theater == theater)
                    result.Add(CrewTheaterMoveDispatcher.Dispatch(
                        session, intent, goal, executors)[0]);
                else
                {
                    var route = CrewTheaterMoveRouter.Plan(world.State,
                        world.SceneCheckpoint, intent, theater)[0];
                    result.Add(new CrewMoveDispatchDecision(id, route.Status,
                        route.Ready ? CrewMoveDispatchStatus.DestinationUnavailable :
                                      CrewMoveDispatchStatus.RouteRejected));
                }
            }
            return result.AsReadOnly();
        }

        private static IReadOnlyList<CrewMoveDispatchDecision> MoveSelected(
            WorldSession session, TacticalSelectionController selection,
            CrewMoveGoal typedGoal, Vector3 resolvedWorldPoint,
            IEnumerable<ICrewTheaterMoveExecutor> sceneExecutors)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (sceneExecutors == null)
                throw new ArgumentNullException(nameof(sceneExecutors));
            if (float.IsNaN(resolvedWorldPoint.x) ||
                float.IsInfinity(resolvedWorldPoint.x) ||
                float.IsNaN(resolvedWorldPoint.z) ||
                float.IsInfinity(resolvedWorldPoint.z))
                throw new ArgumentOutOfRangeException(nameof(resolvedWorldPoint));
            if (selection.Count == 0)
                return Array.Empty<CrewMoveDispatchDecision>();

            // This remains the real camera-resolved world point required by
            // TacticalOrderIntent, NOT a fictitious coordinate projection.
            // The separate goal holds the actual ship-local cell (or shore
            // NavMesh world position) for its respective executor.
            var intent = TacticalOrderIntent.MoveTo(selection.SelectedActorIds,
                resolvedWorldPoint.x, resolvedWorldPoint.z);
            return CrewTheaterMoveDispatcher.Dispatch(session, intent,
                typedGoal, sceneExecutors);
        }
    }
}
