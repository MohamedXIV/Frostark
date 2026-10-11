using System;
using System.Linq;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class TacticalOrderIntentTests
    {
        private static readonly EntityId Hero = EntityId.Parse("actor:hero");
        private static readonly EntityId Medic = EntityId.Parse("actor:medic");
        private static readonly EntityId Enemy = EntityId.Parse("enemy:raider");
        private static readonly EntityId Salvage = EntityId.Parse("site:salvage");

        [Test]
        public void SoloProtagonistMoveOrderUsesWorldPointNotInputAxes()
        {
            var move = TacticalOrderIntent.MoveTo(new[] { Hero }, worldX: -12.5, worldZ: 7.25);

            Assert.That(move.Kind, Is.EqualTo(TacticalOrderKind.Move));
            Assert.That(move.ActorIds, Is.EqualTo(new[] { Hero }));
            Assert.That(move.HasDestination, Is.True);
            Assert.That(move.DestinationX, Is.EqualTo(-12.5));
            Assert.That(move.DestinationZ, Is.EqualTo(7.25));
            Assert.That(move.HasEntityTarget, Is.False);
            Assert.That(move.TargetId.IsValid, Is.False);
        }

        [Test]
        public void GroupSelectionIsSortedAndCannotBeChangedAfterConstruction()
        {
            var selected = new[] { Medic, Hero };
            var request = TacticalOrderIntent.MoveTo(selected, 0, 0);
            selected[0] = Enemy;

            Assert.That(request.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
            Assert.That(request.ActorIds, Has.Count.EqualTo(2));
            var exposed = request.ActorIds as System.Collections.Generic.IList<EntityId>;
            Assert.That(exposed, Is.Not.Null);
            Assert.That(exposed.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposed[0] = Enemy);
            Assert.That(request.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
        }

        [Test]
        public void ContextActionAndAttackReferenceStableEntityTargets()
        {
            var interact = TacticalOrderIntent.InteractWith(new[] { Hero }, Salvage);
            var attack = TacticalOrderIntent.AttackTarget(new[] { Hero, Medic }, Enemy);

            Assert.That(interact.Kind, Is.EqualTo(TacticalOrderKind.Interact));
            Assert.That(interact.TargetId, Is.EqualTo(Salvage));
            Assert.That(interact.HasEntityTarget, Is.True);
            Assert.That(interact.HasDestination, Is.False);
            Assert.That(attack.Kind, Is.EqualTo(TacticalOrderKind.Attack));
            Assert.That(attack.TargetId, Is.EqualTo(Enemy));
            Assert.That(attack.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
            Assert.That(attack.HasDestination, Is.False);
        }

        [Test]
        public void RejectsNullEmptyDuplicatedOrInvalidActorSelections()
        {
            Assert.Throws<ArgumentNullException>(() =>
                TacticalOrderIntent.MoveTo(null, 1, 1));
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.MoveTo(Array.Empty<EntityId>(), 1, 1));
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.MoveTo(new[] { Hero, Hero }, 1, 1));
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.MoveTo(new[] { Hero, default(EntityId) }, 1, 1));
        }

        [Test]
        public void RejectsUnknownTargetAndNonfiniteResolvedWorldPoints()
        {
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.InteractWith(new[] { Hero }, default(EntityId)));
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.AttackTarget(new[] { Hero }, default(EntityId)));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TacticalOrderIntent.MoveTo(new[] { Hero }, double.NaN, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TacticalOrderIntent.MoveTo(new[] { Hero }, 1, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                TacticalOrderIntent.MoveTo(new[] { Hero }, double.NegativeInfinity, 0));
        }

        [Test]
        public void SameLogicalActorsProduceEqualCanonicalSelectionRegardlessOfClickOrder()
        {
            var first = TacticalOrderIntent.AttackTarget(new[] { Medic, Hero }, Enemy);
            var second = TacticalOrderIntent.AttackTarget(new[] { Hero, Medic }, Enemy);

            Assert.That(first.ActorIds.SequenceEqual(second.ActorIds), Is.True);
            Assert.That(first.Kind, Is.EqualTo(second.Kind));
            Assert.That(first.TargetId, Is.EqualTo(second.TargetId));
        }
    }
}
