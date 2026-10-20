using System;
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
    public sealed class CrewProvisionInteractionTests
    {
        private static readonly EntityId A = EntityId.Parse("crew:engineer");
        private static readonly EntityId B = EntityId.Parse("crew:backup");
        private static readonly EntityId Food = EntityId.Parse("item:ration");
        private static readonly EntityId Water = EntityId.Parse("item:water");
        private static readonly EntityId EatSlot = EntityId.Parse("slot:galley:eat");
        private static readonly EntityId DrinkSlot = EntityId.Parse("slot:galley:drink");
        private static readonly EntityId WorkSlot = EntityId.Parse("slot:engine:work");
        private static readonly EntityId Galley = EntityId.Parse("object:galley");

        private static CrewInteractionState Slots() =>
            CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(DrinkSlot, Galley, CrewInteractionVerb.Drink, 1, 1, 2),
                new CrewInteractionSlot(EatSlot, Galley, CrewInteractionVerb.Eat, 1, 0, 2),
                new CrewInteractionSlot(WorkSlot, EntityId.Parse("object:engine"),
                    CrewInteractionVerb.Work, 2, 0, 1)
            });

        private static InventoryState Storage(bool stocked = true)
        {
            var initial = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Food, 1, 200, 10),
                new ItemDefinition(Water, 1, 250, 10)
            }), new[]
            {
                new SalvagePickup(EntityId.Parse("pickup:meal"), Food, 2),
                new SalvagePickup(EntityId.Parse("pickup:drink"), Water, 2)
            }, 10000, 5, 10000, 5);
            if (!stocked) return initial;
            return initial.TryCollect(EntityId.Parse("pickup:meal")).State
                .TryCollect(EntityId.Parse("pickup:drink")).State
                .TryUnload(Food, 2).State
                .TryUnload(Water, 2).State;
        }

        private static WorldState World(int hunger = 900, int thirst = 950,
            bool stocked = true, bool away = false, int wounds = 0)
        {
            var duty = CrewState.Create(new[]
            {
                new CrewMember(A, DutyRole.Engineering, 900, 0, 0, 1440,
                    hunger: hunger, thirst: thirst, isAway: away, wounds: wounds),
                new CrewMember(B, DutyRole.Watch, 850, 600, 0, 1440)
            }, consumables: new[]
            {
                new CrewConsumable(Food, 500, 0),
                new CrewConsumable(Water, 0, 600)
            }, interactions: Slots());
            return WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                new[] { A, B }, crewDuty: duty, inventory: Storage(stocked));
        }

        private static ClaimCrewInteractionCommand Claim(string name, EntityId crew,
            EntityId slot, long revision, int minutes = 15) =>
            new ClaimCrewInteractionCommand(EntityId.Parse("cmd:" + name), crew,
                slot, minutes, revision);
        private static UseCrewProvisionInteractionCommand Use(string name,
            EntityId crew, EntityId slot, EntityId item, long revision) =>
            new UseCrewProvisionInteractionCommand(EntityId.Parse("cmd:" + name),
                crew, slot, item, revision);

        [Test]
        public void ReadOnlyDiscoveryUsesNeedsStockAndAvailableSlotsWithoutClaiming()
        {
            var state = World();
            var before = state.Fingerprint();
            var offers = CrewProvisionInteractionDiscovery.Find(state, A);
            Assert.That(offers, Has.Count.EqualTo(2));
            Assert.That(offers[0].SlotId, Is.EqualTo(DrinkSlot));
            Assert.That(offers[0].ItemId, Is.EqualTo(Water));
            Assert.That(offers[0].NeedSeverity, Is.EqualTo(950));
            Assert.That(offers[1].SlotId, Is.EqualTo(EatSlot));
            Assert.That(offers[1].ItemId, Is.EqualTo(Food));
            Assert.That(state.Fingerprint(), Is.EqualTo(before));
            Assert.That(state.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(CrewProvisionInteractionDiscovery.Find(World(stocked: false), A),
                Is.Empty);
            Assert.That(CrewProvisionInteractionDiscovery.Find(World(away: true), A),
                Is.Empty);
            Assert.That(CrewProvisionInteractionDiscovery.Find(World(wounds: 1000), A),
                Is.Empty);
        }

        [Test]
        public void SuccessfulClaimedMealAtomicallySpendsFoodReducesHungerAndReleasesSlot()
        {
            var state = World();
            var bus = new WorldCommandBus();
            var claim = bus.Execute(state, Claim("claim-meal", A, EatSlot, 0));
            Assert.That(claim.Success, Is.True);
            Assert.That(CrewProvisionInteractionDiscovery.Find(claim.State, A), Is.Empty,
                "Already reserved crew cannot seek a second slot.");
            var used = bus.Execute(claim.State, Use("use-meal", A, EatSlot, Food, 1));
            Assert.That(used.Success, Is.True);
            Assert.That(used.State.Revision, Is.EqualTo(2));
            Assert.That(used.Events, Has.Count.EqualTo(1));
            Assert.That(used.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewProvisionConsumed));
            Assert.That(used.State.CrewDuty.Get(A)!.Hunger, Is.EqualTo(400));
            Assert.That(used.State.CrewDuty.Get(A)!.Thirst, Is.EqualTo(950));
            Assert.That(used.State.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(1));
            Assert.That(used.State.CrewDuty.Interactions.GetClaim(EatSlot), Is.Null);
            Assert.That(claim.State.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(2), "The source snapshot must remain immutable.");
            Assert.That(used.State.CrewDuty.EffectiveSkill(DutyRole.Engineering,
                new WorldMinute(1)), Is.Zero, "Thirst still takes priority.");

            var retry = bus.Execute(used.State, Use("use-meal", A, EatSlot, Food, 2));
            Assert.That(retry.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(retry.State, Is.SameAs(used.State));
            var newClaim = bus.Execute(used.State, Claim("next", B, EatSlot, 2));
            Assert.That(newClaim.Success, Is.True);
        }

        [Test]
        public void DrinkingAfterEatingRestoresEligibilityAndLeavesTwoIndependentEvents()
        {
            var bus = new WorldCommandBus();
            var first = bus.Execute(World(), Claim("claim-food", A, EatSlot, 0));
            var eaten = bus.Execute(first.State, Use("eat", A, EatSlot, Food, 1));
            var drinkClaim = bus.Execute(eaten.State, Claim("claim-water", A, DrinkSlot, 2));
            var drunk = bus.Execute(drinkClaim.State,
                Use("drink", A, DrinkSlot, Water, 3));
            Assert.That(drunk.Success, Is.True);
            Assert.That(drunk.State.CrewDuty.Get(A)!.Hunger, Is.EqualTo(400));
            Assert.That(drunk.State.CrewDuty.Get(A)!.Thirst, Is.EqualTo(350));
            Assert.That(drunk.State.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(drunk.State.Inventory.GetAmount(InventoryContainer.ShipHold, Water),
                Is.EqualTo(1));
            Assert.That(drunk.State.CrewDuty.EffectiveSkill(DutyRole.Engineering,
                new WorldMinute(1)), Is.EqualTo(900));
        }

        [Test]
        public void WrongOwnerOrUnclaimedOrWrongVerbCannotConsumeOrRelease()
        {
            var bus = new WorldCommandBus();
            var original = World();
            var noClaim = bus.Execute(original, Use("unclaimed", A, EatSlot, Food, 0));
            Assert.That(noClaim.Failure, Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
            Assert.That(noClaim.State, Is.SameAs(original));

            var claimed = bus.Execute(original, Claim("owned", A, EatSlot, 0));
            var impostor = bus.Execute(claimed.State,
                Use("impostor", B, EatSlot, Food, 1));
            Assert.That(impostor.Failure, Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
            var mismatch = bus.Execute(claimed.State,
                Use("wrong-kind", A, EatSlot, Water, 1));
            Assert.That(mismatch.Failure, Is.EqualTo(CommandFailure.InteractionVerbMismatch));
            var work = bus.Execute(original, Claim("work", A, WorkSlot, 0));
            var wrongStation = bus.Execute(work.State,
                Use("not-a-galley", A, WorkSlot, Food, 1));
            Assert.That(wrongStation.Failure, Is.EqualTo(CommandFailure.InteractionVerbMismatch));
            foreach (var rejected in new[] { impostor, mismatch, wrongStation })
            {
                Assert.That(rejected.Events, Is.Empty);
                Assert.That(rejected.State.CrewDuty.Interactions.Claims, Has.Count.EqualTo(1));
                Assert.That(rejected.State.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                    Is.EqualTo(2));
            }
        }

        [Test]
        public void EmptyStockAndSatisfiedNeedRejectWithoutDroppingClaim()
        {
            var bus = new WorldCommandBus();
            var noStock = bus.Execute(World(stocked: false), Claim("empty", A, EatSlot, 0));
            var empty = bus.Execute(noStock.State, Use("no-food", A, EatSlot, Food, 1));
            Assert.That(empty.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
            Assert.That(empty.State, Is.SameAs(noStock.State));
            Assert.That(empty.State.CrewDuty.Interactions.GetClaim(EatSlot), Is.Not.Null);

            var full = bus.Execute(World(hunger: 0, thirst: 900),
                Claim("full", A, EatSlot, 0));
            var unnecessary = bus.Execute(full.State,
                Use("not-hungry", A, EatSlot, Food, 1));
            Assert.That(unnecessary.Failure, Is.EqualTo(CommandFailure.NeedAlreadySatisfied));
            Assert.That(unnecessary.State, Is.SameAs(full.State));
        }

        [Test]
        public void ExpiredClaimAndStaleRevisionCannotSpend()
        {
            var bus = new WorldCommandBus();
            var state = World();
            var claim = bus.Execute(state, Claim("short", A, DrinkSlot, 0, 1));
            var stale = bus.Execute(claim.State, Use("stale", A, DrinkSlot, Water, 0));
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(stale.State, Is.SameAs(claim.State));

            // An expiry at the exact current clock boundary is not valid, even
            // when a scene adapter calls Use before the next CrewState.Tick.
            var tickedDuty = claim.State.CrewDuty;
            var advanced = WorldState.Create(new WorldMinute(1),
                Array.Empty<SupplyStock>(), new[] { A, B }, crewDuty: tickedDuty,
                inventory: claim.State.Inventory);
            var expired = bus.Execute(advanced, Use("expired", A, DrinkSlot, Water, 0));
            Assert.That(expired.Failure, Is.EqualTo(CommandFailure.InteractionClaimExpired));
            Assert.That(expired.State, Is.SameAs(advanced));
            Assert.That(advanced.Inventory.GetAmount(InventoryContainer.ShipHold, Water),
                Is.EqualTo(2));
        }

        [Test]
        public void DiscoveryReevaluatesWithoutStaleOffersAfterClaimAndMeal()
        {
            var bus = new WorldCommandBus();
            var original = World(hunger: 950, thirst: 0);
            var offers = CrewProvisionInteractionDiscovery.Find(original, A);
            Assert.That(offers, Has.Count.EqualTo(1));
            Assert.That(offers[0].ItemId, Is.EqualTo(Food));

            var held = bus.Execute(original, Claim("offer-hold", A, offers[0].SlotId, 0));
            Assert.That(held.Success, Is.True);
            Assert.That(CrewProvisionInteractionDiscovery.Find(held.State, A), Is.Empty);
            var finished = bus.Execute(held.State,
                Use("offer-use", A, offers[0].SlotId, offers[0].ItemId, 1));
            Assert.That(finished.Success, Is.True);
            Assert.That(finished.State.CrewDuty.Get(A)!.Hunger, Is.EqualTo(450));
            Assert.That(finished.State.CrewDuty.Interactions.Claims, Is.Empty);
            var remaining = CrewProvisionInteractionDiscovery.Find(finished.State, A);
            Assert.That(remaining, Has.Count.EqualTo(1));
            Assert.That(remaining[0].NeedSeverity, Is.EqualTo(450));
            Assert.That(remaining[0].SlotId, Is.EqualTo(EatSlot));
            Assert.That(remaining[0].ItemId, Is.EqualTo(Food));
        }

        [Test]
        public void SaveResumeWhileClaimedThenUseConsumesExactlyOnce()
        {
            var bus = new WorldCommandBus();
            var held = bus.Execute(World(), Claim("held", A, EatSlot, 0, 40));
            var scheduler = new SimulationScheduler(new WorldClock(held.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var before = SaveCodec.Encode(SaveGameDto.Capture(held.State, scheduler));
            var decoded = SaveCodec.TryDecode(before);
            Assert.That(decoded.Success, Is.True, decoded.Error.ToString());
            Assert.That(SaveCodec.Encode(decoded.Value), Is.EqualTo(before));
            var completed = bus.Execute(decoded.Value.State,
                Use("finish-after-load", A, EatSlot, Food, 1));
            Assert.That(completed.Success, Is.True);
            Assert.That(completed.State.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(completed.State.Inventory.GetAmount(InventoryContainer.ShipHold, Food),
                Is.EqualTo(1));
            var duplicate = bus.Execute(completed.State,
                Use("finish-after-load", A, EatSlot, Food, 2));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
        }
    }
}
