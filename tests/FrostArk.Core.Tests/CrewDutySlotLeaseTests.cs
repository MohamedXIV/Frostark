using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Events;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CrewDutySlotLeaseTests
    {
        private static readonly EntityId Worker = EntityId.Parse("crew:lease:worker");
        private static readonly EntityId Peer = EntityId.Parse("crew:lease:peer");
        private static readonly EntityId Machine = EntityId.Parse("module:lease:machine");
        private static readonly EntityId Work = EntityId.Parse("slot:lease:work");
        private static readonly EntityId Rest = EntityId.Parse("slot:lease:rest");
        private static readonly EntityId Eat = EntityId.Parse("slot:lease:eat");

        private static WorldSession Session()
        {
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Worker, DutyRole.Engineering, 900, 200, 0, 1440),
                new CrewMember(Peer, DutyRole.Watch, 800, 150, 0, 1440)
            }, interactions: CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(Work, Machine, CrewInteractionVerb.Work, 0, 1, 0),
                new CrewInteractionSlot(Rest, Machine, CrewInteractionVerb.Rest, 1, 0, 0),
                new CrewInteractionSlot(Eat, Machine, CrewInteractionVerb.Eat, 1, 1, 0)
            }));
            return new WorldSession(WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), new[] { Worker, Peer }, crewDuty: crew));
        }

        private static RenewCrewDutySlotCommand Renew(EntityId crew, EntityId slot,
            long revision, string id = "renew", int lifetime = 15) =>
            new RenewCrewDutySlotCommand(EntityId.Parse("cmd:lease:" + id), crew,
                slot, lifetime, revision);

        [Test]
        public void LongRunningWorkLeaseRenewsThroughRealWorldMinutesAndSaveRoundTrip()
        {
            using (var s = Session())
            {
                var claim = s.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:first"), Worker, Work, 15, 0));
                Assert.That(claim.Success, Is.True);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(15));
                s.Advance(10);
                var before = s.Capture().State;
                var renewal = s.Submit(Renew(Worker, Work, before.Revision));
                Assert.That(renewal.Success, Is.True);
                Assert.That(renewal.Events[0].Kind,
                    Is.EqualTo(WorldEventKind.CrewDutySlotRenewed));
                Assert.That(renewal.State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(25));
                Assert.That(s.Minute.Value, Is.EqualTo(10),
                    "Claim renewal does not tick WorldClock.");
                Assert.That(s.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1), "No new or duplicated slot owner.");
                var roundtrip = SaveCodec.TryDecode(SaveCodec.Encode(s.Capture()));
                Assert.That(roundtrip.Success, Is.True);
                Assert.That(roundtrip.Value.State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(25));
                s.Advance(10);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Not.Null);
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision, "again")).Success,
                    Is.True);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work)
                    .ExpiresAt.Value, Is.EqualTo(35));
                s.Advance(15);
                Assert.That(s.Capture().State.CrewDuty.Interactions.GetClaim(Work),
                    Is.Null, "An abandoned Work claim still expires without renewal.");
            }
        }

        [Test]
        public void EarlyOrRepeatedRenewalIsRejectedWithoutCommandRevisionChurn()
        {
            using (var s = Session())
            {
                Assert.That(s.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:start"), Worker, Work, 15, 0)).Success,
                    Is.True);
                var before = s.Capture().State;
                var early = s.Submit(Renew(Worker, Work, s.Revision));
                Assert.That(early.Failure, Is.EqualTo(CommandFailure.DutySlotLeaseNotDue));
                Assert.That(early.State, Is.SameAs(before));
                Assert.That(s.Revision, Is.EqualTo(1));
                s.Advance(10);
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision)).Success, Is.True);
                var next = s.Submit(Renew(Worker, Work, s.Revision, "duplicate-early"));
                Assert.That(next.Failure, Is.EqualTo(CommandFailure.DutySlotLeaseNotDue));
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision)).Failure,
                    Is.EqualTo(CommandFailure.DuplicateCommand));
            }
        }

        [Test]
        public void OtherCrewAndFoodSlotsCannotStealOrExtendDutyLeases()
        {
            using (var s = Session())
            {
                Assert.That(s.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:work"), Worker, Work, 15, 0)).Success,
                    Is.True);
                s.Advance(11);
                Assert.That(s.Submit(Renew(Peer, Work, s.Revision, "steal")).Failure,
                    Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
                Assert.That(s.Submit(Renew(Worker, Eat, s.Revision, "food")).Failure,
                    Is.EqualTo(CommandFailure.DutySlotLeaseUnsupported));
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision, "invalid-ttl", 0)).Failure,
                    Is.EqualTo(CommandFailure.InvalidClaimLifetime));
                Assert.That(s.Submit(Renew(Worker, Rest, s.Revision, "rest-not-held")).Failure,
                    Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
                Assert.That(s.Capture().State.CrewDuty.Interactions.Claims.Count,
                    Is.EqualTo(1));
                Assert.That(s.Revision, Is.EqualTo(1));
            }
        }

        [Test]
        public void ExpiredOrReleasedWorkLeaseCannotBeResurrectedByRenewal()
        {
            using (var s = Session())
            {
                Assert.That(s.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:expiry"), Worker, Work, 2, 0)).Success,
                    Is.True);
                s.Advance(2);
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision)).Failure,
                    Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
                Assert.That(s.Capture().State.CrewDuty.Interactions.Claims, Is.Empty);
            }
            using (var s = Session())
            {
                Assert.That(s.Submit(new ClaimCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:release-start"), Worker, Work, 15, 0)).Success,
                    Is.True);
                Assert.That(s.Submit(new ReleaseCrewInteractionCommand(
                    EntityId.Parse("cmd:lease:release"), Worker, Work, 1)).Success, Is.True);
                Assert.That(s.Submit(Renew(Worker, Work, s.Revision)).Failure,
                    Is.EqualTo(CommandFailure.InteractionClaimNotOwned));
            }
        }
    }
}
