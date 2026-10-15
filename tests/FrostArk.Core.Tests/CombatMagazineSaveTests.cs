using System;
using System.Security.Cryptography;
using FrostArk.Core.Application;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CombatMagazineSaveTests
    {
        private static readonly EntityId Pistol = EntityId.Parse("item:save-pistol");
        private static readonly EntityId Ammo = EntityId.Parse("item:save-ammo");
        private static readonly EntityId PistolPickup = EntityId.Parse("pickup:save-pistol");
        private static readonly EntityId AmmoPickup = EntityId.Parse("pickup:save-ammo");

        private static WorldSession CreateSession(long magazineCapacity = 3, long reloadTicks = 10)
        {
            var catalog = new ItemCatalog(new[]
            {
                new ItemDefinition(Pistol, 1, 1000, 1),
                new ItemDefinition(Ammo, 1, 25, 100)
            });
            var inventory = InventoryState.Create(catalog, new[]
            {
                new SalvagePickup(PistolPickup, Pistol, 1),
                new SalvagePickup(AmmoPickup, Ammo, 9)
            }, 10000, 3, 10000, 3);
            inventory = inventory.TryCollect(PistolPickup).State;
            inventory = inventory.TryCollect(AmmoPickup).State;
            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(Pistol, Ammo, 1, 2,
                    magazineCapacity: magazineCapacity, reloadTicks: reloadTicks)
            });
            var state = WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                Array.Empty<EntityId>(), inventory: inventory, combat: combat);
            return new WorldSession(state, seed: 17);
        }

        private static WorldSession SaveRestore(WorldSession source)
        {
            var encoded = SaveCodec.Encode(source.Capture());
            Assert.That(BitConverter.ToUInt16(encoded, 8), Is.EqualTo(14));
            var result = SaveCodec.TryDecode(encoded);
            Assert.That(result.Success, Is.True, result.Error.ToString());
            Assert.That(SaveCodec.Encode(result.Value), Is.EqualTo(encoded),
                "The entire v14 payload must be stable through decode/encode.");
            return WorldSession.Restore(result.Value);
        }

        private static void Fire(WorldSession session, string id)
        {
            var result = session.Submit(new FireWeaponCommand(
                EntityId.Parse("command:" + id), Pistol, session.Revision));
            Assert.That(result.Success, Is.True, result.Failure.ToString());
        }

        [Test]
        public void PartialMagazineRoundTripPreservesDefinitionLoadedRoundsAndWorldFingerprint()
        {
            using (var original = CreateSession())
            {
                Fire(original, "first");
                Assert.That(original.Capture().State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(2));

                using (var loaded = SaveRestore(original))
                {
                    var a = original.Capture().State;
                    var b = loaded.Capture().State;
                    Assert.That(b.Combat.Get(Pistol).Definition.MagazineCapacity, Is.EqualTo(3));
                    Assert.That(b.Combat.Get(Pistol).Definition.ReloadTicks, Is.EqualTo(10));
                    Assert.That(b.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(2));
                    Assert.That(b.Inventory.GetAmount(InventoryContainer.Pack, Ammo), Is.EqualTo(9));
                    Assert.That(b.Fingerprint(), Is.EqualTo(a.Fingerprint()));

                    original.AdvanceCombatTick(2);
                    loaded.AdvanceCombatTick(2);
                    Fire(original, "second");
                    Fire(loaded, "second");
                    Assert.That(loaded.Capture().State.Fingerprint(),
                        Is.EqualTo(original.Capture().State.Fingerprint()));
                    Assert.That(loaded.Capture().State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(1));
                }
            }
        }

        [Test]
        public void EmptyMagazinePersistsAndMustBePaidToReloadAfterRestore()
        {
            using (var source = CreateSession())
            {
                for (int n = 0; n < 3; n++)
                {
                    source.AdvanceCombatTick(n * 2);
                    Fire(source, "spent-" + n);
                }
                Assert.That(source.Capture().State.Combat.Get(Pistol).MagazineRounds, Is.Zero);
                using (var loaded = SaveRestore(source))
                {
                    loaded.AdvanceCombatTick(6);
                    var denied = loaded.Submit(new FireWeaponCommand(
                        EntityId.Parse("command:empty"), Pistol, loaded.Revision));
                    Assert.That(denied.Failure, Is.EqualTo(CommandFailure.MagazineEmpty));
                    var paid = loaded.Submit(new ReloadWeaponCommand(
                        EntityId.Parse("command:reload"), Pistol, loaded.Revision));
                    Assert.That(paid.Success, Is.True);
                    Assert.That(loaded.Capture().State.Combat.Get(Pistol).MagazineRounds, Is.EqualTo(3));
                    Assert.That(loaded.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, Ammo),
                        Is.EqualTo(6));
                }
            }
        }

        [Test]
        public void JustReloadedCooldownRoundTripsAndContinuesDeterministically()
        {
            using (var source = CreateSession())
            {
                Fire(source, "one");
                source.AdvanceCombatTick(2);
                var reload = source.Submit(new ReloadWeaponCommand(
                    EntityId.Parse("command:reload-mid"), Pistol, source.Revision));
                Assert.That(reload.Success, Is.True);
                Assert.That(source.Capture().State.Combat.Get(Pistol).NextReadyTick, Is.EqualTo(12));
                Assert.That(source.Capture().State.Combat.Get(Pistol).ShotCount, Is.EqualTo(1));

                using (var loaded = SaveRestore(source))
                {
                    Assert.That(loaded.Capture().State.Fingerprint(),
                        Is.EqualTo(source.Capture().State.Fingerprint()));
                    Assert.That(loaded.Capture().State.Combat.Get(Pistol).NextReadyTick, Is.EqualTo(12));
                    loaded.AdvanceCombatTick(11);
                    var blocked = loaded.Submit(new FireWeaponCommand(
                        EntityId.Parse("command:too-early"), Pistol, loaded.Revision));
                    Assert.That(blocked.Failure, Is.EqualTo(CommandFailure.WeaponCoolingDown));
                    loaded.AdvanceCombatTick(12);
                    source.AdvanceCombatTick(12);
                    Fire(source, "after");
                    Fire(loaded, "after");
                    Assert.That(loaded.Capture().State.Fingerprint(),
                        Is.EqualTo(source.Capture().State.Fingerprint()));
                }
            }
        }

        [Test]
        public void MagazineFieldsAffectFingerprintEvenWhenAllOldFieldsMatch()
        {
            using (var a = CreateSession(3, 10))
            using (var b = CreateSession(4, 10))
            using (var c = CreateSession(3, 12))
            {
                Assert.That(a.Capture().State.Combat.Fingerprint(),
                    Is.Not.EqualTo(b.Capture().State.Combat.Fingerprint()));
                Assert.That(a.Capture().State.Combat.Fingerprint(),
                    Is.Not.EqualTo(c.Capture().State.Combat.Fingerprint()));
                Fire(a, "fingerprint");
                Assert.That(a.Capture().State.Combat.Fingerprint(),
                    Is.Not.EqualTo(c.Capture().State.Combat.Fingerprint()));
            }
        }

        [Test]
        public void ForgedMagazineRoundsAboveCapacityRejectEvenWithValidChecksum()
        {
            using (var source = CreateSession())
            {
                var blob = SaveCodec.Encode(source.Capture());
                // V14 suffix: count(int32), capacity(int64), reloadTicks(int64), magazineRounds(int64).
                // Last 8 bytes of signed payload are the magazine rounds.
                Array.Copy(BitConverter.GetBytes(999L), 0, blob, blob.Length - 32 - 8, 8);
                using (var sha = SHA256.Create())
                {
                    var signature = sha.ComputeHash(blob, 0, blob.Length - 32);
                    Array.Copy(signature, 0, blob, blob.Length - 32, 32);
                }
                var result = SaveCodec.TryDecode(blob);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Error, Is.EqualTo(SaveDecodeError.InvalidData));
            }
        }

        [Test]
        public void LegacyV13WithoutMagazineExtensionLoadsAsOldNonMagazineWeapon()
        {
            using (var source = CreateSession())
            {
                var v14 = SaveCodec.Encode(source.Capture());
                // Original v13 had no magazine extension; downgrade only the
                // dedicated 4+24 byte suffix for this exact one-weapon save.
                int payloadLength = BitConverter.ToInt32(v14, 10) - 28;
                var old = new byte[14 + payloadLength + 32];
                Array.Copy(v14, old, 14 + payloadLength);
                Array.Copy(BitConverter.GetBytes((ushort)13), 0, old, 8, 2);
                Array.Copy(BitConverter.GetBytes(payloadLength), 0, old, 10, 4);
                using (var sha = SHA256.Create())
                {
                    var signature = sha.ComputeHash(old, 0, old.Length - 32);
                    Array.Copy(signature, 0, old, old.Length - 32, 32);
                }
                var result = SaveCodec.TryDecode(old);
                Assert.That(result.Success, Is.True, result.Error.ToString());
                var weapon = result.Value.State.Combat.Get(Pistol);
                Assert.That(weapon.Definition.MagazineCapacity, Is.Zero);
                Assert.That(weapon.Definition.ReloadTicks, Is.Zero);
                Assert.That(weapon.MagazineRounds, Is.Zero);
            }
        }
    }
}
