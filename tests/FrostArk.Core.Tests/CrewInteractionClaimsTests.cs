using System;
using System.Security.Cryptography;
using System.Text;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewInteractionClaimsTests
    {
        private static readonly EntityId A = EntityId.Parse("crew:engineer");
        private static readonly EntityId B = EntityId.Parse("crew:medic");
        private static readonly EntityId EatSlot = EntityId.Parse("slot:galley:0");
        private static readonly EntityId DrinkSlot = EntityId.Parse("slot:galley:1");
        private static readonly EntityId Galley = EntityId.Parse("object:galley");
        private static readonly EntityId MissingSlot = EntityId.Parse("slot:missing");

        private static CrewInteractionState Registry() =>
            CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(EatSlot, Galley, CrewInteractionVerb.Eat, 1, 0, 2),
                new CrewInteractionSlot(DrinkSlot, Galley, CrewInteractionVerb.Drink, 1, 1, 2)
            });

        private static CrewState Crew(bool aAway = false, int wounds = 0) =>
            CrewState.Create(new[]
            {
                new CrewMember(A, DutyRole.Engineering, 950, 0, 0, 1440,
                    wounds: wounds, isAway: aAway, hunger: 900),
                new CrewMember(B, DutyRole.Medical, 750, 0, 0, 1440)
            }, interactions: Registry());

        private static WorldState World(bool aAway = false, int wounds = 0) =>
            WorldState.Create(WorldMinute.Zero, Array.Empty<SupplyStock>(),
                new[] { A, B }, crewDuty: Crew(aAway, wounds));

        private static ClaimCrewInteractionCommand Claim(string id, EntityId member,
            EntityId slot, long revision, int ttl = 15) =>
            new ClaimCrewInteractionCommand(EntityId.Parse("cmd:" + id),
                member, slot, ttl, revision);
        private static ReleaseCrewInteractionCommand Release(string id, EntityId member,
            EntityId slot, long revision) =>
            new ReleaseCrewInteractionCommand(EntityId.Parse("cmd:" + id),
                member, slot, revision);

        [Test]
        public void AuthoredOffersAreSortedAndHaveDistinctLocalPositionAndVerb()
        {
            var state = Registry();
            Assert.That(state.Slots, Has.Count.EqualTo(2));
            Assert.That(state.GetSlot(EatSlot)!.Verb, Is.EqualTo(CrewInteractionVerb.Eat));
            Assert.That(state.GetSlot(DrinkSlot)!.LocalY, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(EatSlot, Galley, CrewInteractionVerb.Eat, 0, 1, 0),
                new CrewInteractionSlot(EatSlot, Galley, CrewInteractionVerb.Drink, 0, 2, 0)
            }));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CrewInteractionSlot(MissingSlot, Galley, CrewInteractionVerb.Work, 0, 0, 9));
        }

        [Test]
        public void ClaimIsExclusivePerSlotAndPerCrewWithoutInventingWorldProgress()
        {
            var start = World();
            var bus = new WorldCommandBus();
            var taken = bus.Execute(start, Claim("take", A, EatSlot, 0));
            Assert.That(taken.Success, Is.True);
            Assert.That(taken.Events, Has.Count.EqualTo(1));
            Assert.That(taken.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewSlotClaimed));
            Assert.That(taken.State.Revision, Is.EqualTo(1));
            Assert.That(taken.State.CrewDuty.Interactions.GetClaim(EatSlot)!.CrewId, Is.EqualTo(A));
            Assert.That(taken.State.CrewDuty.Get(A)!.Hunger, Is.EqualTo(900));
            Assert.That(start.CrewDuty.Interactions.Claims, Is.Empty);

            var occupied = bus.Execute(taken.State, Claim("blocked", B, EatSlot, 1));
            Assert.That(occupied.Failure, Is.EqualTo(CommandFailure.InteractionSlotBusy));
            Assert.That(occupied.State, Is.SameAs(taken.State));
            Assert.That(occupied.Events, Is.Empty);
            var doubleBooked = bus.Execute(taken.State, Claim("double", A, DrinkSlot, 1));
            Assert.That(doubleBooked.Failure, Is.EqualTo(CommandFailure.CrewHasInteractionClaim));
            Assert.That(doubleBooked.State, Is.SameAs(taken.State));

            var other = bus.Execute(taken.State, Claim("other", B, DrinkSlot, 1));
            Assert.That(other.Success, Is.True);
            Assert.That(other.State.CrewDuty.Interactions.Claims, Has.Count.EqualTo(2));
            Assert.That(other.State.Fingerprint(), Is.Not.EqualTo(start.Fingerprint()));
        }

        [Test]
        public void ReleaseRejectsNonownerAndInvalidReplayAndRestoresSlot()
        {
            var bus = new WorldCommandBus();
            var taken = bus.Execute(World(), Claim("one", A, EatSlot, 0));
            var nonowner = bus.Execute(taken.State, Release("wrong", B, EatSlot, 1));
            Assert.That(nonowner.Failure, Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
            Assert.That(nonowner.State, Is.SameAs(taken.State));
            var result = bus.Execute(taken.State, Release("done", A, EatSlot, 1));
            Assert.That(result.Success, Is.True);
            Assert.That(result.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewSlotReleased));
            Assert.That(result.State.CrewDuty.Interactions.Claims, Is.Empty);
            var replay = bus.Execute(result.State, Release("done", A, EatSlot, 2));
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            var empty = bus.Execute(result.State, Release("again", A, EatSlot, 2));
            Assert.That(empty.Failure, Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
            var next = bus.Execute(result.State, Claim("another", B, EatSlot, 2));
            Assert.That(next.Success, Is.True);
        }

        [Test]
        public void InvalidSlotLifetimeUnavailableActorAndStaleRevisionAreAtomic()
        {
            var bus = new WorldCommandBus();
            var state = World();
            Assert.That(bus.Execute(state, Claim("missing", A, MissingSlot, 0)).Failure,
                Is.EqualTo(CommandFailure.UnknownInteractionSlot));
            Assert.That(bus.Execute(state, Claim("invalid", A, EatSlot, 0, 0)).Failure,
                Is.EqualTo(CommandFailure.InvalidClaimLifetime));
            Assert.That(bus.Execute(state, Claim("stale", A, EatSlot, 7)).Failure,
                Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(bus.Execute(World(aAway: true), Claim("away", A, EatSlot, 0)).Failure,
                Is.EqualTo(CommandFailure.CrewNotOnShip));
            Assert.That(bus.Execute(World(wounds: 1000), Claim("injured", A, EatSlot, 0)).Failure,
                Is.EqualTo(CommandFailure.CrewIncapacitated));
            Assert.That(state.Revision, Is.Zero);
            Assert.That(state.CrewDuty.Interactions.Claims, Is.Empty);
        }

        [Test]
        public void ExpiryOccursOnlyOnCanonicalTickAndAllowsNewOwner()
        {
            var bus = new WorldCommandBus();
            var taken = bus.Execute(World(), Claim("first", A, EatSlot, 0, 2));
            var crew = taken.State.CrewDuty.Tick(new WorldMinute(1));
            Assert.That(crew.Interactions.GetClaim(EatSlot), Is.Not.Null);
            crew = crew.Tick(new WorldMinute(2));
            Assert.That(crew.Interactions.GetClaim(EatSlot), Is.Null);
            var world = WorldState.Create(new WorldMinute(2), Array.Empty<SupplyStock>(),
                new[] { A, B }, crewDuty: crew);
            var newOwner = bus.Execute(world, Claim("second", B, EatSlot, 0, 2));
            Assert.That(newOwner.Success, Is.True);
            Assert.That(newOwner.State.CrewDuty.Interactions.GetClaim(EatSlot)!.CrewId,
                Is.EqualTo(B));
        }

        [Test]
        public void WorldClockExpiryPreservesCommandProvenanceWithoutNewRevision()
        {
            var bus = new WorldCommandBus();
            var claimed = bus.Execute(World(), Claim("clock", A, EatSlot, 0, 2));
            Assert.That(claimed.Success, Is.True);
            var world = claimed.State;
            var scheduler = new SimulationScheduler(new WorldClock(world.Minute),
                new IWorldMinuteSystem[]
                {
                    new ShipCrewMinuteSystem(() => world, s => world = s),
                    new ShipThermalMinuteSystem(() => world, s => world = s, _ => 0)
                }, seed: 17);
            scheduler.Advance(2);
            Assert.That(world.Minute.Value, Is.EqualTo(2));
            Assert.That(world.CrewDuty.Interactions.GetClaim(EatSlot), Is.Null);
            Assert.That(world.Revision, Is.EqualTo(1),
                "Expiring a slot must not invent player-command provenance.");
            var duplicate = bus.Execute(world, Claim("clock", B, EatSlot, 1, 2));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            var retry = bus.Execute(world, Claim("retry", B, EatSlot, 1, 2));
            Assert.That(retry.Success, Is.True);
        }

        [Test]
        public void ShoreDispatchAndIncapacitatingWoundDropOwnedSlot()
        {
            var bus = new WorldCommandBus();
            var taken = bus.Execute(World(), Claim("first", A, EatSlot, 0));
            var away = bus.Execute(taken.State, new SetCrewAwayCommand(
                EntityId.Parse("cmd:shore"), A, true, 1));
            Assert.That(away.Success, Is.True);
            Assert.That(away.State.CrewDuty.Interactions.GetClaim(EatSlot), Is.Null);
            var injured = bus.Execute(taken.State, new DamageCrewCommand(
                EntityId.Parse("cmd:wound"), A, 1000, 1));
            Assert.That(injured.Success, Is.True);
            Assert.That(injured.State.CrewDuty.Interactions.GetClaim(EatSlot), Is.Null);
        }

        [Test]
        public void SaveV17PreservesOffersClaimsAndProvenanceByteIdentically()
        {
            var taken = new WorldCommandBus().Execute(World(),
                Claim("save", A, EatSlot, 0, 45));
            var timer = new SimulationScheduler(new WorldClock(taken.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var snapshot = SaveGameDto.Capture(taken.State, timer);
            var bytes = SaveCodec.Encode(snapshot);
            var read = SaveCodec.TryDecode(bytes);
            Assert.That(read.Success, Is.True, read.Error.ToString());
            Assert.That(read.Value.State.CrewDuty.Interactions.Slots, Has.Count.EqualTo(2));
            Assert.That(read.Value.State.CrewDuty.Interactions.GetClaim(EatSlot)!.CrewId,
                Is.EqualTo(A));
            Assert.That(read.Value.State.Fingerprint(), Is.EqualTo(taken.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(read.Value), Is.EqualTo(bytes));
            var duplicate = new WorldCommandBus().Execute(read.Value.State,
                Claim("save", A, EatSlot, 1, 45));
            Assert.That(duplicate.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
        }

        [Test]
        public void InvalidChecksummedClaimExpiryIsRejected()
        {
            var taken = new WorldCommandBus().Execute(World(),
                Claim("save", A, EatSlot, 0, 45));
            var timer = new SimulationScheduler(new WorldClock(taken.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var bytes = SaveCodec.Encode(SaveGameDto.Capture(taken.State, timer));
            // Claim's final field in v17 payload is signed int64 expiry.
            Array.Copy(BitConverter.GetBytes(-1L), 0, bytes, bytes.Length - 32 - 8, 8);
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(bytes, 0, bytes.Length - 32),
                    0, bytes, bytes.Length - 32, 32);
            var result = SaveCodec.TryDecode(bytes);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void V16WithoutAnySlotsDoesNotInventClaims()
        {
            var state = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { A },
                crewDuty: CrewState.Create(new[]
                {
                    new CrewMember(A, DutyRole.Engineering, 900, 0, 0, 1440)
                }));
            var timer = new SimulationScheduler(new WorldClock(state.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var current = SaveCodec.Encode(SaveGameDto.Capture(state, timer));
            // With no authored slot or claim, v17 adds only two int32 counts.
            int payload = BitConverter.ToInt32(current, 10) - 8;
            var prior = new byte[14 + payload + 32];
            Array.Copy(current, prior, 14 + payload);
            prior[8] = 16; prior[9] = 0;
            Array.Copy(BitConverter.GetBytes(payload), 0, prior, 10, 4);
            using (var sha = SHA256.Create())
                Array.Copy(sha.ComputeHash(prior, 0, prior.Length - 32),
                    0, prior, prior.Length - 32, 32);
            var migrated = SaveCodec.TryDecode(prior);
            Assert.That(migrated.Success, Is.True, migrated.Error.ToString());
            Assert.That(migrated.Value.State.CrewDuty.Interactions.Claims, Is.Empty);
            Assert.That(migrated.Value.State.CrewDuty.Interactions.Slots, Is.Empty);
        }
    }
}
