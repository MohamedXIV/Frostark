using System;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class PlayerInputIntentTests
    {
        [Test]
        public void KeyboardDiagonalIsBoundedWithoutMakingStraightMovementStronger()
        {
            var diagonal = PlayerInputIntent.FromAxes(1f, 1f, 0f, 0f,
                fireHeld: false, interactPressed: false);
            var straight = PlayerInputIntent.FromAxes(0f, 1f, 0f, 0f,
                fireHeld: false, interactPressed: false);

            Assert.That(diagonal.MoveX, Is.EqualTo(Math.Sqrt(0.5)).Within(0.000001));
            Assert.That(diagonal.MoveY, Is.EqualTo(Math.Sqrt(0.5)).Within(0.000001));
            Assert.That(Math.Sqrt(diagonal.MoveX * diagonal.MoveX +
                                  diagonal.MoveY * diagonal.MoveY), Is.EqualTo(1).Within(0.000001));
            Assert.That(straight.MoveX, Is.Zero);
            Assert.That(straight.MoveY, Is.EqualTo(1f));
        }

        [Test]
        public void AnalogStrengthIsPreservedWhileOversizedDirectionsAreClamped()
        {
            var analog = PlayerInputIntent.FromAxes(0.3f, 0.4f, 0.6f, 0.8f,
                fireHeld: false, interactPressed: false);
            var clamped = PlayerInputIntent.FromAxes(4f, -3f, 3f, 4f,
                fireHeld: true, interactPressed: true);

            Assert.That(analog.MoveX, Is.EqualTo(0.3f).Within(0.000001));
            Assert.That(analog.MoveY, Is.EqualTo(0.4f).Within(0.000001));
            Assert.That(analog.AimX, Is.EqualTo(0.6f).Within(0.000001));
            Assert.That(analog.AimY, Is.EqualTo(0.8f).Within(0.000001));
            Assert.That(clamped.MoveX, Is.EqualTo(0.8f).Within(0.000001));
            Assert.That(clamped.MoveY, Is.EqualTo(-0.6f).Within(0.000001));
            Assert.That(clamped.AimX, Is.EqualTo(0.6f).Within(0.000001));
            Assert.That(clamped.AimY, Is.EqualTo(0.8f).Within(0.000001));
            Assert.That(clamped.FireHeld, Is.True);
            Assert.That(clamped.InteractPressed, Is.True);
        }

        [Test]
        public void NoAimDirectionRemainsUnknownWithoutInventingFacingOrActions()
        {
            var neutral = PlayerInputIntent.FromAxes(0f, 0f, 0f, 0f,
                fireHeld: false, interactPressed: false);
            var firingWithoutAim = PlayerInputIntent.FromAxes(1f, 0f, 0f, 0f,
                fireHeld: true, interactPressed: false);

            Assert.That(neutral.HasAimDirection, Is.False);
            Assert.That(neutral.FireHeld, Is.False);
            Assert.That(neutral.InteractPressed, Is.False);
            Assert.That(firingWithoutAim.HasAimDirection, Is.False,
                "The Unity presenter must resolve facing; Core must not invent a direction.");
            Assert.That(firingWithoutAim.FireHeld, Is.True);
            Assert.That(firingWithoutAim.InteractPressed, Is.False);
        }

        [Test]
        public void NonFiniteAxesAreRejectedBeforeTheyCanReachPlayerControls()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerInputIntent.FromAxes(
                float.NaN, 0f, 0f, 0f, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerInputIntent.FromAxes(
                0f, float.PositiveInfinity, 0f, 0f, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerInputIntent.FromAxes(
                0f, 0f, float.NegativeInfinity, 0f, false, false));
            Assert.Throws<ArgumentOutOfRangeException>(() => PlayerInputIntent.FromAxes(
                0f, 0f, 0f, float.NaN, false, false));
        }
    }
}
