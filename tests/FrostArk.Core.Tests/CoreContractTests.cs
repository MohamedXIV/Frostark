using System;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CoreContractTests
    {
        [Test]
        public void DefaultWorldMinuteIsZero()
        {
            Assert.That(default(WorldMinute), Is.EqualTo(WorldMinute.Zero));
        }

        [TestCase(-1)]
        [TestCase(long.MinValue)]
        public void WorldMinuteRejectsNegativeValues(long value)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WorldMinute(value));
        }

        [Test]
        public void WorldMinuteArithmeticIsExplicitAndBounded()
        {
            var start = new WorldMinute(120);
            var end = start.Add(60);
            Assert.That(end.Value, Is.EqualTo(180));
            Assert.That(end.ElapsedSince(start), Is.EqualTo(60));
            Assert.That(end.Subtract(60), Is.EqualTo(start));
            Assert.Throws<ArgumentOutOfRangeException>(() => start.Subtract(121));
            Assert.Throws<ArgumentOutOfRangeException>(() => start.Add(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => start.Subtract(-1));
            Assert.Throws<OverflowException>(() => new WorldMinute(long.MaxValue).Add(1));
        }

        [Test]
        public void WorldMinuteOrderingAndIdentityDoNotDependOnFrames()
        {
            var first = new WorldMinute(2);
            var second = first.Add(1);
            Assert.That(first < second, Is.True);
            Assert.That(second > first, Is.True);
            Assert.That(first, Is.EqualTo(new WorldMinute(2)));
            Assert.That(first.ToString(), Is.EqualTo("2"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(" soldier:1")]
        [TestCase("soldier:1 ")]
        public void EntityIdRejectsMissingOrUnstableWhitespace(string? id)
        {
            Assert.That(EntityId.TryParse(id!, out var parsed), Is.False);
            Assert.That(parsed.IsValid, Is.False);
            Assert.Throws<ArgumentException>(() => EntityId.Parse(id!));
        }

        [Test]
        public void EntityIdUsesOrdinalCaseSensitiveIdentity()
        {
            var a = EntityId.Parse("crew:001");
            var b = EntityId.Parse("crew:001");
            var c = EntityId.Parse("Crew:001");
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
            Assert.That(a, Is.Not.EqualTo(c));
            Assert.That(a.IsValid, Is.True);
            Assert.That(default(EntityId).IsValid, Is.False);
        }

        [Test]
        public void StableHashMatchesVersionedGoldenVectors()
        {
            Assert.That(StableHash32.Of(""), Is.EqualTo(0x811C9DC5u));
            Assert.That(StableHash32.Of("FrostArk"), Is.EqualTo(0xCFB44B4Bu));
            Assert.That(StableHash32.Of("FrostArk", 12345u), Is.EqualTo(0x3099AABAu));
            Assert.That(StableHash32.Of("🧊"), Is.EqualTo(0x38171F0Du));
        }

        [Test]
        public void StableHashDoesNotUseRuntimeHashCodeOrGlobalRandomState()
        {
            var seed = StableHash32.Of("FrostArk/clock");
            for (int i = 0; i < 100; ++i)
                Assert.That(StableHash32.Of("FrostArk/clock"), Is.EqualTo(seed));
            Assert.That(StableHash32.Of("FrostArk"), Is.Not.EqualTo(StableHash32.Of("frostark")));
            Assert.Throws<ArgumentNullException>(() => StableHash32.Of(null!));
        }
    }
}
