using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Immutable, sorted canonical crew duty snapshot. No wall clock, hidden RNG,
    /// Unity references or independent mutable queue. Roles have fixed priority
    /// Engineering > Watch > Cooking > Medical > Repair. A crew member fills
    /// at most one role per minute; eligible backup duty requires >=200 base skill.
    /// </summary>
    public sealed class CrewState
    {
        private static readonly DutyRole[] Roles =
        {
            DutyRole.Engineering, DutyRole.Watch, DutyRole.Cooking,
            DutyRole.Medical, DutyRole.Repair
        };
        private readonly CrewMember[] _members;
        private readonly RepairJob[] _jobs;
        private readonly MedicalTreatmentProtocol[] _treatments;
        private readonly CrewConsumable[] _consumables;
        public IReadOnlyList<CrewMember> Members => Array.AsReadOnly(_members);
        public IReadOnlyList<RepairJob> RepairJobs => Array.AsReadOnly(_jobs);
        public IReadOnlyList<MedicalTreatmentProtocol> Treatments => Array.AsReadOnly(_treatments);
        public IReadOnlyList<CrewConsumable> Consumables => Array.AsReadOnly(_consumables);
        private CrewState(CrewMember[] members, RepairJob[] jobs,
            MedicalTreatmentProtocol[] treatments, CrewConsumable[] consumables)
        {
            _members = members;
            _jobs = jobs;
            _treatments = treatments;
            _consumables = consumables;
        }

        public static CrewState Empty => Create(Array.Empty<CrewMember>());

        public static CrewState Create(IEnumerable<CrewMember> members,
            IEnumerable<RepairJob>? jobs = null,
            IEnumerable<MedicalTreatmentProtocol>? treatments = null,
            IEnumerable<CrewConsumable>? consumables = null)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var crew = members.ToArray();
            if (crew.Any(x => x == null))
                throw new ArgumentException("Null crew member.", nameof(members));
            crew = crew.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < crew.Length; i++)
                if (crew[i - 1].Id == crew[i].Id)
                    throw new ArgumentException("Duplicate persistent crew ID.", nameof(members));
            var queue = (jobs ?? Array.Empty<RepairJob>()).ToArray();
            if (queue.Any(x => x == null))
                throw new ArgumentException("Null repair job.", nameof(jobs));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var job in queue)
                if (!ids.Add(job.Id.Value))
                    throw new ArgumentException("Duplicate repair job ID.", nameof(jobs));
            var protocols = (treatments ?? Array.Empty<MedicalTreatmentProtocol>()).ToArray();
            if (protocols.Any(x => x == null))
                throw new ArgumentException("Null medical protocol.", nameof(treatments));
            protocols = protocols.OrderBy(x => x.ItemId.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < protocols.Length; i++)
                if (protocols[i-1].ItemId == protocols[i].ItemId)
                    throw new ArgumentException("Duplicate treatment item.", nameof(treatments));
            var food = (consumables ?? Array.Empty<CrewConsumable>()).ToArray();
            if (food.Any(x => x == null)) throw new ArgumentException("Null crew consumable.", nameof(consumables));
            food = food.OrderBy(x => x.ItemId.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < food.Length; i++)
                if (food[i - 1].ItemId == food[i].ItemId)
                    throw new ArgumentException("Duplicate consumable item.", nameof(consumables));
            return new CrewState(crew, queue, protocols, food);
        }

        public CrewState EnqueueRepair(EntityId id, long work)
        {
            if (!id.IsValid || GetRepair(id) != null || work <= 0)
                throw new ArgumentException("Duplicate or invalid repair job.", nameof(id));
            return Create(_members, _jobs.Concat(new[] { new RepairJob(id, work) }), _treatments, _consumables);
        }

        public CrewMember? Get(EntityId id) =>
            id.IsValid ? Array.Find(_members, x => x.Id == id) : null;

        public MedicalTreatmentProtocol? GetTreatment(EntityId itemId) =>
            itemId.IsValid ? Array.Find(_treatments, x => x.ItemId == itemId) : null;

        public CrewState WithTreatments(IEnumerable<MedicalTreatmentProtocol> treatments) =>
            Create(_members, _jobs, treatments, _consumables);

        /// <summary>Only authored treatment IDs can resolve healing potency;
        /// a caller cannot supply a substituted high-healing protocol.</summary>
        internal CrewState Treat(EntityId crewId, EntityId treatmentItemId)
        {
            var protocol = GetTreatment(treatmentItemId);
            if (protocol == null)
                throw new ArgumentException("Treatment protocol not authored.", nameof(treatmentItemId));
            var member = Get(crewId);
            if (member == null || member.IsAway || member.Wounds == 0)
                throw new ArgumentException("Crew patient is unavailable or healthy.", nameof(crewId));
            return new CrewState(_members.Select(m => m.Id == crewId
                ? m.WithWounds(Math.Max(0, m.Wounds - protocol.HealPermille))
                : m).ToArray(), _jobs, _treatments, _consumables);
        }

        internal CrewState Wound(EntityId crewId, int woundPermille)
        {
            if (woundPermille <= 0 || woundPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(woundPermille));
            var member = Get(crewId);
            if (member == null)
                throw new ArgumentException("Crew member is not in the authored roster.", nameof(crewId));
            return new CrewState(_members.Select(m => m.Id == crewId
                ? m.WithWounds(Math.Min(1000, m.Wounds + woundPermille))
                : m).ToArray(), _jobs, _treatments, _consumables);
        }

        public RepairJob? GetRepair(EntityId id) =>
            id.IsValid ? Array.Find(_jobs, x => x.Id == id) : null;

        /// <summary>
        /// SaveCodec extension restores the persisted recovery latch.
        /// A flag inconsistent with the crew member's fatigue is rejected by
        /// CrewMember.WithRecovering, not silently coerced during decoding.
        /// </summary>
        internal CrewState WithRecovering(EntityId id, bool recovering)
        {
            var current = Get(id);
            if (current == null)
                throw new ArgumentException("Crew member is not in the authored roster.",
                    nameof(id));
            var next = current.WithRecovering(recovering);
            if (ReferenceEquals(next, current)) return this;
            return new CrewState(_members.Select(m => m.Id == id ? next : m).ToArray(),
                _jobs, _treatments, _consumables);
        }

        public CrewConsumable? GetConsumable(EntityId itemId) =>
            itemId.IsValid ? Array.Find(_consumables, x => x.ItemId == itemId) : null;

        public CrewState WithConsumables(IEnumerable<CrewConsumable> consumables) =>
            Create(_members, _jobs, _treatments, consumables);

        public CrewNeedsAssessment AssessNeeds(EntityId crewId)
        {
            var member = Get(crewId);
            if (member == null) throw new ArgumentException("Unknown crew member.", nameof(crewId));
            return new CrewNeedsAssessment(member);
        }

        internal CrewState WithNeeds(EntityId id, int hunger, int thirst)
        {
            var member = Get(id);
            if (member == null) throw new ArgumentException("Unknown crew member.", nameof(id));
            var next = member.WithNeeds(hunger, thirst);
            return ReferenceEquals(next, member) ? this :
                new CrewState(_members.Select(m => m.Id == id ? next : m).ToArray(),
                    _jobs, _treatments, _consumables);
        }

        internal CrewState Consume(EntityId crewId, EntityId itemId)
        {
            var provision = GetConsumable(itemId);
            if (provision == null)
                throw new ArgumentException("Unapproved consumable.", nameof(itemId));
            var member = Get(crewId);
            if (member == null || member.IsAway)
                throw new ArgumentException("Crew member not on ship.", nameof(crewId));
            return WithNeeds(crewId, Math.Max(0, member.Hunger - provision.Nutrition),
                Math.Max(0, member.Thirst - provision.Hydration));
        }

        public CrewState SetAway(EntityId id, bool away)
        {
            if (Get(id) == null)
                throw new ArgumentException("Crew member is not in the authored roster.", nameof(id));
            var changed = _members.Select(m => m.Id == id ? m.WithAway(away) : m).ToArray();
            return changed.Zip(_members, (a, b) => ReferenceEquals(a, b)).All(x => x)
                ? this : new CrewState(changed, _jobs, _treatments, _consumables);
        }

        /// <summary>
        /// A fixed-priority, one-person-one-job assignment; prefer trained
        /// specialists, then highest effective backup and ordinal ID ties.
        /// This makes a skilled watchkeeper cover an absent engineer at a
        /// visible cost, rather than generating an imaginary replacement.
        /// </summary>
        private Dictionary<DutyRole, CrewMember> Staff(WorldMinute minute)
        {
            var assigned = new Dictionary<DutyRole, CrewMember>();
            var occupied = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in Roles)
            {
                CrewMember? best = null;
                int bestScore = 0;
                // Primary specialists are preferred to backups for each role.
                foreach (bool primary in new[] { true, false })
                {
                    foreach (var member in _members)
                    {
                        if (occupied.Contains(member.Id.Value) || member.IsAway ||
                            member.IsRecovering || member.Hunger >= CrewMember.CriticalNeedAt ||
                            member.Thirst >= CrewMember.CriticalNeedAt || !member.OnShift(minute) ||
                            (member.Role == role) != primary)
                            continue;
                        if (!primary && member.BackupSkill < 200)
                            continue;
                        int score = member.SkillFor(role);
                        if (score > bestScore)
                        {
                            best = member;
                            bestScore = score;
                        }
                    }
                    if (best != null) break;
                }
                if (best != null)
                {
                    assigned.Add(role, best);
                    occupied.Add(best.Id.Value);
                }
            }
            return assigned;
        }

        public int EffectiveSkill(DutyRole role, WorldMinute minute)
        {
            if (!Enum.IsDefined(typeof(DutyRole), role))
                throw new ArgumentOutOfRangeException(nameof(role));
            var assigned = Staff(minute);
            return assigned.TryGetValue(role, out var member) ? member.SkillFor(role) : 0;
        }

        /// <summary>
        /// Advance exactly one world-minute worth of duty, fatigue/rest and
        /// head-of-queue repair work. Caller owns minute sequencing and commit.
        /// Wounds do not magically heal during ordinary sleep.
        /// </summary>
        public CrewState Tick(WorldMinute minute)
        {
            var staffed = Staff(minute);
            var occupied = new HashSet<string>(staffed.Values.Select(x => x.Id.Value),
                StringComparer.Ordinal);
            var nextMembers = _members.Select(member =>
            {
                int fatigue = member.Fatigue;
                if (occupied.Contains(member.Id.Value))
                    fatigue = Math.Min(1000, fatigue + 1);
                else if (!member.IsAway)
                    fatigue = Math.Max(0, fatigue - 2);
                // Exactly one canonical world-minute drives physiology;
                // neither Unity Update nor a GOAP sensor may mutate these values.
                int hunger = Math.Min(1000, member.Hunger + (minute.Value % 3 == 0 ? 1 : 0));
                int thirst = Math.Min(1000, member.Thirst + (minute.Value % 2 == 0 ? 1 : 0));
                return member.WithFatigue(fatigue).WithNeeds(hunger, thirst);
            }).ToArray();

            var jobs = (RepairJob[])_jobs.Clone();
            if (jobs.Length > 0 && staffed.TryGetValue(DutyRole.Repair, out var repairer))
            {
                long progress = Math.Max(1, repairer.SkillFor(DutyRole.Repair) / 100);
                long remaining = jobs[0].RemainingWork - progress;
                if (remaining <= 0)
                    jobs = jobs.Skip(1).ToArray();
                else
                    jobs[0] = new RepairJob(jobs[0].Id, remaining);
            }
            return new CrewState(nextMembers, jobs, _treatments, _consumables);
        }

        public uint Fingerprint()
        {
            var canonical = new StringBuilder();
            void Add(string part)
            {
                canonical.Append(part.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(part);
            }
            foreach (var m in _members)
            {
                Add("member"); Add(m.Id.Value);
                Add(((int)m.Role).ToString(CultureInfo.InvariantCulture));
                Add(m.PrimarySkill.ToString(CultureInfo.InvariantCulture));
                Add(m.BackupSkill.ToString(CultureInfo.InvariantCulture));
                Add(m.ShiftStart.ToString(CultureInfo.InvariantCulture));
                Add(m.ShiftMinutes.ToString(CultureInfo.InvariantCulture));
                Add(m.Fatigue.ToString(CultureInfo.InvariantCulture));
                Add(m.Wounds.ToString(CultureInfo.InvariantCulture));
                Add(m.IsAway ? "1" : "0");
                // Keep old all-healthy fingerprints unchanged; only a latched
                // recovery period contributes additional state.
                if (m.IsRecovering) Add("recovering");
                if (m.Hunger != 0 || m.Thirst != 0)
                {
                    Add("needs"); Add(m.Hunger.ToString(CultureInfo.InvariantCulture));
                    Add(m.Thirst.ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (var job in _jobs)
            {
                Add("repair"); Add(job.Id.Value);
                Add(job.RemainingWork.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var t in _treatments)
            {
                Add("medicine"); Add(t.ItemId.Value);
                Add(t.Version.ToString(CultureInfo.InvariantCulture));
                Add(t.HealPermille.ToString(CultureInfo.InvariantCulture));
                Add(t.MinimumMedicalSkill.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var food in _consumables)
            {
                Add("provision"); Add(food.ItemId.Value);
                Add(food.Version.ToString(CultureInfo.InvariantCulture));
                Add(food.Nutrition.ToString(CultureInfo.InvariantCulture));
                Add(food.Hydration.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(canonical.ToString());
        }
    }
}
