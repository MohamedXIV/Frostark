using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Events;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorldCommandTests
    {
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Crew = EntityId.Parse("crew:01");
        private static readonly EntityId Helm = EntityId.Parse("station:helm");
        private static readonly EntityId Boiler = EntityId.Parse("station:boiler");

        private static WorldState Initial() => WorldState.Create(
            new WorldMinute(1420),
            new[] { new SupplyStock(Fuel, 10) },
            new[] { Crew });

        [Test]
        public void SuccessfulWithdrawalCommitsImmutableStateAndProvenance()
        {
            var before = Initial();
            var cmd = new WithdrawSupplyCommand(EntityId.Parse("command:1"), Fuel, 4, 0);
            var result = new WorldCommandBus().Execute(before, cmd);

            Assert.That(result.Success, Is.True);
            Assert.That(before.GetSupply(Fuel), Is.EqualTo(10));
            Assert.That(before.Revision, Is.Zero);
            Assert.That(result.State.GetSupply(Fuel), Is.EqualTo(6));
            Assert.That(result.State.Revision, Is.EqualTo(1));
            Assert.That(result.Events, Has.Count.EqualTo(1));
            Assert.That(result.Events[0].Kind, Is.EqualTo(WorldEventKind.SupplyWithdrawn));
            Assert.That(result.Events[0].CommandId, Is.EqualTo(cmd.Id));
            Assert.That(result.Events[0].Minute.Value, Is.EqualTo(1420));
            Assert.That(result.Events[0].Revision, Is.EqualTo(1));
        }

        [Test]
        public void NegativeAndExcessiveWithdrawalRejectWithoutAnyPartialMutation()
        {
            var state = Initial();
            var bus = new WorldCommandBus();

            var negative = bus.Execute(state, new WithdrawSupplyCommand(EntityId.Parse("command:negative"), Fuel, -1, 0));
            var excess = bus.Execute(state, new WithdrawSupplyCommand(EntityId.Parse("command:excess"), Fuel, 11, 0));

            Assert.That(negative.Success, Is.False);
            Assert.That(negative.Failure, Is.EqualTo(CommandFailure.InvalidQuantity));
            Assert.That(excess.Success, Is.False);
            Assert.That(excess.Failure, Is.EqualTo(CommandFailure.InsufficientSupply));
            Assert.That(negative.State, Is.SameAs(state));
            Assert.That(excess.State, Is.SameAs(state));
            Assert.That(negative.Events, Is.Empty);
            Assert.That(excess.Events, Is.Empty);
            Assert.That(state.GetSupply(Fuel), Is.EqualTo(10));
            Assert.That(state.Revision, Is.Zero);
        }

        [Test]
        public void CrewCannotHoldTwoExclusiveAssignmentsAtTheSameRevision()
        {
            var bus = new WorldCommandBus();
            var first = bus.Execute(Initial(), new AssignCrewCommand(EntityId.Parse("command:first"), Crew, Helm, 0));
            Assert.That(first.Success, Is.True);
            Assert.That(first.State.GetCrewStation(Crew), Is.EqualTo(Helm));

            var second = bus.Execute(first.State,
                new AssignCrewCommand(EntityId.Parse("command:second"), Crew, Boiler, 1));

            Assert.That(second.Success, Is.False);
            Assert.That(second.Failure, Is.EqualTo(CommandFailure.CrewAlreadyAssigned));
            Assert.That(second.State, Is.SameAs(first.State));
            Assert.That(second.Events, Is.Empty);
            Assert.That(first.State.Revision, Is.EqualTo(1));
            Assert.That(first.State.GetCrewStation(Crew), Is.EqualTo(Helm));
        }

        [Test]
        public void OptimisticRevisionRejectsStaleConcurrentCommand()
        {
            var bus = new WorldCommandBus();
            var state = Initial();
            var first = bus.Execute(state, new AssignCrewCommand(EntityId.Parse("command:first"), Crew, Helm, 0));
            var stale = bus.Execute(first.State, new AssignCrewCommand(EntityId.Parse("command:stale"), Crew, Boiler, 0));

            Assert.That(stale.Success, Is.False);
            Assert.That(stale.Failure, Is.EqualTo(CommandFailure.StaleRevision));
            Assert.That(stale.State, Is.SameAs(first.State));
            Assert.That(stale.Events, Is.Empty);
        }

        [Test]
        public void ReplayingCommandIdCannotWithdrawTwice()
        {
            var bus = new WorldCommandBus();
            var command = new WithdrawSupplyCommand(EntityId.Parse("command:once"), Fuel, 3, 0);
            var first = bus.Execute(Initial(), command);
            var replay = bus.Execute(first.State,
                new WithdrawSupplyCommand(command.Id, Fuel, 3, 1));

            Assert.That(first.Success, Is.True);
            Assert.That(replay.Success, Is.False);
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
            Assert.That(replay.State.GetSupply(Fuel), Is.EqualTo(7));
            Assert.That(replay.Events, Is.Empty);
        }

        [Test]
        public void UnknownIdentityAndUnassignWithoutAssignmentRejectCleanly()
        {
            var state = Initial();
            var bus = new WorldCommandBus();
            var noCrew = bus.Execute(state, new AssignCrewCommand(EntityId.Parse("command:no-crew"),
                EntityId.Parse("crew:missing"), Helm, 0));
            var noSupply = bus.Execute(state, new WithdrawSupplyCommand(EntityId.Parse("command:no-supply"),
                EntityId.Parse("supply:missing"), 1, 0));
            var noAssignment = bus.Execute(state, new UnassignCrewCommand(EntityId.Parse("command:unassign"), Crew, 0));

            Assert.That(noCrew.Failure, Is.EqualTo(CommandFailure.UnknownCrew));
            Assert.That(noSupply.Failure, Is.EqualTo(CommandFailure.UnknownSupply));
            Assert.That(noAssignment.Failure, Is.EqualTo(CommandFailure.CrewNotAssigned));
            Assert.That(noCrew.State, Is.SameAs(state));
            Assert.That(noSupply.State, Is.SameAs(state));
            Assert.That(noAssignment.State, Is.SameAs(state));
        }

        [Test]
        public void UnassignCommitsFreeCrewAndDeterministicEvent()
        {
            var bus = new WorldCommandBus();
            var first = bus.Execute(Initial(), new AssignCrewCommand(EntityId.Parse("command:assign"), Crew, Helm, 0));
            var second = bus.Execute(first.State, new UnassignCrewCommand(EntityId.Parse("command:release"), Crew, 1));

            Assert.That(second.Success, Is.True);
            Assert.That(second.State.GetCrewStation(Crew).IsValid, Is.False);
            Assert.That(second.Events, Has.Count.EqualTo(1));
            Assert.That(second.Events[0].Kind, Is.EqualTo(WorldEventKind.CrewUnassigned));
            Assert.That(second.Events[0].Revision, Is.EqualTo(2));
            Assert.That(first.State.GetCrewStation(Crew), Is.EqualTo(Helm));
        }

        [Test]
        public void IdenticalCommandTraceYieldsIdenticalDigestAndEventOrder()
        {
            string Run()
            {
                var bus = new WorldCommandBus();
                var state = Initial();
                var one = bus.Execute(state, new WithdrawSupplyCommand(EntityId.Parse("command:1"), Fuel, 2, 0));
                var two = bus.Execute(one.State, new AssignCrewCommand(EntityId.Parse("command:2"), Crew, Boiler, 1));
                var three = bus.Execute(two.State, new UnassignCrewCommand(EntityId.Parse("command:3"), Crew, 2));
                Assert.That(one.Success && two.Success && three.Success, Is.True);
                var events = new[] { one.Events[0], two.Events[0], three.Events[0] };
                Assert.That(events[0].Revision, Is.EqualTo(1));
                Assert.That(events[1].Revision, Is.EqualTo(2));
                Assert.That(events[2].Revision, Is.EqualTo(3));
                return three.State.Fingerprint() + "|" + string.Join("|",
                    Array.ConvertAll(events, e => e.EventId.Value + ":" + e.Kind + ":" + e.Revision));
            }

            Assert.That(Run(), Is.EqualTo(Run()));
        }
    }
}
