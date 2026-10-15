using FrostArk.Core.Combat;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class CombatMitigationTests
    {
        [Test]
        public void ZeroArmorAppliesFullDamage()
        {
            Assert.That(CombatMitigation.CalculateMitigatedWounds(200, 0), Is.EqualTo(200));
            Assert.That(CombatMitigation.CalculateMitigatedWounds(500, -10), Is.EqualTo(500));
        }

        [Test]
        public void PartialArmorMitigatesProportionally()
        {
            // 25% armor reduction
            Assert.That(CombatMitigation.CalculateMitigatedWounds(200, 250), Is.EqualTo(150));
            // 50% armor reduction
            Assert.That(CombatMitigation.CalculateMitigatedWounds(300, 500), Is.EqualTo(150));
            // 70% armor reduction: 100 * 0.3 = 30
            Assert.That(CombatMitigation.CalculateMitigatedWounds(100, 700), Is.EqualTo(30));
        }

        [Test]
        public void HeavyArmorGuaranteesAtLeastOneWoundUnlessFullyDeflected()
        {
            // 99% armor: 50 * 0.01 = 0.5 -> clamps to 1
            Assert.That(CombatMitigation.CalculateMitigatedWounds(50, 990), Is.EqualTo(1));
        }

        [Test]
        public void FullArmorCompletelyDeflectsDamage()
        {
            Assert.That(CombatMitigation.CalculateMitigatedWounds(200, 1000), Is.EqualTo(0));
            Assert.That(CombatMitigation.CalculateMitigatedWounds(500, 1200), Is.EqualTo(0));
        }

        [Test]
        public void ZeroOrNegativeRawDamageReturnsZero()
        {
            Assert.That(CombatMitigation.CalculateMitigatedWounds(0, 500), Is.EqualTo(0));
            Assert.That(CombatMitigation.CalculateMitigatedWounds(-100, 500), Is.EqualTo(0));
        }
    }
}
