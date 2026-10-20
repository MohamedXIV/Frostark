using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Save;
using FrostArk.Core.World;

namespace FrostArk.Core.Application
{
    /// <summary>
    /// Why a stable crew ID cannot receive a local movement order in the
    /// currently loaded theater. This is routing eligibility, not pathfinding.
    /// </summary>
    public enum CrewTheaterRouteStatus
    {
        Ready = 0,
        UnknownCrew,
        UnavailableCrew,
        MissingPhysicalCheckpoint,
        InactiveTheater,
        WrongTheater,
        NotInActiveShoreParty
    }

    public sealed class CrewTheaterRouteDecision
    {
        public EntityId ActorId { get; }
        public CrewTheaterRouteStatus Status { get; }
        public bool Ready => Status == CrewTheaterRouteStatus.Ready;

        public CrewTheaterRouteDecision(EntityId actorId, CrewTheaterRouteStatus status)
        {
            ActorId = actorId;
            Status = status;
        }
    }

    /// <summary>
    /// Derives physical movement eligibility from the one authoritative crew
    /// roster and the persisted physical scene checkpoint. Does not move an
    /// actor, assign a job, spend resources, fabricate a scene or change a save.
    /// A null legacy checkpoint means unknown location, never "Ship" by default.
    /// </summary>
    public static class CrewTheaterMoveRouter
    {
        public static IReadOnlyList<CrewTheaterRouteDecision> Plan(
            WorldState world, PhysicalSceneCheckpoint checkpoint,
            TacticalOrderIntent intent, PhysicalTheater requestedTheater)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (intent == null) throw new ArgumentNullException(nameof(intent));
            if (intent.Kind != TacticalOrderKind.Move)
                throw new ArgumentException("Only Move orders belong to this router.", nameof(intent));
            if (requestedTheater != PhysicalTheater.Ship &&
                requestedTheater != PhysicalTheater.Shore)
                throw new ArgumentOutOfRangeException(nameof(requestedTheater));

            var decisions = new CrewTheaterRouteDecision[intent.ActorIds.Count];
            for (int i = 0; i < decisions.Length; i++)
            {
                var id = intent.ActorIds[i];
                var member = world.CrewDuty.Get(id);
                CrewTheaterRouteStatus status;
                if (!world.HasCrew(id) || member == null)
                    status = CrewTheaterRouteStatus.UnknownCrew;
                else if (member.Wounds >= 1000)
                    status = CrewTheaterRouteStatus.UnavailableCrew;
                else if (checkpoint == null)
                    status = CrewTheaterRouteStatus.MissingPhysicalCheckpoint;
                else if ((member.IsAway ? PhysicalTheater.Shore : PhysicalTheater.Ship)
                         != requestedTheater)
                    status = CrewTheaterRouteStatus.WrongTheater;
                else if (checkpoint.Theater != requestedTheater)
                    status = CrewTheaterRouteStatus.InactiveTheater;
                else if (requestedTheater == PhysicalTheater.Shore &&
                         !checkpoint.PartyIds.Contains(id))
                    status = CrewTheaterRouteStatus.NotInActiveShoreParty;
                else
                    status = CrewTheaterRouteStatus.Ready;

                decisions[i] = new CrewTheaterRouteDecision(id, status);
            }
            return Array.AsReadOnly(decisions);
        }
    }
}
