using System;
using System.Collections.Generic;
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
