using FrostArk.Core.Combat;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ColdCombatModifiersTests
    {
        [Test]
        public void ZeroColdAppliesNoPenalty()
        {
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(10, 0), Is.EqualTo(10));
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(10, -50), Is.EqualTo(10));
            Assert.That(ColdCombatModifiers.ApplyReloadModifier(20, 0), Is.EqualTo(20));
            Assert.That(ColdCombatModifiers.CalculateSpeedMultiplier(0), Is.EqualTo(1.0f));
        }

        [Test]
        public void SevereColdIncreasesCooldownAndReloadDurations()
        {
            // 500 permille (50% cold severity): +50% duration
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(10, 500), Is.EqualTo(15));
            Assert.That(ColdCombatModifiers.ApplyReloadModifier(20, 500), Is.EqualTo(30));
        }

        [Test]
        public void ExtremeColdDoublesActionDurationsAndReducesMovementSpeed()
        {
            // 1000 permille (extreme cold): +100% duration (double)
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(10, 1000), Is.EqualTo(20));
            Assert.That(ColdCombatModifiers.ApplyReloadModifier(20, 1000), Is.EqualTo(40));

            // Max speed reduction: 1.0 - 0.35 = 0.65 (35% slowdown)
            Assert.That(ColdCombatModifiers.CalculateSpeedMultiplier(1000), Is.EqualTo(0.65f).Within(0.001f));
        }

        [Test]
        public void NegativeOrZeroBaseTicksReturnsZero()
        {
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(0, 500), Is.EqualTo(0));
            Assert.That(ColdCombatModifiers.ApplyCooldownModifier(-5, 500), Is.EqualTo(0));
            Assert.That(ColdCombatModifiers.ApplyReloadModifier(0, 500), Is.EqualTo(0));
        }
    }
}
