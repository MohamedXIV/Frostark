using System;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// A physical intention inferred strictly from persisted role/shift/crew
    /// station, the actual canonical staffed roster and authored Work/Rest
    /// interaction slots. This is read-only: it does NOT perform work, reduce
    /// fatigue, grant stock, or become a second minute scheduler.
    /// </summary>
    public readonly struct CrewDutyActivityOffer
    {
        public CrewInteractionVerb Verb { get; }
        public EntityId SlotId { get; }
        public EntityId SourceModuleId { get; }
        public DutyRole? StaffedRole { get; }

        public CrewDutyActivityOffer(CrewInteractionVerb verb,
            EntityId slotId, EntityId sourceModuleId, DutyRole? staffedRole)
        {
            Verb = verb;
            SlotId = slotId;
            SourceModuleId = sourceModuleId;
            StaffedRole = staffedRole;
        }
    }

    public static class CrewDutyActivityPlanner
    {
        // Physical recovery is optional, not an invented mandatory Work order:
        // canonical fatigue still advances in CrewState.Tick only.
        public const int PreferRestFromFatigue = 650;

        public static CrewDutyActivityOffer? Find(WorldState world, EntityId crewId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var crew = world.CrewDuty.Get(crewId);
            if (!world.HasCrew(crewId) || crew == null ||
                crew.IsAway || crew.Wounds >= 1000)
                return null;

            var ownedClaim = world.CrewDuty.Interactions.ClaimForCrew(crewId);
            var staffed = world.CrewDuty.StaffedRoleFor(crewId, world.Minute);
            bool mustRest = crew.IsRecovering || !crew.OnShift(world.Minute) ||
                (staffed == null && crew.Fatigue >= PreferRestFromFatigue);

            // Nominal idle, off-duty and recovering personnel must not
            // fabricate a station or assume that unstaffed backup duty exists.
            if (mustRest)
            {
                if (crew.Fatigue == 0 && !crew.IsRecovering) return null;
                return FindSlot(world, CrewInteractionVerb.Rest, default,
                    null, ownedClaim);
            }

            if (staffed == null) return null;
            var stationId = world.GetCrewStation(crewId);
            if (!stationId.IsValid || world.Layout.GetModule(stationId) == null)
                return null; // Existing assignment may be symbolic/unplaced.
            return FindSlot(world, CrewInteractionVerb.Work, stationId,
                staffed, ownedClaim);
        }

        private static CrewDutyActivityOffer? FindSlot(WorldState world,
            CrewInteractionVerb verb, EntityId source, DutyRole? role,
            CrewInteractionClaim ownedClaim)
        {
            foreach (var slot in world.CrewDuty.Interactions.Slots
                .Where(x => x.Verb == verb && (!source.IsValid ||
                            x.SourceId == source))
                .OrderBy(x => x.Id.Value, StringComparer.Ordinal))
            {
                // Authored module, not a guessed room, and no unowned claim.
                if (world.Layout.GetModule(slot.SourceId) == null) continue;
                var claim = world.CrewDuty.Interactions.GetClaim(slot.Id);
                if (claim != null && (ownedClaim == null ||
                    claim.CrewId != ownedClaim.CrewId)) continue;
                // One crew cannot claim two distinct slots.
                if (ownedClaim != null && ownedClaim.SlotId != slot.Id) continue;
                return new CrewDutyActivityOffer(verb, slot.Id, slot.SourceId, role);
            }
            return null;
        }
    }
}
