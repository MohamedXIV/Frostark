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
        public IReadOnlyList<CrewMember> Members => Array.AsReadOnly(_members);
        public IReadOnlyList<RepairJob> RepairJobs => Array.AsReadOnly(_jobs);
        private CrewState(CrewMember[] members, RepairJob[] jobs)
        {
            _members = members;
            _jobs = jobs;
        }

        public static CrewState Empty => Create(Array.Empty<CrewMember>());

        public static CrewState Create(IEnumerable<CrewMember> members,
            IEnumerable<RepairJob>? jobs = null)
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
            return new CrewState(crew, queue);
        }

        public CrewMember? Get(EntityId id) =>
            id.IsValid ? Array.Find(_members, x => x.Id == id) : null;

        public RepairJob? GetRepair(EntityId id) =>
            id.IsValid ? Array.Find(_jobs, x => x.Id == id) : null;

        public CrewState SetAway(EntityId id, bool away)
        {
            if (Get(id) == null)
                throw new ArgumentException("Crew member is not in the authored roster.", nameof(id));
            var changed = _members.Select(m => m.Id == id ? m.WithAway(away) : m).ToArray();
            return changed.Zip(_members, (a, b) => ReferenceEquals(a, b)).All(x => x)
                ? this : new CrewState(changed, _jobs);
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
                            !member.OnShift(minute) || (member.Role == role) != primary)
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
                return member.WithFatigue(fatigue);
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
            return new CrewState(nextMembers, jobs);
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
            }
            foreach (var job in _jobs)
            {
                Add("repair"); Add(job.Id.Value);
                Add(job.RemainingWork.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(canonical.ToString());
        }
    }
}
