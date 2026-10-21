using System.Collections.Generic;
using FrostArk.Core.Ship;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Ship-root-local, TRANSIENT presentation occupancy. The layout owns
    /// static obstacles; this component owns only currently occupied crew
    /// cells and cells/edges reserved for the next physical step.
    /// No independent clock, world authority or persisted work-slot claims.
    /// </summary>
    public sealed class ShipTrafficCoordinator : MonoBehaviour
    {
        private readonly List<ShipGridWalker> _walkers = new List<ShipGridWalker>();
        public int Count => _walkers.Count;

        public void Register(ShipGridWalker walker)
        {
            if (walker != null && !_walkers.Contains(walker))
                _walkers.Add(walker);
        }

        public void Unregister(ShipGridWalker walker) => _walkers.Remove(walker);

        public bool CanPark(ShipGridWalker caller, ShipGridCell goal)
        {
            foreach (var other in _walkers)
            {
                if (other == null || other == caller || !other.IsConfigured) continue;
                if (other.HoldsCell(goal)) return false;
                if (other.IsMoving && other.OrderedCell == goal) return false;
            }
            return true;
        }

        public bool CanEnter(ShipGridWalker caller, ShipGridCell next)
        {
            // Until a peer has physically vacated its current cell, a
            // conflicting next-step reservation cannot be acquired. This
            // also prevents opposing actors swapping a narrow edge in one
            // frame or entering the same bulkhead door simultaneously.
            foreach (var other in _walkers)
                if (other != null && other != caller && other.IsConfigured &&
                    other.HoldsCell(next))
                    return false;
            return true;
        }
    }
}
