using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShoreCrewTransferTests
    {
        private static readonly EntityId Engineer = EntityId.Parse("actor:engineer");
        private static readonly EntityId Scout = EntityId.Parse("actor:scout");
        private static readonly EntityId Repairer = EntityId.Parse("actor:repairer");
        private static readonly EntityId Missing = EntityId.Parse("actor:missing");
        private static readonly EntityId Scrap = EntityId.Parse("item:ship_scrap");
        private static readonly EntityId Pickup = EntityId.Parse("salvage:shore_cargo");
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId BoilerRoom = EntityId.Parse("room:boiler");
        private static readonly EntityId RepairJobId = EntityId.Parse("repair:hull");

        private static WorldSession NewSession()
        {
            var roster = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 950, 150, 0, 1440),
                new CrewMember(Scout, DutyRole.Watch, 850, 400, 0, 1440),
                new CrewMember(Repairer, DutyRole.Repair, 900, 100, 0, 1440)
            }, new[] { new RepairJob(RepairJobId, 3000) });
            var thermal = ShipThermalState.Create(new[]
            {
                new ThermalCompartment(BoilerRoom, 1000, 20_000_000, 2000, 700, 1000)
            }, Array.Empty<ThermalConnection>(),
                new BoilerConfiguration(BoilerRoom, 1, 10000));
            var inventory = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Scrap, 2, 100, 20) }),
                new[] { new SalvagePickup(Pickup, Scrap, 5) },
                5000, 5, 50000, 50);
            var world = WorldState.Create(new WorldMinute(1200),
                new[] { new SupplyStock(Fuel, 300) },
                new[] { Engineer, Scout, Repairer }, inventory: inventory,
                crewDuty: roster, thermal: thermal);
            return new WorldSession(world, seed: 41, ambientMilliC: _ => -20000);
        }

        [Test]
        public void GroupDispatchUsesCanonicalIdentityOrderAndOnlyOneWorldSession()
        {
            using (var session = NewSession())
            {
                var result = ShoreCrewTransfer.Transfer(session,
                    new[] { Scout, Engineer }, toShore: true,
                    transitionId: EntityId.Parse("transition:landing:001"));

                Assert.That(result.Success, Is.True);
                Assert.That(result.Failure, Is.EqualTo(CommandFailure.None));
                Assert.That(result.ActorIds, Is.EqualTo(new[] { Engineer, Scout }));
                Assert.That(result.CommandResults, Has.Count.EqualTo(2));
                Assert.That(session.Revision, Is.EqualTo(2));
                Assert.That(session.Capture().State.CrewDuty.Get(Engineer).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Scout).IsAway, Is.True);
                Assert.That(session.Capture().State.CrewDuty.Get(Repairer).IsAway, Is.False);
                Assert.That(session.Capture().State.HasCrew(Engineer), Is.True);
                Assert.That(session.Capture().State.HasCrew(Scout), Is.True);
            }
        }

        [Test]
        public void InvalidOrUnavailableMixedGroupIsRejectedBeforeAnyCrewChanges()
        {
            using (var session = NewSession())
            {
                var original = session.Capture().State.Fingerprint();
                var invalid = ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer, Missing }, true,
                    EntityId.Parse("transition:unknown"));
                Assert.That(invalid.Success, Is.False);
                Assert.That(invalid.Failure, Is.EqualTo(CommandFailure.UnknownCrew));
                Assert.That(session.Revision, Is.Zero);
                Assert.That(session.Capture().State.Fingerprint(), Is.EqualTo(original));
                Assert.That(session.Capture().State.CrewDuty.Get(Engineer).IsAway, Is.False);

                var first = ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer }, true, EntityId.Parse("transition:one"));
                Assert.That(first.Success, Is.True);
                var revision = session.Revision;
                var mixed = ShoreCrewTransfer.Transfer(session,
                    new[] { Scout, Engineer }, true, EntityId.Parse("transition:repeat"));
                Assert.That(mixed.Success, Is.False);
                Assert.That(mixed.Failure, Is.EqualTo(CommandFailure.CrewAvailabilityUnchanged));
                Assert.That(session.Revision, Is.EqualTo(revision));
                Assert.That(session.Capture().State.CrewDuty.Get(Scout).IsAway, Is.False);
            }
        }

        [Test]
        public void InvalidManifestDoesNotAccidentallyAdvanceTimeOrWorldRevision()
        {
            using (var session = NewSession())
            {
                Assert.Throws<ArgumentException>(() => ShoreCrewTransfer.Transfer(session,
                    Array.Empty<EntityId>(), true, EntityId.Parse("transition:empty")));
                Assert.Throws<ArgumentException>(() => ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer, Engineer }, true, EntityId.Parse("transition:duplicate")));
                Assert.Throws<ArgumentException>(() => ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer, default(EntityId) }, true,
                    EntityId.Parse("transition:invalid")));
                Assert.Throws<ArgumentException>(() => ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer }, true, default(EntityId)));
                Assert.That(session.Revision, Is.Zero);
                Assert.That(session.Minute.Value, Is.EqualTo(1200));
            }
        }

        [Test]
        public void ShoreSalvageAndShipCrewWorkSurviveSaveRestoreAndReturnWithoutDuplication()
        {
            byte[] saveWhileAshore;
            using (var session = NewSession())
            {
                var sent = ShoreCrewTransfer.Transfer(session,
                    new[] { Engineer }, true, EntityId.Parse("transition:depart"));
                Assert.That(sent.Success, Is.True);

                session.Advance(15);
                var salvage = session.Submit(new CollectSalvageCommand(
                    EntityId.Parse("command:shore:pickup"), Pickup, session.Revision));
                Assert.That(salvage.Success, Is.True);
                Assert.That(session.Capture().State.CrewDuty.GetRepair(RepairJobId).RemainingWork,
                    Is.LessThan(3000), "Other ship staff should continue their jobs while the engineer is away.");
                Assert.That(session.Capture().State.GetSupply(Fuel), Is.LessThan(300),
                    "Canonical ship heat and fuel continue during the shore visit.");
                saveWhileAshore = SaveCodec.Encode(session.Capture());
            }

            var load = SaveCodec.TryDecode(saveWhileAshore);
            Assert.That(load.Success, Is.True, load.Error.ToString());
            using (var restored = WorldSession.Restore(load.Value, ambientMilliC: _ => -20000))
            {
                var state = restored.Capture().State;
                Assert.That(state.CrewDuty.Get(Engineer).IsAway, Is.True);
                Assert.That(state.HasCrew(Engineer), Is.True);
                Assert.That(state.Inventory.IsCollected(Pickup), Is.True);
                Assert.That(state.Inventory.GetAmount(InventoryContainer.Pack, Scrap), Is.EqualTo(5));

                restored.Advance(10);
                var returned = ShoreCrewTransfer.Transfer(restored,
                    new[] { Engineer }, false, EntityId.Parse("transition:extract"));
                Assert.That(returned.Success, Is.True);
                Assert.That(restored.Capture().State.CrewDuty.Get(Engineer).IsAway, Is.False);
                var unload = restored.Submit(new UnloadToShipCommand(
                    EntityId.Parse("command:unload"), Scrap, 5, restored.Revision));
                Assert.That(unload.Success, Is.True);
                var duplicate = restored.Submit(new CollectSalvageCommand(
                    EntityId.Parse("command:duplicate_salvage"), Pickup, restored.Revision));
                Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.AlreadyCollected));
                Assert.That(restored.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, Scrap), Is.Zero);
                Assert.That(restored.Capture().State.Inventory.GetAmount(InventoryContainer.ShipHold, Scrap),
                    Is.EqualTo(5));
                Assert.That(restored.Capture().State.HasCrew(Engineer), Is.True);
            }
        }

        [Test]
        public void ContinuousAndSaveResumedShoreVisitMatchCanonicalWorldFingerprint()
        {
            byte[] saved;
            using (var partial = NewSession())
            {
                ShoreCrewTransfer.Transfer(partial, new[] { Engineer }, true,
                    EntityId.Parse("transition:depart_equal"));
                partial.Advance(8);
                saved = SaveCodec.Encode(partial.Capture());
            }

            var decoded = SaveCodec.TryDecode(saved);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            using (var resumed = WorldSession.Restore(decoded.Value, ambientMilliC: _ => -20000))
            using (var continuous = NewSession())
            {
                ShoreCrewTransfer.Transfer(continuous, new[] { Engineer }, true,
                    EntityId.Parse("transition:depart_equal"));
                continuous.Advance(8);

                foreach (var session in new[] { resumed, continuous })
                {
                    session.Advance(6);
                    Assert.That(session.Submit(new CollectSalvageCommand(
                        EntityId.Parse("command:collect_equal"), Pickup, session.Revision)).Success, Is.True);
                    Assert.That(ShoreCrewTransfer.Transfer(session, new[] { Engineer }, false,
                        EntityId.Parse("transition:return_equal")).Success, Is.True);
                }

                Assert.That(resumed.Capture().State.Fingerprint(),
                    Is.EqualTo(continuous.Capture().State.Fingerprint()));
                Assert.That(SaveCodec.Encode(resumed.Capture()),
                    Is.EqualTo(SaveCodec.Encode(continuous.Capture())));
            }
        }
    }
}
