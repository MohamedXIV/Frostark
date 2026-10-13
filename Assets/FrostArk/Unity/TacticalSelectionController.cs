using System;
using System.Collections.Generic;
using FrostArk.Core.World;

namespace FrostArk.Unity
{
    /// <summary>
    /// Pure adapter maintaining the current active TacticalSelection snapshot.
    /// Does not own game simulation state, clock, or character health.
    /// </summary>
    public sealed class TacticalSelectionController
    {
        public TacticalSelection Current { get; private set; } = TacticalSelection.Empty;

        public event Action<TacticalSelection> SelectionChanged;

        public IReadOnlyList<EntityId> SelectedActorIds => Current.ActorIds;
        public int Count => Current.Count;

        public bool IsSelected(EntityId actorId) => Current.Contains(actorId);

        public void Select(EntityId actorId)
        {
            Current = Current.Replace(new[] { actorId });
            SelectionChanged?.Invoke(Current);
        }

        public void SelectMultiple(IEnumerable<EntityId> actorIds)
        {
            Current = Current.Replace(actorIds);
            SelectionChanged?.Invoke(Current);
        }

        public void Toggle(EntityId actorId)
        {
            Current = Current.Contains(actorId)
                ? Current.Exclude(actorId)
                : Current.Include(actorId);
            SelectionChanged?.Invoke(Current);
        }

        public void Exclude(EntityId actorId)
        {
            if (Current.Contains(actorId))
            {
                Current = Current.Exclude(actorId);
                SelectionChanged?.Invoke(Current);
            }
        }

        public void Clear()
        {
            if (Current.Count > 0)
            {
                Current = Current.Clear();
                SelectionChanged?.Invoke(Current);
            }
        }
    }
}
