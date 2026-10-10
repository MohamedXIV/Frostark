using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    /// <summary>Immutable ship route truth. Does not drive Unity movement.</summary>
    public sealed class ShipNavigationState
    {
        private readonly SeaWaypoint[] _waypoints;
        private readonly SeaLeg[] _legs;
        private readonly EntityId[] _route;

        public IReadOnlyList<SeaWaypoint> Waypoints => Array.AsReadOnly(_waypoints);
        public IReadOnlyList<SeaLeg> Legs => Array.AsReadOnly(_legs);
        public IReadOnlyList<EntityId> ActiveLegIds => Array.AsReadOnly(_route);
        public EntityId CurrentWaypoint { get; }
        public NavigationTuning? Tuning { get; }
        public int HullIntegrityPermille { get; }
        public int ActiveLegIndex { get; }
        public int ProgressUnits { get; }
        public bool IsActive => _route.Length > 0 && ActiveLegIndex < _route.Length;

        private ShipNavigationState(SeaWaypoint[] waypoints, SeaLeg[] legs,
            EntityId currentWaypoint, NavigationTuning? tuning, int hullIntegrityPermille,
            EntityId[] route, int activeLegIndex, int progressUnits)
        {
            _waypoints = waypoints; _legs = legs; CurrentWaypoint = currentWaypoint;
            Tuning = tuning; HullIntegrityPermille = hullIntegrityPermille;
            _route = route; ActiveLegIndex = activeLegIndex; ProgressUnits = progressUnits;
        }

        public static ShipNavigationState Empty => new ShipNavigationState(
            Array.Empty<SeaWaypoint>(), Array.Empty<SeaLeg>(), default, null, 0,
            Array.Empty<EntityId>(), 0, 0);

        public static ShipNavigationState Create(IEnumerable<SeaWaypoint> waypoints,
            IEnumerable<SeaLeg> legs, EntityId currentWaypoint, NavigationTuning tuning,
            int hullIntegrityPermille)
        {
            if (waypoints == null || legs == null || tuning == null)
                throw new ArgumentNullException(nameof(waypoints));
            var points = waypoints.ToArray(); var links = legs.ToArray();
            if (points.Any(x => x == null) || links.Any(x => x == null))
                throw new ArgumentException("Null route map element.");
            points = points.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            links = links.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            if (points.Length == 0 || !currentWaypoint.IsValid ||
                !points.Any(x => x.Id == currentWaypoint))
                throw new ArgumentException("Current waypoint must exist in the authored graph.");
            if (hullIntegrityPermille < 1 || hullIntegrityPermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(hullIntegrityPermille));
            if (points.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != points.Length ||
                links.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != links.Length ||
                links.Any(x => !points.Any(p => p.Id == x.FromId) ||
                               !points.Any(p => p.Id == x.ToId)))
                throw new ArgumentException("Duplicate or dangling route map IDs.");
            return new ShipNavigationState(points, links, currentWaypoint, tuning,
                hullIntegrityPermille, Array.Empty<EntityId>(), 0, 0);
        }

        public SeaLeg? GetLeg(EntityId id) => id.IsValid ?
            Array.Find(_legs, l => l.Id == id) : null;

        public ShipNavigationState PlanRoute(IReadOnlyList<EntityId> legIds)
        {
            if (legIds == null || legIds.Count == 0)
                throw new ArgumentException("Nonempty route required.", nameof(legIds));
            if (!CurrentWaypoint.IsValid || ProgressUnits != 0)
                throw new InvalidOperationException("Cannot plan from an unknown or mid-leg location.");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var at = CurrentWaypoint;
            var ordered = new EntityId[legIds.Count];
            for (int i = 0; i < legIds.Count; ++i)
            {
                var id = legIds[i];
                if (!id.IsValid || !seen.Add(id.Value))
                    throw new ArgumentException("Duplicate or invalid leg.", nameof(legIds));
                var leg = GetLeg(id);
                if (leg == null || leg.FromId != at)
                    throw new ArgumentException("Route must be connected and begin here.", nameof(legIds));
                ordered[i] = id; at = leg.ToId;
            }
            return new ShipNavigationState(_waypoints, _legs, CurrentWaypoint, Tuning,
                HullIntegrityPermille, ordered, 0, 0);
        }

        public ShipNavigationState CancelRoute()
        {
            if (!IsActive || ProgressUnits != 0)
                throw new InvalidOperationException("Cannot cancel an idle or mid-leg route.");
            return new ShipNavigationState(_waypoints, _legs, CurrentWaypoint, Tuning,
                HullIntegrityPermille, Array.Empty<EntityId>(), 0, 0);
        }

        public uint Fingerprint()
        {
            var b = new StringBuilder();
            void Add(string x) => b.Append(x.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(x);
            Add("navigation-v1");
            foreach (var p in _waypoints)
            {
                Add("point"); Add(p.Id.Value); Add(p.X.ToString(CultureInfo.InvariantCulture));
                Add(p.Y.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var e in _legs)
            {
                Add("leg"); Add(e.Id.Value); Add(e.FromId.Value); Add(e.ToId.Value);
                Add(e.DistanceUnits.ToString(CultureInfo.InvariantCulture));
                Add(e.IcePermille.ToString(CultureInfo.InvariantCulture));
                Add(e.StormPermille.ToString(CultureInfo.InvariantCulture));
                Add(e.VisibilityPermille.ToString(CultureInfo.InvariantCulture));
            }
            Add(CurrentWaypoint.Value ?? "");
            Add(HullIntegrityPermille.ToString(CultureInfo.InvariantCulture));
            Add(ActiveLegIndex.ToString(CultureInfo.InvariantCulture));
            Add(ProgressUnits.ToString(CultureInfo.InvariantCulture));
            foreach (var r in _route) Add(r.Value);
            if (Tuning != null)
            {
                foreach (var n in new[] {
                    Tuning.SpeedUnitsPerMinute, Tuning.FuelPerUnit, Tuning.IceDragPermille,
                    Tuning.StormDragPermille, Tuning.SafeIcePermille,
                    Tuning.IceDamagePerMinute, Tuning.StormDamagePerMinute })
                    Add(n.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(b.ToString());
        }
    }
}
