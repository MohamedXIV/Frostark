using System;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Authored, versioned need relief for one actual Inventory item.
    /// The player cannot supply arbitrary nutrition or hydration in a command.
    /// </summary>
    public sealed class CrewConsumable
    {
        public EntityId ItemId { get; }
        public int Nutrition { get; }
        public int Hydration { get; }
        public int Version { get; }

        public CrewConsumable(EntityId itemId, int nutrition, int hydration, int version = 1)
        {
            if (!itemId.IsValid) throw new ArgumentException("Item ID required.", nameof(itemId));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (nutrition < 0 || nutrition > 1000) throw new ArgumentOutOfRangeException(nameof(nutrition));
            if (hydration < 0 || hydration > 1000) throw new ArgumentOutOfRangeException(nameof(hydration));
            if (nutrition == 0 && hydration == 0)
                throw new ArgumentException("A consumable must satisfy a real need.");
            ItemId = itemId; Nutrition = nutrition; Hydration = hydration; Version = version;
        }
    }

    /// <summary>
    /// A lightweight read-only goal signal. Severe Needs prohibit duty but
    /// never silently manufacture food, water or a finished interaction.
    /// </summary>
    public readonly struct CrewNeedsAssessment
    {
        public int Hunger { get; }
        public int Thirst { get; }
        public bool RequiresFood => Hunger >= CrewMember.CriticalNeedAt;
        public bool RequiresWater => Thirst >= CrewMember.CriticalNeedAt;
        public bool IsCritical => RequiresFood || RequiresWater;
        public string Reason => RequiresWater ? "critical-thirst" :
            RequiresFood ? "critical-hunger" : "ordinary";

        public CrewNeedsAssessment(CrewMember member)
        {
            if (member == null) throw new ArgumentNullException(nameof(member));
            Hunger = member.Hunger;
            Thirst = member.Thirst;
        }
    }
}
