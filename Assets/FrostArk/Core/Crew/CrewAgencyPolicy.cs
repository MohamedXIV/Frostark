using System;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// A read-only priority decision. The WorldSession remains the ONLY source
    /// of Needs, assignments, provisions and slot claims. This is not a new
    /// simulation clock, GOAP planner, or a persisted mouse-selection model.
    /// </summary>
    public enum CrewAgencyDecisionKind
    {
        Unavailable = 0,
        ContinuePlayerOrder,
        HoldForPlayerSelection,
        SeekProvision,
        EmergencyNeedsHandover,
        UnmetCriticalNeed,
        ReadyForDuty
    }

    public readonly struct CrewAgencyDecision
    {
        public CrewAgencyDecisionKind Kind { get; }
        public bool HasProvision { get; }
        public CrewProvisionInteractionOffer Provision { get; }
        public int Severity { get; }

        public CrewAgencyDecision(CrewAgencyDecisionKind kind, int severity,
            CrewProvisionInteractionOffer provision = default,
            bool hasProvision = false)
        {
            Kind = kind;
            Severity = severity;
            Provision = provision;
            HasProvision = hasProvision;
        }
    }

    public static class CrewAgencyPolicy
    {
        // Engineering thresholds. Urgent needs can interrupt at a safe
        // movement boundary. No food/water may be conjured when stock is empty.
        public const int SeekProvisionAt = 650;
        public const int EmergencyInterruptAt = 950;

        public static CrewAgencyDecision Evaluate(WorldState world, EntityId crewId,
            bool isSelected, bool hasActivePlayerOrder)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var member = world.CrewDuty.Get(crewId);
            if (!world.HasCrew(crewId) || member == null ||
                member.IsAway || member.Wounds >= 1000)
                return new CrewAgencyDecision(CrewAgencyDecisionKind.Unavailable, 0);

            int severity = Math.Max(member.Hunger, member.Thirst);
            bool emergency = severity >= EmergencyInterruptAt;
            bool critical = severity >= CrewMember.CriticalNeedAt;
            if (emergency && (hasActivePlayerOrder || isSelected))
            {
                var emergencyOffers = CrewProvisionInteractionDiscovery.Find(world, crewId);
                if (emergencyOffers.Count > 0)
                    return new CrewAgencyDecision(CrewAgencyDecisionKind.EmergencyNeedsHandover,
                        severity, emergencyOffers[0], true);
                // No item or free authored slot: do not invent a safety action.
                return new CrewAgencyDecision(CrewAgencyDecisionKind.UnmetCriticalNeed, severity);
            }

            if (hasActivePlayerOrder)
                return new CrewAgencyDecision(CrewAgencyDecisionKind.ContinuePlayerOrder, severity);
            if (isSelected)
                return new CrewAgencyDecision(CrewAgencyDecisionKind.HoldForPlayerSelection, severity);

            if (severity >= SeekProvisionAt)
            {
                var offers = CrewProvisionInteractionDiscovery.Find(world, crewId);
                if (offers.Count > 0)
                    return new CrewAgencyDecision(CrewAgencyDecisionKind.SeekProvision,
                        severity, offers[0], true);
                if (critical)
                    return new CrewAgencyDecision(CrewAgencyDecisionKind.UnmetCriticalNeed, severity);
            }
            return new CrewAgencyDecision(CrewAgencyDecisionKind.ReadyForDuty, severity);
        }
    }
}
