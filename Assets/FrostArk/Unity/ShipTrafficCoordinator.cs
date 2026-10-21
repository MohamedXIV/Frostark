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

        public bool CanEnter(ShipGridWalker caller, ShipGridCell from, ShipGridCell next)
        {
            // Own the crossing edge and prevent diagonal squeezing between
            // occupied cardinal side cells, just as static A* forbids it.
            bool diagonal = from.RoomId == next.RoomId &&
                from.X != next.X && from.Y != next.Y;
            var flankX = diagonal ? new ShipGridCell(from.RoomId, next.X, from.Y) : default;
            var flankY = diagonal ? new ShipGridCell(from.RoomId, from.X, next.Y) : default;
            foreach (var other in _walkers)
            {
                if (other == null || other == caller || !other.IsConfigured) continue;
                if (other.HoldsCell(next)) return false;
                if (diagonal &&
                    (other.HoldsCell(flankX) || other.HoldsCell(flankY))) return false;
            }
            return true;
        }
    }
}
