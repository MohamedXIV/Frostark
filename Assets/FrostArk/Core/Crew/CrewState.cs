using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Crew
{
    /// <summary>
    /// Persistent immutable crew roster, shift windows and ordered repair jobs.
    /// Duties are calculated afresh from stable IDs per minute, never cached as
    /// separate mutable assignments in a MonoBehaviour.
    /// </summary>
    public sealed class CrewState
    {
        private readonly CrewMember[] _members;
        private readonly RepairJob[] _jobs;

        private CrewState(CrewMember[] members, RepairJob[] jobs)
        {
            _members = members;
            _jobs = jobs;
        }

        public static CrewState Empty => Create(Array.Empty<CrewMember>());
        public IReadOnlyList<CrewMember> Members => Array.AsReadOnly(_members);
        public IReadOnlyList<RepairJob> Repairs => Array.AsReadOnly(_jobs);

        public static CrewState Create(IEnumerable<CrewMember> members,
            IEnumerable<RepairJob> jobs = null)
        {
            if (members == null) throw new ArgumentNullException(nameof(members));
            var people = members.ToArray();
            if (people.Any(x => x == null)) throw new ArgumentException("Null member.");
            Array.Sort(people, (a, b) => StringComparer.Ordinal.Compare(a.Id.Value, b.Id.Value));
            for (int i = 1; i < people.Length; i++)
                if (people[i].Id == people[i - 1].Id)
                    throw new ArgumentException("Duplicate crew ID.", nameof(members));
            var repairs = (jobs ?? Array.Empty<RepairJob>()).ToArray();
            if (repairs.Any(x => x == null)) throw new ArgumentException("Null repair job.");
            Array.Sort(repairs, (a, b) => StringComparer.Ordinal.Compare(a.Id.Value, b.Id.Value));
            for (int i = 1; i < repairs.Length; i++)
                if (repairs[i].Id == repairs[i - 1].Id)
                    throw new ArgumentException("Duplicate repair job ID.", nameof(jobs));
            return new CrewState(people, repairs);
        }

        public CrewMember Get(EntityId id) =>
            id.IsValid ? Array.Find(_members, x => x.Id == id) : null;

        public RepairJob GetRepair(EntityId id) =>
            id.IsValid ? Array.Find(_jobs, x => x.Id == id) : null;

        public CrewState SetAway(EntityId id, bool away)
        {
            if (Get(id) == null)
                throw new ArgumentException("Crew ID not in the roster.", nameof(id));
            var people = _members.Select(p => p.Id == id ? p.WithAway(away) : p).ToArray();
            return new CrewState(people, _jobs);
        }

        /// <summary>
        /// One person per staffed role. Only authored primary roles open a duty
        /// slot; alternate specialties can cover them with backup skill.
        /// Engineering and Repair have precedence over other roles.
        /// </summary>
        private Dictionary<DutyRole, CrewMember> Allocate(WorldMinute minute)
        {
            var staffed = new Dictionary<DutyRole, CrewMember>();
            var used = new HashSet<string>(StringComparer.Ordinal);
            foreach (var role in new[] { DutyRole.Engineering, DutyRole.Repair,
                DutyRole.Watch, DutyRole.Cooking, DutyRole.Medical })
            {
                if (!_members.Any(p => p.Specialty == role))
                    continue;
                CrewMember best = null;
                int bestSkill = 0;
                foreach (var person in _members)
                {
                    if (used.Contains(person.Id.Value)) continue;
                    int skill = person.EffectiveSkill(role, minute);
                    if (skill > bestSkill)
                    {
                        best = person;
                        bestSkill = skill;
                    }
                }
                if (best == null) continue;
                staffed.Add(role, best);
                used.Add(best.Id.Value);
            }
            return staffed;
        }

        public int EffectiveSkill(DutyRole role, WorldMinute minute)
        {
            var allocated = Allocate(minute);
            return allocated.TryGetValue(role, out var person)
                ? person.EffectiveSkill(role, minute) : 0;
        }

        public CrewState Tick(WorldMinute minute)
        {
            var staffed = Allocate(minute);
            var busy = new HashSet<string>(staffed.Values.Select(x => x.Id.Value),
                StringComparer.Ordinal);
            var next = _members.Select(x =>
            {
                int fatigue = x.Fatigue;
                if (busy.Contains(x.Id.Value))
                    fatigue = Math.Min(1000, fatigue + 1);
                else if (!x.IsAway)
                    fatigue = Math.Max(0, fatigue - 2);
                return x.WithFatigue(fatigue);
            }).ToArray();

            // Only a staffed on-shift repair specialist or available backup
            // may process the first unfinished ordered repair job.
            var jobs = (RepairJob[])_jobs.Clone();
            int work = EffectiveSkill(DutyRole.Repair, minute) / 100;
            if (work > 0)
            {
                for (int i = 0; i < jobs.Length; i++)
                {
                    if (jobs[i].RemainingWork == 0) continue;
                    jobs[i] = jobs[i].WithWork(Math.Max(0, jobs[i].RemainingWork - work));
                    break;
                }
            }
            return new CrewState(next, jobs);
        }

        public uint Fingerprint()
        {
            var sb = new StringBuilder();
            void Add(string x) { sb.Append(x.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(x); }
            foreach (var p in _members)
            {
                Add(p.Id.Value); Add(((int)p.Specialty).ToString(CultureInfo.InvariantCulture));
                Add(p.PrimarySkill.ToString(CultureInfo.InvariantCulture));
                Add(p.BackupSkill.ToString(CultureInfo.InvariantCulture));
                Add(p.ShiftStart.ToString(CultureInfo.InvariantCulture));
                Add(p.ShiftMinutes.ToString(CultureInfo.InvariantCulture));
                Add(p.Fatigue.ToString(CultureInfo.InvariantCulture));
                Add(p.Wounds.ToString(CultureInfo.InvariantCulture));
                Add(p.IsAway ? "1" : "0");
            }
            foreach (var job in _jobs)
            {
                Add(job.Id.Value); Add(job.RemainingWork.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(sb.ToString());
        }
    }
}
