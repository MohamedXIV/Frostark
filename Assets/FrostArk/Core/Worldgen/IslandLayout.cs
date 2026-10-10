using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Worldgen
{
    public enum IslandNodeKind
    {
        Landing = 0,
        Extraction = 1,
        Salvage = 2
    }

    public sealed class IslandNode
    {
        public EntityId Id { get; }
        public int X { get; }
        public int Y { get; }
        public IslandNodeKind Kind { get; }

        public IslandNode(EntityId id, int x, int y, IslandNodeKind kind)
        {
            if (!id.IsValid) throw new ArgumentException("Site needs a stable ID.", nameof(id));
            if (!Enum.IsDefined(typeof(IslandNodeKind), kind))
                throw new ArgumentOutOfRangeException(nameof(kind));
            Id = id; X = x; Y = y; Kind = kind;
        }
    }

    public sealed class IslandEdge
    {
        public EntityId A { get; }
        public EntityId B { get; }
        public bool IsSafe { get; }

        public IslandEdge(EntityId a, EntityId b, bool isSafe)
        {
            if (!a.IsValid || !b.IsValid || a == b)
                throw new ArgumentException("Each path connects two distinct sites.");
            if (StringComparer.Ordinal.Compare(a.Value, b.Value) <= 0)
            {
                A = a; B = b;
            }
            else
            {
                A = b; B = a;
            }
            IsSafe = isSafe;
        }
    }

    /// <summary>
    /// Validated immutable topology. The landing-to-extraction safe path is a
    /// generation invariant; optional side sites may require hazardous travel.
    /// Pure graph data, not proof of Unity navigation mesh or collisions.
    /// </summary>
    public sealed class IslandLayout
    {
        private readonly IslandNode[] _nodes;
        private readonly IslandEdge[] _edges;
        public EntityId IslandId { get; }
        public WorldZoneProfile Zone { get; }
        public EntityId LandingId { get; }
        public EntityId ExtractionId { get; }
        public IReadOnlyList<IslandNode> Nodes => Array.AsReadOnly(_nodes);
        public IReadOnlyList<IslandEdge> Edges => Array.AsReadOnly(_edges);

        private IslandLayout(EntityId islandId, WorldZoneProfile zone,
            IslandNode[] nodes, IslandEdge[] edges)
        {
            IslandId = islandId;
            Zone = zone;
            _nodes = nodes;
            _edges = edges;
            LandingId = nodes.Single(n => n.Kind == IslandNodeKind.Landing).Id;
            ExtractionId = nodes.Single(n => n.Kind == IslandNodeKind.Extraction).Id;
        }

        public static IslandLayout Create(EntityId islandId, WorldZoneProfile zone,
            IEnumerable<IslandNode> nodes, IEnumerable<IslandEdge> edges)
        {
            if (!islandId.IsValid)
                throw new ArgumentException("Island ID is required.", nameof(islandId));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            var points = nodes.ToArray();
            if (points.Length < 2 || points.Length > 64 || points.Any(n => n == null))
                throw new ArgumentException("Island requires 2..64 non-null nodes.", nameof(nodes));
            points = points.OrderBy(n => n.Id.Value, StringComparer.Ordinal).ToArray();
            if (points.Select(n => n.Id.Value).Distinct(StringComparer.Ordinal).Count() != points.Length ||
                points.Select(n => (n.X, n.Y)).Distinct().Count() != points.Length ||
                points.Count(n => n.Kind == IslandNodeKind.Landing) != 1 ||
                points.Count(n => n.Kind == IslandNodeKind.Extraction) != 1)
                throw new ArgumentException("Island sites must have unique IDs/coordinates and exactly one landing/extraction.", nameof(nodes));

            var links = edges.ToArray();
            if (links.Any(e => e == null))
                throw new ArgumentException("Null island edge.", nameof(edges));
            links = links.OrderBy(e => e.A.Value, StringComparer.Ordinal)
                .ThenBy(e => e.B.Value, StringComparer.Ordinal).ToArray();
            var ids = new HashSet<string>(points.Select(p => p.Id.Value), StringComparer.Ordinal);
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var link in links)
            {
                if (!ids.Contains(link.A.Value) || !ids.Contains(link.B.Value) ||
                    !pairs.Add(link.A.Value + "\0" + link.B.Value))
                    throw new ArgumentException("Dangling or duplicate island edge.", nameof(edges));
            }
            var layout = new IslandLayout(islandId, zone, points, links);
            if (!layout.CanReachExtraction(safeOnly: true) || !layout.AreAllSitesReachable())
                throw new ArgumentException("Landing, extraction and optional sites must be reachable.", nameof(edges));
            return layout;
        }

        public IslandNode Get(EntityId id) =>
            id.IsValid ? Array.Find(_nodes, n => n.Id == id) : null;

        private HashSet<string> Reachable(EntityId source, bool safeOnly)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal) { source.Value };
            var pending = new Queue<string>();
            pending.Enqueue(source.Value);
            while (pending.Count > 0)
            {
                string here = pending.Dequeue();
                foreach (var link in _edges)
                {
                    if (safeOnly && !link.IsSafe) continue;
                    string next;
                    if (link.A.Value == here) next = link.B.Value;
                    else if (link.B.Value == here) next = link.A.Value;
                    else continue;
                    if (visited.Add(next)) pending.Enqueue(next);
                }
            }
            return visited;
        }

        public bool CanReachExtraction(bool safeOnly) =>
            Reachable(LandingId, safeOnly).Contains(ExtractionId.Value);

        public bool AreAllSitesReachable() =>
            Reachable(LandingId, safeOnly: false).Count == _nodes.Length;

        public uint Fingerprint()
        {
            var text = new StringBuilder();
            void Add(string value) => text.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(value);
            string N(long value) => value.ToString(CultureInfo.InvariantCulture);
            Add(IslandId.Value); Add(Zone.Id.Value); Add(N((int)Zone.Band));
            Add(N(Zone.AmbientMilliC)); Add(N(Zone.ContaminationPermille));
            Add(N(Zone.PackIcePermille));
            foreach (var n in _nodes)
            {
                Add("site"); Add(n.Id.Value); Add(N(n.X)); Add(N(n.Y)); Add(N((int)n.Kind));
            }
            foreach (var e in _edges)
            {
                Add("edge"); Add(e.A.Value); Add(e.B.Value); Add(e.IsSafe ? "1" : "0");
            }
            return StableHash32.Of(text.ToString());
        }
    }
}
