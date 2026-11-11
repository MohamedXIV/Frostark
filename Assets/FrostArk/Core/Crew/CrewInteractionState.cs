using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>What the authoring object offers, never the activity's actual effect.</summary>
    public enum CrewInteractionVerb : byte { Eat = 0, Drink = 1, Rest = 2, Work = 3 }

    /// <summary>
    /// Stable logical interaction opportunity. Offsets are local grid cells, not
    /// Unity Transform or a guaranteed traversable path; source integrity and
    /// walkability are validated by a later ship-scene adapter.
    /// </summary>
    public sealed class CrewInteractionSlot
    {
        public EntityId Id { get; }
        public EntityId SourceId { get; }
        public CrewInteractionVerb Verb { get; }
        public int LocalX { get; }
        public int LocalY { get; }
        public int Facing { get; }

        public CrewInteractionSlot(EntityId id, EntityId sourceId,
            CrewInteractionVerb verb, int localX, int localY, int facing)
        {
            if (!id.IsValid || !sourceId.IsValid)
                throw new ArgumentException("Authored interaction slot and source IDs are required.");
            if (!Enum.IsDefined(typeof(CrewInteractionVerb), verb))
                throw new ArgumentOutOfRangeException(nameof(verb));
            if (localX < -64 || localX > 64 || localY < -64 || localY > 64)
                throw new ArgumentOutOfRangeException(nameof(localX));
            if (facing < 0 || facing > 3)
                throw new ArgumentOutOfRangeException(nameof(facing));
            Id = id; SourceId = sourceId; Verb = verb;
            LocalX = localX; LocalY = localY; Facing = facing;
        }
    }

    /// <summary>
    /// Temporary slot occupancy by one crew member. Not a player Assignment,
    /// bed preference, or a guarantee the actor has arrived at the slot.
    /// </summary>
    public sealed class CrewInteractionClaim
    {
        public EntityId SlotId { get; }
        public EntityId CrewId { get; }
        public WorldMinute ExpiresAt { get; }

        public CrewInteractionClaim(EntityId slotId, EntityId crewId, WorldMinute expiresAt)
        {
            if (!slotId.IsValid || !crewId.IsValid)
                throw new ArgumentException("Claim requires real slot and crew IDs.");
            if (expiresAt.Value <= 0)
                throw new ArgumentOutOfRangeException(nameof(expiresAt));
            SlotId = slotId; CrewId = crewId; ExpiresAt = expiresAt;
        }
    }

    /// <summary>
    /// Immutable, sorted offer/claim registry for a ship CrewState. World commands
    /// own claiming and releasing; WorldClock owns expiry. No hidden wall time.
    /// </summary>
    public sealed class CrewInteractionState
    {
        private readonly CrewInteractionSlot[] _slots;
        private readonly CrewInteractionClaim[] _claims;
        public IReadOnlyList<CrewInteractionSlot> Slots => Array.AsReadOnly(_slots);
        public IReadOnlyList<CrewInteractionClaim> Claims => Array.AsReadOnly(_claims);
        private CrewInteractionState(CrewInteractionSlot[] slots, CrewInteractionClaim[] claims)
        { _slots = slots; _claims = claims; }

        public static CrewInteractionState Empty => Create(Array.Empty<CrewInteractionSlot>());

        public static CrewInteractionState Create(IEnumerable<CrewInteractionSlot> slots,
            IEnumerable<CrewInteractionClaim>? claims = null)
        {
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            var authored = slots.ToArray();
            var runtime = (claims ?? Array.Empty<CrewInteractionClaim>()).ToArray();
            if (authored.Any(s => s == null) || runtime.Any(c => c == null))
                throw new ArgumentException("Null interaction slot or claim.");
            authored = authored.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            runtime = runtime.OrderBy(x => x.SlotId.Value, StringComparer.Ordinal).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in authored)
                if (!ids.Add(slot.Id.Value))
                    throw new ArgumentException("Duplicate interaction slot ID.", nameof(slots));
            var owners = new HashSet<string>(StringComparer.Ordinal);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var claim in runtime)
            {
                if (!ids.Contains(claim.SlotId.Value) ||
                    !claimed.Add(claim.SlotId.Value) || !owners.Add(claim.CrewId.Value))
                    throw new ArgumentException("Invalid/duplicate interaction claim.", nameof(claims));
            }
            return new CrewInteractionState(authored, runtime);
        }

        public CrewInteractionSlot? GetSlot(EntityId slotId) =>
            slotId.IsValid ? Array.Find(_slots, s => s.Id == slotId) : null;
        public CrewInteractionClaim? GetClaim(EntityId slotId) =>
            slotId.IsValid ? Array.Find(_claims, c => c.SlotId == slotId) : null;
        public CrewInteractionClaim? ClaimForCrew(EntityId crewId) =>
            crewId.IsValid ? Array.Find(_claims, c => c.CrewId == crewId) : null;

        internal CrewInteractionState Claim(EntityId slotId, EntityId crewId,
            WorldMinute now, int lifetimeMinutes)
        {
            if (GetSlot(slotId) == null)
                throw new ArgumentException("Slot not authored.", nameof(slotId));
            if (GetClaim(slotId) != null || ClaimForCrew(crewId) != null)
                throw new InvalidOperationException("Slot or crew already claimed.");
            if (lifetimeMinutes <= 0 || lifetimeMinutes > 1440)
                throw new ArgumentOutOfRangeException(nameof(lifetimeMinutes));
            var expiration = new WorldMinute(checked(now.Value + lifetimeMinutes));
            return Create(_slots, _claims.Concat(new[] {
                new CrewInteractionClaim(slotId, crewId, expiration)
            }));
        }

        internal CrewInteractionState RenewDuty(EntityId slotId, EntityId crewId,
            WorldMinute now, int lifetimeMinutes)
        {
            var slot = GetSlot(slotId);
            var held = GetClaim(slotId);
            if (slot == null || (slot.Verb != CrewInteractionVerb.Work &&
                                  slot.Verb != CrewInteractionVerb.Rest) ||
                held == null || held.CrewId != crewId ||
                held.ExpiresAt <= now)
                throw new InvalidOperationException("Cannot renew an unowned or expired duty lease.");
            if (lifetimeMinutes <= 0 || lifetimeMinutes > 1440 ||
                held.ExpiresAt.Value - now.Value > 5)
                throw new ArgumentException("Duty lease renewal is not due or lifetime is invalid.");
            var nextExpiry = new WorldMinute(checked(now.Value + lifetimeMinutes));
            if (nextExpiry <= held.ExpiresAt)
                throw new InvalidOperationException("Duty lease must extend the current expiry.");
            return Create(_slots, _claims.Select(c => c.SlotId == slotId
                ? new CrewInteractionClaim(slotId, crewId, nextExpiry) : c));
        }

        internal CrewInteractionState Release(EntityId slotId, EntityId crewId)
        {
            var claim = GetClaim(slotId);
            if (claim == null || claim.CrewId != crewId)
                throw new InvalidOperationException("Only current owner may release.");
            return Create(_slots, _claims.Where(x => x.SlotId != slotId));
        }

        internal CrewInteractionState Revalidate(WorldMinute now,
            Func<EntityId, bool> crewEligible)
        {
            if (crewEligible == null) throw new ArgumentNullException(nameof(crewEligible));
            var remaining = _claims.Where(c =>
                c.ExpiresAt > now && crewEligible(c.CrewId)).ToArray();
            return remaining.Length == _claims.Length ? this : Create(_slots, remaining);
        }

        internal CrewInteractionState ClearCrew(EntityId crewId)
        {
            if (ClaimForCrew(crewId) == null) return this;
            return Create(_slots, _claims.Where(c => c.CrewId != crewId));
        }

        public uint Fingerprint()
        {
            var b = new StringBuilder();
            void Add(string part) => b.Append(part.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(part);
            foreach (var slot in _slots)
            {
                Add("slot"); Add(slot.Id.Value); Add(slot.SourceId.Value);
                Add(((int)slot.Verb).ToString(CultureInfo.InvariantCulture));
                Add(slot.LocalX.ToString(CultureInfo.InvariantCulture));
                Add(slot.LocalY.ToString(CultureInfo.InvariantCulture));
                Add(slot.Facing.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var claim in _claims)
            {
                Add("claim"); Add(claim.SlotId.Value); Add(claim.CrewId.Value);
                Add(claim.ExpiresAt.Value.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(b.ToString());
        }
    }
}
