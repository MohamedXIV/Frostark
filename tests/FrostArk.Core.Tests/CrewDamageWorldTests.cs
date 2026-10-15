using System;
using FrostArk.Core.Application;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewDamageWorldTests
    {
        private static readonly EntityId Protagonist = EntityId.Parse("actor:protagonist");
        private static readonly EntityId Enemy = EntityId.Parse("actor:enemy");

        private static WorldState CreateState(int protagonistWounds = 0, int enemyWounds = 0)
        {
            var crew = new[]
            {
                new CrewMember(Protagonist, DutyRole.Watch, 900, 300, 0, 1440, wounds: protagonistWounds),
                new CrewMember(Enemy, DutyRole.Watch, 800, 200, 0, 1440, wounds: enemyWounds)
            };
            return WorldState.Create(new WorldMinute(100),
                Array.Empty<SupplyStock>(),
                new[] { Protagonist, Enemy },
                crewDuty: CrewState.Create(crew));
        }

        [Test]
        public void DamageCrewCommandAppliesAuthoritativeWoundsAndEmitsEvent()
        {
            var before = CreateState();
            var bus = new WorldCommandBus();
            var cmd = new DamageCrewCommand(EntityId.Parse("cmd:wound:1"), Enemy, 250, 0);

            var result = bus.Execute(before, cmd);

            Assert.That(result.Success, Is.True);
            Assert.That(result.State.CrewDuty.Get(Enemy)!.Wounds, Is.EqualTo(250));
            Assert.That(result.State.Revision, Is.EqualTo(1));
            Assert.That(result.Events.Count, Is.EqualTo(1));
            Assert.That(result.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewWounded));
            Assert.That(result.Events[0].SubjectId, Is.EqualTo(Enemy));
            Assert.That(result.Events[0].Quantity, Is.EqualTo(250));
            Assert.That(before.CrewDuty.Get(Enemy)!.Wounds, Is.EqualTo(0));
        }

        [Test]
        public void AccumulatingWoundsReducesCrewSkillAndCapsAt1000()
        {
            var state = CreateState();
            var bus = new WorldCommandBus();

            var initialSkill = state.CrewDuty.Get(Enemy)!.SkillFor(DutyRole.Watch);
            Assert.That(initialSkill, Is.EqualTo(800));

            var shot1 = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:w:1"), Enemy, 400, 0));
            Assert.That(shot1.Success, Is.True);
            Assert.That(shot1.State.CrewDuty.Get(Enemy)!.Wounds, Is.EqualTo(400));
            var midSkill = shot1.State.CrewDuty.Get(Enemy)!.SkillFor(DutyRole.Watch);
            Assert.That(midSkill, Is.LessThan(initialSkill));

            // Another shot of 700 should cap at 1000
            var shot2 = bus.Execute(shot1.State, new DamageCrewCommand(EntityId.Parse("cmd:w:2"), Enemy, 700, 1));
            Assert.That(shot2.Success, Is.True);
            Assert.That(shot2.State.CrewDuty.Get(Enemy)!.Wounds, Is.EqualTo(1000));
            Assert.That(shot2.State.CrewDuty.Get(Enemy)!.SkillFor(DutyRole.Watch), Is.EqualTo(0));
        }

        [Test]
        public void DamagingAlreadyIncapacitatedCrewRejects()
        {
            var state = CreateState(enemyWounds: 1000);
            var bus = new WorldCommandBus();
            var cmd = new DamageCrewCommand(EntityId.Parse("cmd:w:overkill"), Enemy, 100, 0);

            var result = bus.Execute(state, cmd);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Failure, Is.EqualTo(CommandFailure.CrewAlreadyIncapacitated));
            Assert.That(result.State, Is.SameAs(state));
        }

        [Test]
        public void InvalidQuantityAndUnknownCrewAreRejected()
        {
            var state = CreateState();
            var bus = new WorldCommandBus();

            var zeroDmg = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:zero"), Enemy, 0, 0));
            Assert.That(zeroDmg.Failure, Is.EqualTo(CommandFailure.InvalidQuantity));

            var negDmg = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:neg"), Enemy, -50, 0));
            Assert.That(negDmg.Failure, Is.EqualTo(CommandFailure.InvalidQuantity));

            var excessive = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:excess"), Enemy, 1001, 0));
            Assert.That(excessive.Failure, Is.EqualTo(CommandFailure.InvalidQuantity));

            var unknown = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:unk"), EntityId.Parse("actor:ghost"), 200, 0));
            Assert.That(unknown.Failure, Is.EqualTo(CommandFailure.UnknownCrew));
        }

        [Test]
        public void DuplicateAndStaleCommandsAreRejected()
        {
            var state = CreateState();
            var bus = new WorldCommandBus();
            var cmd = new DamageCrewCommand(EntityId.Parse("cmd:dup"), Enemy, 200, 0);

            var first = bus.Execute(state, cmd);
            Assert.That(first.Success, Is.True);

            var duplicate = bus.Execute(first.State, cmd);
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));

            var stale = bus.Execute(first.State, new DamageCrewCommand(EntityId.Parse("cmd:stale"), Enemy, 200, 0));
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
        }

        [Test]
        public void CrewWoundsArePreservedAcrossSaveReload()
        {
            var state = CreateState();
            var bus = new WorldCommandBus();
            var wounded = bus.Execute(state, new DamageCrewCommand(EntityId.Parse("cmd:w:save"), Enemy, 450, 0)).State;

            var timer = new SimulationScheduler(new WorldClock(wounded.Minute), Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(wounded, timer));

            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.CrewDuty.Get(Enemy)!.Wounds, Is.EqualTo(450));
            Assert.That(decoded.Value.State.Revision, Is.EqualTo(1));
        }

        [Test]
        public void WorldSessionAdvancesCombatTickMonotonically()
        {
            var gunId = EntityId.Parse("item:gun");
            var bulletsId = EntityId.Parse("item:bullets");
            var catalog = new ItemCatalog(new[]
            {
                new ItemDefinition(gunId, 1, 1000, 1),
                new ItemDefinition(bulletsId, 1, 10, 50)
            });
            var inventory = InventoryState.Create(catalog, Array.Empty<SalvagePickup>(), 10000, 10, 10000, 10);
            var weapon = new WeaponDefinition(gunId, bulletsId, 1, 5);
            var combat = CombatState.Create(new[] { weapon });
            var state = WorldState.Create(new WorldMinute(0),
                Array.Empty<SupplyStock>(),
                new[] { Protagonist },
                inventory: inventory,
                crewDuty: CrewState.Create(new[] { new CrewMember(Protagonist, DutyRole.Watch, 900, 200, 0, 1440) }),
                combat: combat);

            using var session = new WorldSession(state);
            Assert.That(session.Capture().State.Combat.Tick, Is.EqualTo(0));

            bool notificationReceived = false;
            session.Changed += change =>
            {
                if (change.Kind == WorldSessionChangeKind.Time)
                    notificationReceived = true;
            };

            session.AdvanceCombatTick(10);
            Assert.That(session.Capture().State.Combat.Tick, Is.EqualTo(10));
            Assert.That(notificationReceived, Is.True);

            // Same tick does not re-notify
            notificationReceived = false;
            session.AdvanceCombatTick(10);
            Assert.That(notificationReceived, Is.False);

            // Rewind throws
            Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceCombatTick(5));
        }
    }
}
