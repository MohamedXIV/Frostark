using System;
using FrostArk.Core.Crew;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewDutyTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId Watch = EntityId.Parse("crew:watch");

        private static CrewState Roster() => CrewState.Create(new[]
        {
            new CrewMember(Engineer, DutyRole.Engineering, primarySkill: 1000,
                backupSkill: 100, shiftStart: 0, shiftMinutes: 1440),
            new CrewMember(Watch, DutyRole.Watch, primarySkill: 800,
                backupSkill: 400, shiftStart: 0, shiftMinutes: 1440)
        }, new[] { new RepairJob(EntityId.Parse("repair:hull"), 200) });

        [Test]
        public void DispatchingEngineerAshoreSelectsLessEfficientBackup()
        {
            var roster = Roster();
            Assert.That(roster.EffectiveSkill(DutyRole.Engineering, new WorldMinute(10)),
                Is.EqualTo(1000));
            var dispatched = roster.SetAway(Engineer, true);
            Assert.That(dispatched.Get(Engineer).IsAway, Is.True);
            Assert.That(dispatched.EffectiveSkill(DutyRole.Engineering, new WorldMinute(10)),
                Is.EqualTo(400));
            Assert.That(roster.Get(Engineer).IsAway, Is.False);
            Assert.That(dispatched.SetAway(Engineer, false)
                .EffectiveSkill(DutyRole.Engineering, new WorldMinute(10)), Is.EqualTo(1000));
        }

        [Test]
        public void ShiftWindowsWrapMidnightAndAbsenceOverridesDuty()
        {
            var member = new CrewMember(Engineer, DutyRole.Engineering, 950, 100,
                shiftStart: 1320, shiftMinutes: 480);
            var roster = CrewState.Create(new[] { member });
            Assert.That(roster.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1380)),
                Is.EqualTo(950));
            Assert.That(roster.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1800)),
                Is.Zero);
            Assert.That(roster.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1500)),
                Is.EqualTo(950));
        }

        [Test]
        public void FatigueWoundsAndRestDeterministicallyModifyAvailability()
        {
            var healthy = Roster();
            var tired = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 100,
                    0, 1440, fatigue: 600, wounds: 500)
            });
            Assert.That(tired.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1)),
                Is.LessThan(healthy.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1))));

            var resting = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 900, 100,
                    shiftStart: 100, shiftMinutes: 500, fatigue: 200)
            });
            var next = resting.Tick(new WorldMinute(50));
            Assert.That(next.Get(Engineer).Fatigue, Is.LessThan(200));
            Assert.That(next.Get(Engineer).Wounds, Is.Zero);
            Assert.That(next.Get(Engineer).PrimarySkill, Is.EqualTo(900));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMember(
                Engineer, DutyRole.Engineering, 800, 100, 0, 0));
        }

        [Test]
        public void RepairProgressRequiresAvailableRepairSkillAndKeepsIdentity()
        {
            var repairer = new CrewMember(Engineer, DutyRole.Repair, 800, 100,
                shiftStart: 0, shiftMinutes: 1440);
            var state = CrewState.Create(new[] { repairer },
                new[] { new RepairJob(EntityId.Parse("repair:hull"), 40) });
            var after = state.Tick(new WorldMinute(1));
            Assert.That(after.GetRepair(EntityId.Parse("repair:hull")).RemainingWork,
                Is.LessThan(40));
            Assert.That(state.GetRepair(EntityId.Parse("repair:hull")).RemainingWork,
                Is.EqualTo(40));

            var absent = state.SetAway(Engineer, true).Tick(new WorldMinute(1));
            Assert.That(absent.GetRepair(EntityId.Parse("repair:hull")).RemainingWork,
                Is.EqualTo(40));
        }

        [Test]
        public void InvalidRosterAndJobsCannotIntroduceDuplicatePersistentIds()
        {
            var member = new CrewMember(Engineer, DutyRole.Engineering, 1000, 300,
                shiftStart: 0, shiftMinutes: 600);
            Assert.Throws<ArgumentException>(() => CrewState.Create(new[] { member, member }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrewMember(
                Engineer, DutyRole.Engineering, 1001, 100, 0, 600));
            Assert.Throws<ArgumentException>(() => CrewState.Create(new[] { member },
                new[] { new RepairJob(EntityId.Parse("repair:one"), 10),
                    new RepairJob(EntityId.Parse("repair:one"), 15) }));
        }
        [Test]
        public void ReplacementNeverDoubleBooksSameWatchkeeper()
        {
            var loneWatch = CrewState.Create(new[]
            {
                new CrewMember(Watch, DutyRole.Watch, 900, 400,
                    shiftStart: 0, shiftMinutes: 1440)
            });
            Assert.That(loneWatch.EffectiveSkill(DutyRole.Engineering,
                new WorldMinute(1)), Is.EqualTo(400));
            Assert.That(loneWatch.EffectiveSkill(DutyRole.Watch,
                new WorldMinute(1)), Is.Zero);
            Assert.That(loneWatch.Tick(new WorldMinute(1)).Get(Watch).Fatigue,
                Is.EqualTo(1));
        }

    }
}
