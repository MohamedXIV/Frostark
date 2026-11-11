using System;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewAgencyPolicyTests
    {
        private static readonly EntityId A = EntityId.Parse("crew:engineer");
        private static readonly EntityId B = EntityId.Parse("crew:watch");
        private static readonly EntityId C = EntityId.Parse("crew:medic");
        private static readonly EntityId D = EntityId.Parse("crew:scout");
        private static readonly EntityId Water = EntityId.Parse("item:water");
        private static readonly EntityId Galley = EntityId.Parse("module:galley");
        private static readonly EntityId Slot = EntityId.Parse("slot:drink");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:water");

        private static WorldState World(int thirst = 0, int hunger = 0,
            bool stock = true, bool away = false, int wounds = 0)
        {
            var inventory = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Water, 1, 100, 10) }),
                new[] { new SalvagePickup(Pickup, Water, 2) },
                10000, 5, 10000, 5);
            if (stock)
                inventory = inventory.TryCollect(Pickup).State.TryUnload(Water, 2).State;
            var crew = CrewState.Create(new[]
            {
                new CrewMember(A, DutyRole.Engineering, 900, 300, 0, 1440,
                    thirst: thirst, hunger: hunger, isAway: away, wounds: wounds),
                new CrewMember(B, DutyRole.Watch, 800, 300, 0, 1440),
                new CrewMember(C, DutyRole.Medical, 800, 300, 0, 1440),
                new CrewMember(D, DutyRole.Repair, 800, 300, 0, 1440)
            }, consumables: new[] { new CrewConsumable(Water, 0, 600) },
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Slot, Galley,
                        CrewInteractionVerb.Drink, 0, 1, 2)
                }));
            return WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                new[] { A, B, C, D }, inventory: inventory, crewDuty: crew);
        }

        [Test]
        public void SelectedFourCrewHoldWhileUnselectedIdleCanResumeDuty()
        {
            var world = World();
            var before = world.Fingerprint();
            foreach (var id in new[] { A, B, C, D })
            {
                Assert.That(CrewAgencyPolicy.Evaluate(world, id, true, false).Kind,
                    Is.EqualTo(CrewAgencyDecisionKind.HoldForPlayerSelection));
                Assert.That(CrewAgencyPolicy.Evaluate(world, id, false, false).Kind,
                    Is.EqualTo(CrewAgencyDecisionKind.ReadyForDuty));
            }
            Assert.That(world.Fingerprint(), Is.EqualTo(before));
        }

        [Test]
        public void DeselectDuringAcceptedMoveKeepsOrderAboveOrdinaryAutonomy()
        {
            var world = World(thirst: 800);
            Assert.That(CrewAgencyPolicy.Evaluate(world, A, true, true).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.ContinuePlayerOrder));
            Assert.That(CrewAgencyPolicy.Evaluate(world, A, false, true).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.ContinuePlayerOrder));
            var arrived = CrewAgencyPolicy.Evaluate(world, A, false, false);
            Assert.That(arrived.Kind, Is.EqualTo(CrewAgencyDecisionKind.SeekProvision));
            Assert.That(arrived.HasProvision, Is.True);
            Assert.That(arrived.Provision.SlotId, Is.EqualTo(Slot));
            Assert.That(arrived.Provision.ItemId, Is.EqualTo(Water));
        }

        [Test]
        public void EmergencyNeedsCanRequestSafeHandoverButDoNotConjureWater()
        {
            var stocked = CrewAgencyPolicy.Evaluate(World(thirst: 975), A, true, true);
            Assert.That(stocked.Kind,
                Is.EqualTo(CrewAgencyDecisionKind.EmergencyNeedsHandover));
            Assert.That(stocked.HasProvision, Is.True);
            Assert.That(stocked.Severity, Is.EqualTo(975));

            var empty = CrewAgencyPolicy.Evaluate(World(thirst: 975, stock: false),
                A, true, true);
            Assert.That(empty.Kind, Is.EqualTo(CrewAgencyDecisionKind.UnmetCriticalNeed));
            Assert.That(empty.HasProvision, Is.False);
            var unselected = CrewAgencyPolicy.Evaluate(
                World(thirst: 900, stock: false), A, false, false);
            Assert.That(unselected.Kind, Is.EqualTo(CrewAgencyDecisionKind.UnmetCriticalNeed));
        }

        [Test]
        public void CriticalThresholdIsSeverityAndNeverInterpretedAsFull()
        {
            Assert.That(CrewAgencyPolicy.Evaluate(World(thirst: 849), A, true, false).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.HoldForPlayerSelection));
            Assert.That(CrewAgencyPolicy.Evaluate(World(thirst: 850), A, false, false).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.SeekProvision));
            Assert.That(CrewAgencyPolicy.Evaluate(World(thirst: 1000), A, false, true).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.EmergencyNeedsHandover));
        }

        [Test]
        public void AwayIncapacitatedAndUnknownCrewAreNeverScheduled()
        {
            foreach (var world in new[] { World(away: true), World(wounds: 1000) })
                Assert.That(CrewAgencyPolicy.Evaluate(world, A, false, false).Kind,
                    Is.EqualTo(CrewAgencyDecisionKind.Unavailable));
            Assert.That(CrewAgencyPolicy.Evaluate(World(),
                EntityId.Parse("crew:missing"), false, false).Kind,
                Is.EqualTo(CrewAgencyDecisionKind.Unavailable));
        }
    }
}
