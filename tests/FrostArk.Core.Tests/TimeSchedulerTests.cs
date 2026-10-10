using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class TimeSchedulerTests
    {
        private sealed class RecordingSystem : IWorldMinuteSystem
        {
            private readonly List<string> _log;

            public RecordingSystem(SimulationPhase phase, string stableKey, List<string> log)
            {
                Phase = phase;
                StableKey = stableKey;
                _log = log;
            }

            public SimulationPhase Phase { get; }
            public string StableKey { get; }
            public long TickCount { get; private set; }

            public void Tick(WorldMinute minute)
            {
                TickCount++;
                _log.Add(minute.Value + ":" + Phase + ":" + StableKey);
            }
        }

        [Test]
        public void SixtyOneMinuteAdvancesEqualOneSixtyMinuteAdvance()
        {
            var stepsLog = new List<string>();
            var batchLog = new List<string>();
            var steps = new SimulationScheduler(new WorldClock(new WorldMinute(120)),
                new[] { new RecordingSystem(SimulationPhase.Supplies, "fuel", stepsLog) }, 42);
            var batch = new SimulationScheduler(new WorldClock(new WorldMinute(120)),
                new[] { new RecordingSystem(SimulationPhase.Supplies, "fuel", batchLog) }, 42);

            for (int i = 0; i < 60; i++) steps.Advance(1);
            var result = batch.Advance(60);

            Assert.That(result.EndedAt.Value, Is.EqualTo(180));
            Assert.That(steps.Clock.Current, Is.EqualTo(batch.Clock.Current));
            Assert.That(stepsLog, Is.EqualTo(batchLog));
            Assert.That(stepsLog, Has.Count.EqualTo(60));
        }

        [Test]
        public void SystemsRunByPhaseThenOrdinalStableKeyRegardlessOfRegistrationOrder()
        {
            var log = new List<string>();
            var systems = new[]
            {
                new RecordingSystem(SimulationPhase.Heat, "z", log),
                new RecordingSystem(SimulationPhase.Supplies, "z", log),
                new RecordingSystem(SimulationPhase.Supplies, "a", log),
                new RecordingSystem(SimulationPhase.Crew, "a", log)
            };
            var scheduler = new SimulationScheduler(new WorldClock(), systems, 17);

            scheduler.Advance(1);

            Assert.That(log, Is.EqualTo(new[]
            {
                "1:Crew:a", "1:Supplies:a", "1:Supplies:z", "1:Heat:z"
            }));
        }

        [Test]
        public void SameMinuteEventOrderIsSeededAndIndependentOfEnqueueOrder()
        {
            string[] Run(bool reverse)
            {
                var scheduler = new SimulationScheduler(new WorldClock(), Array.Empty<IWorldMinuteSystem>(), 777);
                var ids = new[] { "hazard:z", "hazard:a", "hazard:ice" };
                if (reverse) Array.Reverse(ids);
                foreach (var id in ids)
                    scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse(id), new WorldMinute(5), "alert"));
                return scheduler.Advance(5).FiredEvents.Select(e => e.Id.Value).ToArray();
            }

            var a = Run(false);
            var b = Run(true);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.EquivalentTo(new[] { "hazard:z", "hazard:a", "hazard:ice" }));
        }

        [Test]
        public void SleepFrom2200StopsExactlyAt0315HazardAfterWorldSystemsTick()
        {
            var log = new List<string>();
            var fuel = new RecordingSystem(SimulationPhase.Supplies, "fuel", log);
            var scheduler = new SimulationScheduler(new WorldClock(new WorldMinute(1320)), new[] { fuel });
            var hazard = new ScheduledWorldEvent(EntityId.Parse("event:raid"),
                new WorldMinute(1635), "raider-alert", interruptsSleep: true);
            scheduler.Schedule(hazard);

            var result = scheduler.AdvanceUntil(new WorldMinute(1800), new SleepInterruptionPolicy());

            Assert.That(result.WasInterrupted, Is.True);
            Assert.That(result.EndedAt.Value, Is.EqualTo(1635));
            Assert.That(result.Interruption, Is.SameAs(hazard));
            Assert.That(result.FiredEvents, Has.Count.EqualTo(1));
            Assert.That(fuel.TickCount, Is.EqualTo(315));
            Assert.That(log.Last(), Is.EqualTo("1635:Supplies:fuel"));
            Assert.That(scheduler.Clock.Current.Value, Is.EqualTo(1635));
        }

        [Test]
        public void NonInterruptingEventsDoNotStopSleep()
        {
            var scheduler = new SimulationScheduler(new WorldClock(new WorldMinute(1)),
                Array.Empty<IWorldMinuteSystem>());
            scheduler.Schedule(new ScheduledWorldEvent(EntityId.Parse("event:weather"),
                new WorldMinute(3), "weather-change"));

            var result = scheduler.AdvanceUntil(new WorldMinute(6), new SleepInterruptionPolicy());

            Assert.That(result.WasInterrupted, Is.False);
            Assert.That(result.Interruption, Is.Null);
            Assert.That(result.EndedAt.Value, Is.EqualTo(6));
            Assert.That(result.FiredEvents, Has.Count.EqualTo(1));
        }

        [Test]
        public void InvalidSchedulingAndTimeAdvanceLeaveClockUnchanged()
        {
            var clock = new WorldClock(new WorldMinute(10));
            var scheduler = new SimulationScheduler(clock, Array.Empty<IWorldMinuteSystem>());
            var hazard = new ScheduledWorldEvent(EntityId.Parse("event:unique"),
                new WorldMinute(12), "alert");
            scheduler.Schedule(hazard);

            Assert.Throws<InvalidOperationException>(() => scheduler.Schedule(hazard));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(
                new ScheduledWorldEvent(EntityId.Parse("event:past"), new WorldMinute(10), "past")));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Advance(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceUntil(new WorldMinute(9)));
            Assert.That(clock.Current.Value, Is.EqualTo(10));
        }

        [Test]
        public void EmptyAdvanceHasNoSideEffectsAndOverflowIsRejected()
        {
            var log = new List<string>();
            var clock = new WorldClock(new WorldMinute(long.MaxValue));
            var scheduler = new SimulationScheduler(clock, new[]
            {
                new RecordingSystem(SimulationPhase.Crew, "crew", log)
            });
            var empty = scheduler.Advance(0);

            Assert.That(empty.EndedAt, Is.EqualTo(clock.Current));
            Assert.That(log, Is.Empty);
            Assert.Throws<OverflowException>(() => scheduler.Advance(1));
            Assert.That(clock.Current.Value, Is.EqualTo(long.MaxValue));
        }
    }
}
