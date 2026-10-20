using System;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewNeedsTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId Backup = EntityId.Parse("crew:backup");
        private static readonly EntityId Food = EntityId.Parse("item:ration");
        private static readonly EntityId Water = EntityId.Parse("item:water");

        private static CrewState Duty(int hunger = 750, int thirst = 700,
            bool away = false, bool includeBackup = true, bool withProvisions = true)
        {
            var engineer = new CrewMember(Engineer, DutyRole.Engineering, 900, 50, 0, 1440,
                isAway: away, hunger: hunger, thirst: thirst);
            var backup = new CrewMember(Backup, DutyRole.Watch, 850, 600, 0, 1440);
            return CrewState.Create(includeBackup ? new[] { engineer, backup } : new[] { engineer },
                consumables: withProvisions ? new[]
                {
                    new CrewConsumable(Food, nutrition: 500, hydration: 0),
                    new CrewConsumable(Water, nutrition: 0, hydration: 600)
                } : Array.Empty<CrewConsumable>());
        }

        private static InventoryState Inventory(bool stocked = true)
        {
            var initial = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Food, 1, 250, 10),
                new ItemDefinition(Water, 1, 500, 10)
            }), new[]
            {
                new SalvagePickup(EntityId.Parse("pickup:food"), Food, 2),
                new SalvagePickup(EntityId.Parse("pickup:water"), Water, 2)
            }, 10000, 5, 10000, 5);
            if (!stocked) return initial;
            return initial.TryCollect(EntityId.Parse("pickup:food")).State
                .TryCollect(EntityId.Parse("pickup:water")).State
                .TryUnload(Food, 2).State
                .TryUnload(Water, 2).State;
        }

        private static WorldState World(int hunger = 750, int thirst = 700,
            bool stocked = true, bool away = false, bool backup = true,
            bool provisions = true)
        {
            return WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                backup ? new[] { Engineer, Backup } : new[] { Engineer },
                crewDuty: Duty(hunger, thirst, away, backup, provisions),
                inventory: Inventory(stocked));
        }

        private static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> write) =>
            new SimulationScheduler(new WorldClock(read().Minute), new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(read, write),
                new ShipThermalMinuteSystem(read, write, _ => 0)
            }, seed: 19);

        [Test]
        public void WorldMinuteDrivesBothNeedsAndCriticalDutyHandoff()
        {
            var crew = Duty(hunger: 849, thirst: 100);
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, WorldMinute.Zero),
                Is.EqualTo(900));
            crew = crew.Tick(new WorldMinute(1));
            crew = crew.Tick(new WorldMinute(2));
            Assert.That(crew.Get(Engineer).Hunger, Is.EqualTo(849));
            Assert.That(crew.Get(Engineer).Thirst, Is.EqualTo(101));
            crew = crew.Tick(new WorldMinute(3));
            Assert.That(crew.Get(Engineer).Hunger, Is.EqualTo(850));
            Assert.That(crew.AssessNeeds(Engineer).RequiresFood, Is.True);
            Assert.That(crew.AssessNeeds(Engineer).Reason, Is.EqualTo("critical-hunger"));
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, new WorldMinute(4)),
                Is.GreaterThan(0), "The available backup must be staffed.");
            Assert.That(crew.EffectiveSkill(DutyRole.Watch, new WorldMinute(4)), Is.Zero,
                "Backup must not be double booked.");
        }

        [Test]
        public void NoBackupMeansTheCriticalCrewMemberStopsWorking()
        {
            var crew = Duty(hunger: 850, thirst: 200, includeBackup: false);
            Assert.That(crew.EffectiveSkill(DutyRole.Engineering, new WorldMinute(1)),
                Is.Zero);
            crew = crew.Tick(new WorldMinute(1));
            Assert.That(crew.Get(Engineer).Fatigue, Is.Zero);
            Assert.That(crew.AssessNeeds(Engineer).IsCritical, Is.True);
        }

        [Test]
        public void RealFoodAndWaterConsumeStockAtomicallyAndSatisfyOnlyApprovedNeeds()
        {
            var start = World(hunger: 950, thirst: 920);
            var bus = new WorldCommandBus();
            var eat = new ConsumeCrewProvisionCommand(EntityId.Parse("command:eat"),
                Engineer, Food, 0);
            var first = bus.Execute(start, eat);
            Assert.That(first.Success, Is.True);
            Assert.That(first.Events, Has.Count.EqualTo(1));
            Assert.That(first.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewProvisionConsumed));
            Assert.That(first.State.CrewDuty.Get(Engineer).Hunger, Is.EqualTo(450));
            Assert.That(first.State.CrewDuty.Get(Engineer).Thirst, Is.EqualTo(920));
            Assert.That(first.State.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(1));
            Assert.That(start.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(2));

            var drink = new ConsumeCrewProvisionCommand(EntityId.Parse("command:drink"),
                Engineer, Water, first.State.Revision);
            var second = bus.Execute(first.State, drink);
            Assert.That(second.Success, Is.True);
            Assert.That(second.State.CrewDuty.Get(Engineer).Thirst, Is.EqualTo(320));
            Assert.That(second.State.Inventory.GetAmount(InventoryContainer.ShipHold, Water),
                Is.EqualTo(1));
            Assert.That(second.State.CrewDuty.EffectiveSkill(DutyRole.Engineering,
                new WorldMinute(1)), Is.GreaterThan(0));
            var replay = bus.Execute(second.State, eat);
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(replay.State, Is.SameAs(second.State));
        }

        [Test]
        public void NoFoodUnknownProvisionAwayActorAndStaleRevisionNeverSpend()
        {
            var bus = new WorldCommandBus();
            var missing = World(stocked: false);
            var noFood = bus.Execute(missing, new ConsumeCrewProvisionCommand(
                EntityId.Parse("command:empty"), Engineer, Food, 0));
            Assert.That(noFood.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
            Assert.That(noFood.State, Is.SameAs(missing));
            Assert.That(noFood.Events, Is.Empty);

            var world = World();
            var unknown = bus.Execute(world, new ConsumeCrewProvisionCommand(
                EntityId.Parse("command:unknown"), Engineer, EntityId.Parse("item:unknown"), 0));
            Assert.That(unknown.Failure, Is.EqualTo(CommandFailure.UnknownProvision));
            var away = World(away: true);
            var absent = bus.Execute(away, new ConsumeCrewProvisionCommand(
                EntityId.Parse("command:absent"), Engineer, Food, 0));
            Assert.That(absent.Failure, Is.EqualTo(CommandFailure.CrewNotOnShip));
            var stale = bus.Execute(world, new ConsumeCrewProvisionCommand(
                EntityId.Parse("command:stale"), Engineer, Water, 9));
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(world.Revision, Is.Zero);
            Assert.That(world.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(2));
        }

        [Test]
        public void NeedsProgressWhileAshoreWithoutSimulatingFreeRecovery()
        {
            var crew = Duty(hunger: 600, thirst: 650, away: true, includeBackup: false);
            for (int minute = 1; minute <= 6; minute++)
                crew = crew.Tick(new WorldMinute(minute));
            var member = crew.Get(Engineer);
            Assert.That(member.Hunger, Is.EqualTo(602));
            Assert.That(member.Thirst, Is.EqualTo(653));
            Assert.That(member.Fatigue, Is.Zero);
            Assert.That(member.IsAway, Is.True);
        }

        [Test]
        public void InvalidConsumableCatalogCannotBypassAuthoritativeInventory()
        {
            Assert.Throws<ArgumentException>(() => new CrewConsumable(Food, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new CrewConsumable(Food, 1200, 1));
            Assert.Throws<ArgumentException>(() => CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 900, 0, 0, 1440)
            }, consumables: new[] { new CrewConsumable(Food, 300, 0),
                new CrewConsumable(Food, 100, 0) }));
            Assert.Throws<ArgumentException>(() => WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Engineer },
                crewDuty: Duty(includeBackup: false), inventory: InventoryState.Empty));
        }

        [Test]
        public void V16RoundTripPreservesNeedsConsumablesAndCommandProvenance()
        {
            var initial = World(hunger: 970, thirst: 940);
            var first = new WorldCommandBus().Execute(initial,
                new ConsumeCrewProvisionCommand(EntityId.Parse("command:meal"), Engineer, Food, 0));
            Assert.That(first.Success, Is.True);
            var timer = Bind(() => first.State, _ => { });
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(first.State, timer));
            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.CrewDuty.Get(Engineer).Hunger, Is.EqualTo(470));
            Assert.That(decoded.Value.State.CrewDuty.Consumables, Has.Count.EqualTo(2));
            Assert.That(decoded.Value.State.Fingerprint(), Is.EqualTo(first.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(bytes));
            var replay = new WorldCommandBus().Execute(decoded.Value.State,
                new ConsumeCrewProvisionCommand(EntityId.Parse("command:meal"), Engineer, Food,
                    decoded.Value.State.Revision));
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
        }

        [Test]
        public void ExistingV15SaveWithoutNeedsDoesNotInventFoodOrHunger()
        {
            var state = WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                new[] { Engineer }, crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(Engineer, DutyRole.Engineering, 800, 0, 0, 1440)
                }));
            var timer = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var newest = SaveCodec.Encode(SaveGameDto.Capture(state, timer));
            int idBytes = Encoding.UTF8.GetByteCount(Engineer.Value);
            int extension = 4 + (4 + idBytes + 8) + 4; // v16: one crew, zero provisions
            int payload = BitConverter.ToInt32(newest, 10) - extension;
            var old = new byte[14 + payload + 32];
            Array.Copy(newest, old, 14 + payload);
            old[8] = 15;
            old[9] = 0;
            Array.Copy(BitConverter.GetBytes(payload), 0, old, 10, 4);
            Resign(old);
            var decoded = SaveCodec.TryDecode(old);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(decoded.Value.State.CrewDuty.Get(Engineer).Hunger, Is.Zero);
            Assert.That(decoded.Value.State.CrewDuty.Consumables, Is.Empty);
        }

        [Test]
        public void InvalidChecksummedNeedValueCannotBeLoaded()
        {
            var state = World(hunger: 650, thirst: 590, provisions: false, backup: false);
            var timer = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(state, timer));
            int idBytes = Encoding.UTF8.GetByteCount(Engineer.Value);
            int extension = 4 + (4 + idBytes + 8) + 4;
            int hungerOffset = bytes.Length - 32 - extension + 4 + 4 + idBytes;
            Array.Copy(BitConverter.GetBytes(1001), 0, bytes, hungerOffset, 4);
            Resign(bytes);
            var decoded = SaveCodec.TryDecode(bytes);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void SaveResumeAndBatchAdvanceMatchUninterruptedNeedEvolution()
        {
            var whole = World(hunger: 200, thirst: 300);
            var uninterrupted = Bind(() => whole, s => whole = s);
            uninterrupted.Advance(60);

            var partial = World(hunger: 200, thirst: 300);
            var first = Bind(() => partial, s => partial = s);
            first.Advance(25);
            var restored = SaveCodec.TryDecode(SaveCodec.Encode(
                SaveGameDto.Capture(partial, first)));
            Assert.That(restored.Success, Is.True, restored.Error.ToString());
            var resumed = restored.Value.State;
            var second = restored.Value.CreateScheduler(new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(() => resumed, s => resumed = s),
                new ShipThermalMinuteSystem(() => resumed, s => resumed = s, _ => 0)
            });
            second.Advance(35);
            Assert.That(resumed.Fingerprint(), Is.EqualTo(whole.Fingerprint()));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(resumed, second)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(whole, uninterrupted))));
        }

        private static void Resign(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32),
                    0, bytes, bytes.Length - 32, 32);
        }
    }
}
