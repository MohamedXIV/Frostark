using System;
using System.Linq;
using System.Security.Cryptography;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipModuleWorldTests
    {
        private static readonly EntityId Room = EntityId.Parse("room:engine");
        private static readonly EntityId Parts = EntityId.Parse("item:parts");
        private static readonly EntityId EngineType = EntityId.Parse("blueprint:engine");
        private static readonly EntityId BoilerType = EntityId.Parse("blueprint:boiler");
        private static readonly EntityId Engine = EntityId.Parse("module:engine");
        private static readonly EntityId Boiler = EntityId.Parse("module:boiler");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Engineer = EntityId.Parse("crew:engineer");
        private static readonly EntityId Repairer = EntityId.Parse("crew:repairer");

        private static WorldState Begin()
        {
            var inventory = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Parts, 1, 100, 100) }),
                new[] { new SalvagePickup(EntityId.Parse("pickup:parts"), Parts, 30) },
                100_000, 10, 100_000, 10);
            var layout = ShipLayoutState.Create(
                new[] { new ShipCompartmentSlot(Room, 4, 3) },
                new[]
                {
                    new ShipModuleBlueprint(EngineType, ShipModuleKind.Engine, 2, 2, Parts, 5, 3),
                    new ShipModuleBlueprint(BoilerType, ShipModuleKind.Boiler, 2, 2, Parts, 4, 2)
                }, Array.Empty<ShipModule>());
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 1000, 0, 0, 1440),
                new CrewMember(Repairer, DutyRole.Repair, 1000, 0, 0, 1440)
            });
            var thermal = ShipThermalState.Create(
                new[] { new ThermalCompartment(Room, 1000, 20_000_000, 0, 1000, 1000) },
                Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(Room, 1, 10000));
            var world = WorldState.Create(new WorldMinute(1320),
                new[] { new SupplyStock(Fuel, 400) },
                new[] { Engineer, Repairer }, inventory: inventory, thermal: thermal,
                crewDuty: crew, layout: layout);
            var bus = new WorldCommandBus();
            world = bus.Execute(world, new CollectSalvageCommand(
                EntityId.Parse("cmd:collect"), EntityId.Parse("pickup:parts"), world.Revision)).State;
            world = bus.Execute(world, new UnloadToShipCommand(
                EntityId.Parse("cmd:unload"), Parts, 30, world.Revision)).State;
            return world;
        }

        private static WorldState Build(WorldState world, EntityId id, EntityId type,
            int x, int y, string command)
        {
            var result = new WorldCommandBus().Execute(world,
                new BuildShipModuleCommand(EntityId.Parse(command), id, type, Room, x, y, world.Revision));
            Assert.That(result.Success, Is.True, result.Failure.ToString());
            return result.State;
        }

        private static SimulationScheduler Bind(Func<WorldState> read, Action<WorldState> commit) =>
            new SimulationScheduler(new WorldClock(read().Minute),
                new IWorldMinuteSystem[]
                {
                    new ShipCrewMinuteSystem(read, commit),
                    new ShipThermalMinuteSystem(read, commit, _ => 20_000)
                }, seed: 27);

        [Test]
        public void BuildConsumesRealShipPartsAndRejectsCollisionWithoutRevision()
        {
            var start = Begin();
            var engine = Build(start, Engine, EngineType, 0, 0, "cmd:engine");
            Assert.That(engine.Inventory.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(25));
            Assert.That(engine.Layout.GetModule(Engine).IntegrityPermille, Is.EqualTo(1000));
            var collide = new WorldCommandBus().Execute(engine, new BuildShipModuleCommand(
                EntityId.Parse("cmd:collision"), Boiler, BoilerType, Room, 1, 1, engine.Revision));
            Assert.That(collide.Success, Is.False);
            Assert.That(collide.Failure, Is.EqualTo(CommandFailure.InvalidPlacement));
            Assert.That(collide.State, Is.SameAs(engine));
            Assert.That(collide.Events, Is.Empty);
            var boiler = Build(engine, Boiler, BoilerType, 2, 0, "cmd:boiler");
            Assert.That(boiler.Inventory.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(21));
            Assert.That(boiler.Layout.Modules, Has.Count.EqualTo(2));
        }

        [Test]
        public void DamagedEngineReducesHeatAndPropulsionAndStaffedPaidRepairRestoresBoth()
        {
            var built = Build(Build(Begin(), Engine, EngineType, 0, 0, "cmd:engine"),
                Boiler, BoilerType, 2, 0, "cmd:boiler");
            var damaged = new WorldCommandBus().Execute(built, new DamageShipModuleCommand(
                EntityId.Parse("cmd:damage"), Engine, 600, built.Revision));
            Assert.That(damaged.Success, Is.True);
            Assert.That(damaged.State.Layout.PropulsionPermille, Is.EqualTo(400));
            Assert.That(damaged.State.Layout.BoilerOutputPermille, Is.EqualTo(400));

            var healthy = built;
            var broken = damaged.State;
            Bind(() => healthy, s => healthy = s).Advance(1);
            Bind(() => broken, s => broken = s).Advance(1);
            Assert.That(broken.Thermal.TotalEnergy, Is.LessThan(healthy.Thermal.TotalEnergy));

            var repair = new WorldCommandBus().Execute(broken, new StartShipModuleRepairCommand(
                EntityId.Parse("cmd:repair"), Engine, broken.Revision));
            Assert.That(repair.Success, Is.True, repair.Failure.ToString());
            Assert.That(repair.State.Inventory.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(18));
            Assert.That(repair.State.CrewDuty.GetRepair(Engine), Is.Not.Null);
            var state = repair.State;
            Bind(() => state, s => state = s).Advance(100);
            Assert.That(state.CrewDuty.GetRepair(Engine), Is.Null);
            Assert.That(state.Layout.GetModule(Engine).IntegrityPermille, Is.EqualTo(1000));
            Assert.That(state.Layout.PropulsionPermille, Is.EqualTo(1000));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(18));
        }

        [Test]
        public void RepairAndBuildNeverCreateFreePartsOrMutateOnRejectedCommands()
        {
            var world = Begin();
            var unknown = new WorldCommandBus().Execute(world, new BuildShipModuleCommand(
                EntityId.Parse("cmd:unknown"), Engine, EntityId.Parse("blueprint:missing"),
                Room, 0, 0, world.Revision));
            Assert.That(unknown.Success, Is.False);
            Assert.That(unknown.State, Is.SameAs(world));

            var built = Build(world, Engine, EngineType, 0, 0, "cmd:engine");
            var full = new WorldCommandBus().Execute(built, new StartShipModuleRepairCommand(
                EntityId.Parse("cmd:full"), Engine, built.Revision));
            Assert.That(full.Failure, Is.EqualTo(CommandFailure.ModuleNotDamaged));
            Assert.That(full.State, Is.SameAs(built));

            var damage = new WorldCommandBus().Execute(built, new DamageShipModuleCommand(
                EntityId.Parse("cmd:damage"), Engine, 600, built.Revision)).State;
            var queued = new WorldCommandBus().Execute(damage, new StartShipModuleRepairCommand(
                EntityId.Parse("cmd:repair"), Engine, damage.Revision)).State;
            var duplicate = new WorldCommandBus().Execute(queued, new StartShipModuleRepairCommand(
                EntityId.Parse("cmd:again"), Engine, queued.Revision));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.ModuleAlreadyRepairing));
            Assert.That(duplicate.State, Is.SameAs(queued));
            Assert.That(queued.Inventory.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(22));
        }

        [Test]
        public void UnpaidLegacyJobCannotHealMatchingModule()
        {
            var baseWorld = Build(Begin(), Engine, EngineType, 0, 0, "cmd:build");
            var damaged = new WorldCommandBus().Execute(baseWorld,
                new DamageShipModuleCommand(EntityId.Parse("cmd:hit"), Engine, 300,
                    baseWorld.Revision)).State;
            var rogueJob = CrewState.Create(damaged.CrewDuty.Members,
                new[] { new RepairJob(Engine, 1) });
            var forged = WorldState.Create(damaged.Minute,
                new[] { new SupplyStock(Fuel, damaged.GetSupply(Fuel)) },
                new[] { Engineer, Repairer }, inventory: damaged.Inventory,
                thermal: damaged.Thermal, crewDuty: rogueJob, layout: damaged.Layout);
            var tick = forged;
            Bind(() => tick, x => tick = x).Advance(1);
            Assert.That(tick.Layout.GetModule(Engine).IntegrityPermille, Is.EqualTo(700));
            Assert.That(tick.Layout.GetModule(Engine).RepairReserved, Is.False);
        }

        [Test]
        public void AwayRepairerCannotCompletePaidRepair()
        {
            var built = Build(Begin(), Engine, EngineType, 0, 0, "cmd:engine");
            var damaged = new WorldCommandBus().Execute(built,
                new DamageShipModuleCommand(EntityId.Parse("cmd:damage-away"), Engine, 400,
                    built.Revision)).State;
            var queued = new WorldCommandBus().Execute(damaged,
                new StartShipModuleRepairCommand(EntityId.Parse("cmd:repair-away"), Engine,
                    damaged.Revision)).State;
            var sent = new WorldCommandBus().Execute(queued,
                new SetCrewAwayCommand(EntityId.Parse("cmd:shore-repairer"), Repairer, true,
                    queued.Revision)).State;
            var state = sent;
            Bind(() => state, x => state = x).Advance(200);
            Assert.That(state.Layout.GetModule(Engine).IntegrityPermille, Is.EqualTo(600));
            Assert.That(state.CrewDuty.GetRepair(Engine).RemainingWork, Is.EqualTo(500));
        }

        [Test]
        public void SaveResumeDuringPaidRepairMatchesUninterruptedBytes()
        {
            var start = Build(Begin(), Engine, EngineType, 0, 0, "cmd:engine");
            start = new WorldCommandBus().Execute(start, new DamageShipModuleCommand(
                EntityId.Parse("cmd:damage"), Engine, 600, start.Revision)).State;
            start = new WorldCommandBus().Execute(start, new StartShipModuleRepairCommand(
                EntityId.Parse("cmd:repair"), Engine, start.Revision)).State;
            var full = start;
            var fullClock = Bind(() => full, s => full = s);
            fullClock.Advance(120);

            var partial = start;
            var before = Bind(() => partial, s => partial = s);
            before.Advance(31);
            var loaded = SaveCodec.TryDecode(SaveCodec.Encode(SaveGameDto.Capture(partial, before)));
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            var restored = loaded.Value.State;
            var after = loaded.Value.CreateScheduler(new IWorldMinuteSystem[]
            {
                new ShipCrewMinuteSystem(() => restored, s => restored = s),
                new ShipThermalMinuteSystem(() => restored, s => restored = s, _ => 20_000)
            });
            after.Advance(89);
            Assert.That(restored.Fingerprint(), Is.EqualTo(full.Fingerprint()));
            Assert.That(SaveCodec.Encode(SaveGameDto.Capture(restored, after)),
                Is.EqualTo(SaveCodec.Encode(SaveGameDto.Capture(full, fullClock))));
        }

        [Test]
        public void CostsConsumeOnlyRealShipHoldStock()
        {
            var inventory = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Parts, 1, 100, 100) }),
                new[] { new SalvagePickup(EntityId.Parse("pickup:unloaded"), Parts, 10) },
                100000, 10, 100000, 10);
            var collected = inventory.TryCollect(EntityId.Parse("pickup:unloaded"));
            Assert.That(collected.Success, Is.True);
            var denied = collected.State.TryConsumeShip(Parts, 3);
            Assert.That(denied.Success, Is.False);
            Assert.That(denied.State, Is.SameAs(collected.State));
            var moved = collected.State.TryUnload(Parts, 10);
            Assert.That(moved.Success, Is.True);
            var paid = moved.State.TryConsumeShip(Parts, 3);
            Assert.That(paid.Success, Is.True);
            Assert.That(paid.State.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(7));
            Assert.That(moved.State.GetAmount(InventoryContainer.ShipHold, Parts), Is.EqualTo(10));
        }

        [Test]
        public void ReservationFlagWithoutPaidCrewJobRejectsForgedSave()
        {
            var built = Build(Begin(), Engine, EngineType, 0, 0, "cmd:build-reservation-forge");
            var damaged = new WorldCommandBus().Execute(built,
                new DamageShipModuleCommand(EntityId.Parse("cmd:damage-reservation-forge"),
                    Engine, 300, built.Revision)).State;
            var scheduler = new SimulationScheduler(new WorldClock(damaged.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(damaged, scheduler));
            // The module reservation flag is the final byte in the v6 layout payload
            // when there is exactly one module.
            Assert.That(bytes[bytes.Length - 33], Is.Zero);
            bytes[bytes.Length - 33] = 1;
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32), 0,
                    bytes, bytes.Length - 32, 32);
            var result = SaveCodec.TryDecode(bytes);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void ForgedChecksumCannotAuthorizeUnknownModuleKind()
        {
            var built = Build(Begin(), Engine, EngineType, 0, 0, "cmd:module-forgery");
            var timer = new SimulationScheduler(new WorldClock(built.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(built, timer));
            var marker = System.Text.Encoding.UTF8.GetBytes("blueprint:engine");
            int found = -1;
            for (int i = 0; i <= bytes.Length - marker.Length - 32; i++)
            {
                bool match = true;
                for (int j = 0; j < marker.Length; j++)
                    if (bytes[i + j] != marker[j]) { match = false; break; }
                if (match) { found = i; break; }
            }
            Assert.That(found, Is.GreaterThan(0));
            bytes[found + marker.Length] = 255; // Serialized ShipModuleKind after the blueprint ID.
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32), 0,
                    bytes, bytes.Length - 32, 32);
            var loaded = SaveCodec.TryDecode(bytes);
            Assert.That(loaded.Success, Is.False);
            Assert.That(loaded.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void V5SaveMigratesToEmptyLayoutWithoutInventingShipModules()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>());
            var current = SaveCodec.Encode(SaveGameDto.Capture(state,
                new SimulationScheduler(new WorldClock(state.Minute), Array.Empty<IWorldMinuteSystem>())));
            Assert.That(current[8], Is.EqualTo(6));
            var old = new byte[current.Length - 12];
            Array.Copy(current, old, old.Length - 32);
            old[8] = 5;
            Array.Copy(BitConverter.GetBytes(old.Length - 46), 0, old, 10, 4);
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(old, 0, old.Length - 32), 0, old, old.Length - 32, 32);
            var loaded = SaveCodec.TryDecode(old);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.Layout.Modules, Is.Empty);
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(state.Fingerprint()));
        }
    }
}
