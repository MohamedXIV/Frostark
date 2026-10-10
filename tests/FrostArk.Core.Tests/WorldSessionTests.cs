using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorldSessionTests
    {
        private static readonly EntityId Fuel = EntityId.Parse("supply:fuel");
        private static readonly EntityId Crew = EntityId.Parse("crew:01");

        private static WorldSession MakeSession() => new WorldSession(
            WorldState.Create(new WorldMinute(10),
                new[] { new SupplyStock(Fuel, 10) }, new[] { Crew }), seed: 23);

        [Test]
        public void NewSessionStartsWithOneCanonicalWorldAndClock()
        {
            using (var session = MakeSession())
            {
                Assert.That(session.Revision, Is.EqualTo(0));
                Assert.That(session.Minute.Value, Is.EqualTo(10));
                Assert.That(session.Read().GetSupply(Fuel), Is.EqualTo(10));
                Assert.That(session.Read().Minute, Is.EqualTo(session.Minute));
            }
        }

        [Test]
        public void CommandCommitsBeforeNotificationAndRejectionPublishesNothing()
        {
            using (var session = MakeSession())
            {
                var observed = new List<string>();
                session.Changed += change =>
                {
                    observed.Add(change.Kind + ":" + change.Revision + ":" +
                                 change.View.GetSupply(Fuel));
                    Assert.That(session.Read().GetSupply(Fuel),
                        Is.EqualTo(change.View.GetSupply(Fuel)));
                };
                var committed = session.Submit(new WithdrawSupplyCommand(
                    EntityId.Parse("command:1"), Fuel, 4, 0));
                var rejected = session.Submit(new WithdrawSupplyCommand(
                    EntityId.Parse("command:2"), Fuel, 100, 1));

                Assert.That(committed.Success, Is.True);
                Assert.That(rejected.Success, Is.False);
                Assert.That(session.Revision, Is.EqualTo(1));
                Assert.That(session.Read().GetSupply(Fuel), Is.EqualTo(6));
                Assert.That(observed, Is.EqualTo(new[] { "Command:1:6" }));
                Assert.That(committed.Events, Has.Count.EqualTo(1));
            }
        }

        [Test]
        public void TimeAdvanceUsesSameSchedulerAndUpdatesWorldMinuteWithoutChangingRevision()
        {
            using (var session = MakeSession())
            {
                var changes = new List<string>();
                session.Changed += change => changes.Add(change.Kind + ":" + change.View.Minute.Value);
                var first = session.Advance(5);
                var none = session.Advance(0);

                Assert.That(first.EndedAt.Value, Is.EqualTo(15));
                Assert.That(none.AdvancedMinutes, Is.Zero);
                Assert.That(session.Minute.Value, Is.EqualTo(15));
                Assert.That(session.Revision, Is.Zero);
                Assert.That(changes, Is.EqualTo(new[] { "Time:15" }));
            }
        }

        [Test]
        public void SaveAndRestorePreserveCommandsSeedAndPendingHazard()
        {
            using (var session = MakeSession())
            {
                session.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                    new WorldMinute(25), "raid", interruptsSleep: true));
                var command = new WithdrawSupplyCommand(EntityId.Parse("command:once"),
                    Fuel, 3, 0);
                Assert.That(session.Submit(command).Success, Is.True);
                session.Advance(4);
                var bytes = SaveCodec.Encode(session.Capture());
                var loaded = SaveCodec.TryDecode(bytes);
                Assert.That(loaded.Success, Is.True);

                using (var restored = WorldSession.Restore(loaded.Value))
                {
                    Assert.That(restored.Minute.Value, Is.EqualTo(14));
                    Assert.That(restored.Read().GetSupply(Fuel), Is.EqualTo(7));
                    Assert.That(restored.Submit(new WithdrawSupplyCommand(command.Id,
                        Fuel, 1, 1)).Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
                    var result = restored.AdvanceUntil(new WorldMinute(30),
                        new SleepInterruptionPolicy());
                    Assert.That(result.WasInterrupted, Is.True);
                    Assert.That(result.EndedAt.Value, Is.EqualTo(25));
                    Assert.That(restored.Minute, Is.EqualTo(result.EndedAt));
                }
            }
        }

        [Test]
        public void RestoredSessionPreservesProceduralOverridesOnSubsequentSave()
        {
            var initial = WorldState.Create(new WorldMinute(14),
                new[] { new SupplyStock(Fuel, 5) }, new[] { Crew });
            var scheduler = new SimulationScheduler(new WorldClock(initial.Minute),
                Array.Empty<IWorldMinuteSystem>(), seed: 291);
            var snapshot = SaveGameDto.Capture(initial, scheduler,
                new[] { new ProceduralOverride(EntityId.Parse("site:wreck-17"), "salvaged") });

            using (var session = WorldSession.Restore(snapshot))
            {
                session.Advance(3);
                var again = session.Capture();
                var decoded = SaveCodec.TryDecode(SaveCodec.Encode(again));

                Assert.That(decoded.Success, Is.True);
                Assert.That(decoded.Value.ProceduralOverrides, Has.Count.EqualTo(1));
                Assert.That(decoded.Value.ProceduralOverrides[0].Id,
                    Is.EqualTo(EntityId.Parse("site:wreck-17")));
                Assert.That(decoded.Value.ProceduralOverrides[0].Value, Is.EqualTo("salvaged"));
                Assert.That(decoded.Value.State.Minute.Value, Is.EqualTo(17));
            }
        }

        [Test]
        public void DisposeRejectsUseAndReleasesSubscriptions()
        {
            var session = MakeSession();
            session.Dispose();
            session.Dispose();

            Assert.Throws<ObjectDisposedException>(() => session.Read());
            Assert.Throws<ObjectDisposedException>(() => session.Advance(1));
            Assert.Throws<ObjectDisposedException>(() => session.Submit(
                new WithdrawSupplyCommand(EntityId.Parse("cmd:late"), Fuel, 1, 0)));
        }
    }
}
