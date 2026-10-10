using System;
using System.Security.Cryptography;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CombatSaveValidationTests
    {
        private static readonly EntityId Rifle = EntityId.Parse("item:rifle");
        private static readonly EntityId Ammo = EntityId.Parse("item:ammo");

        private static byte[] OneShot()
        {
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Rifle, 1, 3000, 1),
                new ItemDefinition(Ammo, 1, 10, 100)
            }), new[]
            {
                new SalvagePickup(EntityId.Parse("pickup:rifle"), Rifle, 1),
                new SalvagePickup(EntityId.Parse("pickup:ammo"), Ammo, 6)
            }, 10_000, 4, 10_000, 4);
            inventory = inventory.TryCollect(EntityId.Parse("pickup:rifle")).State;
            inventory = inventory.TryCollect(EntityId.Parse("pickup:ammo")).State;
            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(Rifle, Ammo, ammoPerShot: 1, cooldownTicks: 3)
            });
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), Array.Empty<EntityId>(),
                inventory: inventory, combat: combat);
            var fired = new WorldCommandBus().Execute(state,
                new FireWeaponCommand(EntityId.Parse("command:shot"), Rifle, 0));
            Assert.That(fired.Success, Is.True);
            var timer = new SimulationScheduler(new WorldClock(fired.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            return SaveCodec.Encode(SaveGameDto.Capture(fired.State, timer));
        }

        private static void ReplaceLastLongAndResign(byte[] bytes, int fromTail, long value)
        {
            Array.Copy(BitConverter.GetBytes(value), 0,
                bytes, bytes.Length - 32 - fromTail, sizeof(long));
            using (var hash = SHA256.Create())
                Array.Copy(hash.ComputeHash(bytes, 0, bytes.Length - 32),
                    0, bytes, bytes.Length - 32, 32);
        }

        [Test]
        public void ForgedShotCounterCannotExceedCommittedWorldCommands()
        {
            var encoded = OneShot();
            ReplaceLastLongAndResign(encoded, fromTail: 12, value: 400);
            var decoded = SaveCodec.TryDecode(encoded);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void ForgedFutureCooldownBeyondCurrentTickPlusAuthoredPeriodIsInvalid()
        {
            var encoded = OneShot();
            ReplaceLastLongAndResign(encoded, fromTail: 20, value: 10);
            var decoded = SaveCodec.TryDecode(encoded);
            Assert.That(decoded.Success, Is.False);
            Assert.That(decoded.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }
    }
}
