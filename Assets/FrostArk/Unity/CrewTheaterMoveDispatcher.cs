using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// A typed movement destination. Ship actors receive an integer local
    /// cell; shore actors receive a world point for Unity NavMesh. Sea-ship
    /// travel is deliberately not represented here.
    /// </summary>
    public readonly struct CrewMoveGoal
    {
        public PhysicalTheater Theater { get; }
        public ShipGridCell ShipCell { get; }
        public Vector3 ShoreWorld { get; }

        private CrewMoveGoal(PhysicalTheater theater, ShipGridCell cell, Vector3 point)
        {
            Theater = theater;
            ShipCell = cell;
            ShoreWorld = point;
        }

        public static CrewMoveGoal ShipCellGoal(ShipGridCell localCell) =>
            new CrewMoveGoal(PhysicalTheater.Ship, localCell, default);

        public static CrewMoveGoal ShorePoint(Vector3 world)
        {
            if (float.IsNaN(world.x) || float.IsInfinity(world.x) ||
                float.IsNaN(world.y) || float.IsInfinity(world.y) ||
                float.IsNaN(world.z) || float.IsInfinity(world.z))
                throw new ArgumentOutOfRangeException(nameof(world));
            return new CrewMoveGoal(PhysicalTheater.Shore, default, world);
        }
    }

    /// <summary>
    /// An already-authored scene proxy for ONE stable persistent crew identity.
    /// A ship implementation must use ShipCell (local A*), and a shore
    /// implementation must use ShoreWorld (Unity NavMeshAgent).
    /// </summary>
    public interface ICrewTheaterMoveExecutor
    {
        EntityId ActorId { get; }
        EntityId SceneId { get; }
        PhysicalTheater Theater { get; }
        bool TryMove(CrewMoveGoal destination);
    }

    /// <summary>
    /// Thin adapter around the existing TacticalActorProxy/NavMesh shore
    /// movement. It never makes a parallel actor, inventory or movement clock.
    /// </summary>
    public sealed class ShoreNavMeshCrewExecutor : ICrewTheaterMoveExecutor
    {
        private readonly TacticalActorProxy _actor;

        public ShoreNavMeshCrewExecutor(TacticalActorProxy actor, EntityId sceneId)
        {
            _actor = actor != null ? actor :
                throw new ArgumentNullException(nameof(actor));
            if (!sceneId.IsValid)
                throw new ArgumentException("Authored scene ID required.", nameof(sceneId));
            SceneId = sceneId;
        }

        public EntityId ActorId => _actor.ActorId;
        public EntityId SceneId { get; }
        public PhysicalTheater Theater => PhysicalTheater.Shore;

        public bool TryMove(CrewMoveGoal destination) =>
            destination.Theater == PhysicalTheater.Shore &&
            _actor != null && _actor.isActiveAndEnabled &&
            _actor.MoveTo(destination.ShoreWorld);
    }

    public enum CrewMoveDispatchStatus
    {
        Accepted = 0,
        RouteRejected,
        NoSceneExecutor,
        StaleSceneExecutor,
        DuplicateSceneExecutor,
        ExecutorRejected,
        DestinationUnavailable
    }

    public sealed class CrewMoveDispatchDecision
    {
        public EntityId ActorId { get; }
        public CrewTheaterRouteStatus RouteStatus { get; }
        public CrewMoveDispatchStatus Status { get; }
        public bool Accepted => Status == CrewMoveDispatchStatus.Accepted;

        public CrewMoveDispatchDecision(EntityId actorId,
            CrewTheaterRouteStatus routeStatus, CrewMoveDispatchStatus status)
        {
            ActorId = actorId;
            RouteStatus = routeStatus;
            Status = status;
        }
    }

    /// <summary>
    /// Per-actor command-to-executor seam. Checks canonical WorldSession +
    /// SceneCheckpoint before resolving any scene proxy. No arbitrary visual
    /// GameObject or click location determines the physical theater.
    /// Invalid actors never receive TryMove; no success-by-partial-batch.
    /// </summary>
    public static class CrewTheaterMoveDispatcher
    {
        public static IReadOnlyList<CrewMoveDispatchDecision> Dispatch(
            WorldSession session, TacticalOrderIntent intent, CrewMoveGoal goal,
            IEnumerable<ICrewTheaterMoveExecutor> sceneExecutors)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (sceneExecutors == null)
                throw new ArgumentNullException(nameof(sceneExecutors));
            var snapshot = session.Capture();
            var routes = CrewTheaterMoveRouter.Plan(snapshot.State,
                snapshot.SceneCheckpoint, intent, goal.Theater);
            var executors = sceneExecutors.Where(e => e != null).ToArray();
            var decisions = new CrewMoveDispatchDecision[routes.Count];

            for (int i = 0; i < routes.Count; i++)
            {
                var route = routes[i];
                var status = CrewMoveDispatchStatus.RouteRejected;
                if (route.Ready)
                {
                    var sameTheater = executors.Where(e =>
                        e.ActorId == route.ActorId && e.Theater == goal.Theater).ToArray();
                    var matches = sameTheater.Where(e =>
                        e.SceneId == snapshot.SceneCheckpoint.SceneId).ToArray();
                    if (matches.Length == 0)
                        status = sameTheater.Length > 0 ?
                            CrewMoveDispatchStatus.StaleSceneExecutor :
                            CrewMoveDispatchStatus.NoSceneExecutor;
                    else if (matches.Length > 1)
                        status = CrewMoveDispatchStatus.DuplicateSceneExecutor;
                    else
                        status = matches[0].TryMove(goal) ?
                            CrewMoveDispatchStatus.Accepted :
                            CrewMoveDispatchStatus.ExecutorRejected;
                }
                decisions[i] = new CrewMoveDispatchDecision(
                    route.ActorId, route.Status, status);
            }

            return Array.AsReadOnly(decisions);
        }
    }
}
