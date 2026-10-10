using System;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class WorldTimePacerTests
    {
        [Test]
        public void SameElapsedRealMillisecondsProduceSameWorldStateAtDifferentFrameCadences()
        {
            var rate = new WorldTimeRate(1, 8000); // One world minute per eight real seconds.
            var smallPacer = new WorldTimePacer(rate);
            var largePacer = new WorldTimePacer(rate);
            var small = new SimulationScheduler(new WorldClock(), Array.Empty<IWorldMinuteSystem>());
            var large = new SimulationScheduler(new WorldClock(), Array.Empty<IWorldMinuteSystem>());

            for (int i = 0; i < 160; i++)
                small.Advance(smallPacer.ConsumeElapsedMilliseconds(100));
            large.Advance(largePacer.ConsumeElapsedMilliseconds(16000));

            Assert.That(small.Clock.Current.Value, Is.EqualTo(2));
            Assert.That(small.Clock.Current, Is.EqualTo(large.Clock.Current));
        }

        [Test]
        public void FractionalRealTimeAccumulatesWithoutTruncationLoss()
        {
            var pacer = new WorldTimePacer(new WorldTimeRate(3, 1000));
            for (int i = 0; i < 9; i++)
                Assert.That(pacer.ConsumeElapsedMilliseconds(100), Is.EqualTo(0));
            Assert.That(pacer.ConsumeElapsedMilliseconds(100), Is.EqualTo(3));
            Assert.That(pacer.ConsumeElapsedMilliseconds(1000), Is.EqualTo(3));
        }

        [Test]
        public void PausedRateDoesNotAdvanceWorldMinutes()
        {
            var pacer = new WorldTimePacer(new WorldTimeRate(0, 1000));
            Assert.That(pacer.ConsumeElapsedMilliseconds(100000), Is.Zero);
        }

        [Test]
        public void InvalidElapsedOrRateDoesNotSilentlyAdvanceThePacer()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTimeRate(-1, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTimeRate(1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldTimePacer(new WorldTimeRate(1, 1000)).ConsumeElapsedMilliseconds(-10));
            Assert.Throws<ArgumentException>(() => new WorldTimePacer(default(WorldTimeRate)));

            var pacer = new WorldTimePacer(new WorldTimeRate(2, 1000));
            Assert.Throws<OverflowException>(() => pacer.ConsumeElapsedMilliseconds(long.MaxValue));
            Assert.That(pacer.ConsumeElapsedMilliseconds(500), Is.EqualTo(1));
        }
    }
}
