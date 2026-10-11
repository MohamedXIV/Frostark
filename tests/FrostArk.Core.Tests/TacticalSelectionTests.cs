using System;
using System.Collections.Generic;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class TacticalSelectionTests
    {
        private static readonly EntityId Hero = EntityId.Parse("actor:hero");
        private static readonly EntityId Medic = EntityId.Parse("actor:medic");
        private static readonly EntityId Scout = EntityId.Parse("actor:scout");
        private static readonly EntityId Crate = EntityId.Parse("item:crate");

        [Test]
        public void EmptySelectionIsSafeButCannotProduceAnOrder()
        {
            var selected = TacticalSelection.Empty;
            Assert.That(selected.Count, Is.Zero);
            Assert.That(selected.ActorIds, Is.Empty);
            Assert.That(selected.Contains(Hero), Is.False);
            Assert.Throws<ArgumentException>(() =>
                TacticalOrderIntent.MoveTo(selected.ActorIds, 1, 2));
        }

        [Test]
        public void ReplaceCanonicalizesDuplicatesAndCopiesCallerCollection()
        {
            var actors = new[] { Medic, Hero, Medic };
            var selected = TacticalSelection.Empty.Replace(actors);
            actors[0] = Scout;

            Assert.That(selected.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
            Assert.That(selected.Count, Is.EqualTo(2));
            Assert.That(selected.Contains(Hero), Is.True);
            Assert.That(selected.Contains(Scout), Is.False);

            var exposed = selected.ActorIds as IList<EntityId>;
            Assert.That(exposed, Is.Not.Null);
            Assert.That(exposed.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => exposed[0] = Scout);
        }

        [Test]
        public void IncludeExcludePreserveOriginalSnapshotAndAreIdempotent()
        {
            var first = TacticalSelection.Empty.Include(Hero);
            var duo = first.Include(Medic);
            var unchanged = duo.Include(Hero);
            var solo = duo.Exclude(Medic);

            Assert.That(first.ActorIds, Is.EqualTo(new[] { Hero }));
            Assert.That(duo.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
            Assert.That(unchanged, Is.SameAs(duo));
            Assert.That(solo.ActorIds, Is.EqualTo(new[] { Hero }));
            Assert.That(duo.Exclude(Scout), Is.SameAs(duo));
            Assert.That(duo.Count, Is.EqualTo(2));
        }

        [Test]
        public void ClearDoesNotMutateEarlierSelection()
        {
            var selected = TacticalSelection.Empty.Include(Hero).Include(Medic);
            var cleared = selected.Clear();

            Assert.That(cleared, Is.SameAs(TacticalSelection.Empty));
            Assert.That(selected.Count, Is.EqualTo(2));
            Assert.That(selected.Exclude(Hero).Exclude(Medic).Count, Is.Zero);
        }

        [Test]
        public void InvalidActorIdsAndNullReplacementAreRejected()
        {
            Assert.Throws<ArgumentNullException>(() =>
                TacticalSelection.Empty.Replace(null));
            Assert.Throws<ArgumentException>(() =>
                TacticalSelection.Empty.Replace(new[] { Hero, default(EntityId) }));
            Assert.Throws<ArgumentException>(() =>
                TacticalSelection.Empty.Include(default(EntityId)));
            Assert.Throws<ArgumentException>(() =>
                TacticalSelection.Empty.Exclude(default(EntityId)));
        }

        [Test]
        public void CanonicalSelectionFeedsExistingClickToMoveAndInteractOrders()
        {
            var selected = TacticalSelection.Empty.Include(Medic).Include(Hero);
            var move = TacticalOrderIntent.MoveTo(selected.ActorIds, 12, -7);
            var interact = TacticalOrderIntent.InteractWith(selected.ActorIds, Crate);

            Assert.That(move.ActorIds, Is.EqualTo(new[] { Hero, Medic }));
            Assert.That(move.DestinationX, Is.EqualTo(12));
            Assert.That(interact.ActorIds, Is.EqualTo(move.ActorIds));
            Assert.That(interact.TargetId, Is.EqualTo(Crate));
            Assert.That(selected.Count, Is.EqualTo(2), "Order requests must not change the current UI selection.");
        }
    }
}
