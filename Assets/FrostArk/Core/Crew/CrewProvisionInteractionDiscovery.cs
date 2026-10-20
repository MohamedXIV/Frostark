using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// A possible provision activity, not a movement command or arrival proof.
    /// Physical source placement, line-of-sight and route validity are handled
    /// by the subsequent ship-local navigation/presentation adapter.
    /// </summary>
    public readonly struct CrewProvisionInteractionOffer
    {
        public EntityId SlotId { get; }
        public EntityId SourceId { get; }
        public EntityId ItemId { get; }
        public CrewInteractionVerb Verb { get; }
        public int NeedSeverity { get; }

        public CrewProvisionInteractionOffer(CrewInteractionSlot slot, EntityId itemId,
            int needSeverity)
        {
            SlotId = slot.Id;
            SourceId = slot.SourceId;
            ItemId = itemId;
            Verb = slot.Verb;
            NeedSeverity = needSeverity;
        }
    }

    /// <summary>
    /// Read-only deterministic discovery shared by a future goal selector, UI and
    /// Unity adapter. Real hunger and stock are authoritative; this class never
    /// claims a slot, consumes an item, or mutates any world state.
    /// </summary>
    public static class CrewProvisionInteractionDiscovery
    {
        public static IReadOnlyList<CrewProvisionInteractionOffer> Find(
            WorldState world, EntityId crewId)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var actor = world.CrewDuty.Get(crewId);
            if (actor == null || !world.HasCrew(crewId) || actor.IsAway ||
                actor.Wounds >= 1000 ||
                world.CrewDuty.Interactions.ClaimForCrew(crewId) != null)
                return Array.Empty<CrewProvisionInteractionOffer>();

            var available = new List<CrewProvisionInteractionOffer>();
            foreach (var slot in world.CrewDuty.Interactions.Slots)
            {
                if (world.CrewDuty.Interactions.GetClaim(slot.Id) != null)
                    continue;
                int severity = slot.Verb == CrewInteractionVerb.Eat ? actor.Hunger :
                    slot.Verb == CrewInteractionVerb.Drink ? actor.Thirst : 0;
                if (severity <= 0) continue;
                foreach (var provision in world.CrewDuty.Consumables)
                {
                    if (slot.Verb == CrewInteractionVerb.Eat && provision.Nutrition <= 0)
                        continue;
                    if (slot.Verb == CrewInteractionVerb.Drink && provision.Hydration <= 0)
                        continue;
                    if (world.Inventory.GetAmount(InventoryContainer.ShipHold,
                        provision.ItemId) <= 0)
                        continue;
                    available.Add(new CrewProvisionInteractionOffer(slot,
                        provision.ItemId, severity));
                }
            }

            return Array.AsReadOnly(available
                .OrderByDescending(x => x.NeedSeverity)
                .ThenBy(x => x.SlotId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.ItemId.Value, StringComparer.Ordinal)
                .ToArray());
        }
    }
}
