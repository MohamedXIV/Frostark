using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.World;

namespace FrostArk.Core.Worldgen
{
    /// <summary>
    /// Per-site immutable persistent progress. No items or damage physics are
    /// created by marking a site; the future Unity session validates physical
    /// interaction before issuing a command.
    /// </summary>
    public sealed class IslandSiteRecord
    {
        public EntityId IslandId { get; }
        public EntityId Id { get; }
        public bool IsLooted { get; }
        public int DamagePermille { get; }

        public IslandSiteRecord(EntityId islandId, EntityId id, bool isLooted,
            int damagePermille)
        {
            if (!islandId.IsValid || !id.IsValid)
                throw new ArgumentException("Site and island IDs are required.");
            if (damagePermille < 0 || damagePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(damagePermille));
            IslandId = islandId;
            Id = id;
            IsLooted = isLooted;
            DamagePermille = damagePermille;
        }

        internal IslandSiteRecord WithLooted() =>
            new IslandSiteRecord(IslandId, Id, true, DamagePermille);

        internal IslandSiteRecord WithDamage(int added) =>
            new IslandSiteRecord(IslandId, Id, IsLooted,
                Math.Min(1000, checked(DamagePermille + added)));
    }

    /// <summary>
    /// Immutable canonical registry across islands: all known sites and
    /// their consumed and damaged flags, keyed by stable globally unique IDs.
    /// No Unity instance identifiers or mutable scene flags.
    /// </summary>
    public sealed class IslandSiteProgress
    {
        private readonly IslandSiteRecord[] _sites;
        public IReadOnlyList<IslandSiteRecord> Sites => Array.AsReadOnly(_sites);

        private IslandSiteProgress(IslandSiteRecord[] sites) => _sites = sites;

        public static IslandSiteProgress Empty => Restore(Array.Empty<IslandSiteRecord>());

        public static IslandSiteProgress Create(IEnumerable<IslandLayout> layouts)
        {
            if (layouts == null) throw new ArgumentNullException(nameof(layouts));
            var islands = new HashSet<string>(StringComparer.Ordinal);
            var sites = new List<IslandSiteRecord>();
            foreach (var layout in layouts)
            {
                if (layout == null || !islands.Add(layout.IslandId.Value))
                    throw new ArgumentException("Invalid or duplicate authored island.", nameof(layouts));
                foreach (var node in layout.Nodes)
                    sites.Add(new IslandSiteRecord(layout.IslandId, node.Id, false, 0));
            }
            return Restore(sites);
        }

        internal static IslandSiteProgress Restore(IEnumerable<IslandSiteRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var rows = records.ToArray();
            if (rows.Any(x => x == null))
                throw new ArgumentException("Null site record.", nameof(records));
            rows = rows.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < rows.Length; i++)
                if (rows[i].Id == rows[i - 1].Id)
                    throw new ArgumentException("Duplicate persistent site ID.", nameof(records));
            return new IslandSiteProgress(rows);
        }

        public IslandSiteRecord Get(EntityId siteId) =>
            siteId.IsValid ? Array.Find(_sites, x => x.Id == siteId) : null;

        public IslandSiteProgress MarkLooted(EntityId id)
        {
            var record = Get(id) ??
                throw new ArgumentException("Unknown site.", nameof(id));
            if (record.IsLooted) throw new InvalidOperationException("Site already looted.");
            return Replace(id, record.WithLooted());
        }

        public IslandSiteProgress ApplyDamage(EntityId id, int amount)
        {
            if (amount <= 0 || amount > 1000)
                throw new ArgumentOutOfRangeException(nameof(amount));
            var record = Get(id) ??
                throw new ArgumentException("Unknown site.", nameof(id));
            if (record.DamagePermille == 1000)
                throw new InvalidOperationException("Site already fully damaged.");
            return Replace(id, record.WithDamage(amount));
        }

        private IslandSiteProgress Replace(EntityId id, IslandSiteRecord updated)
        {
            var copied = (IslandSiteRecord[])_sites.Clone();
            for (int i = 0; i < copied.Length; i++)
                if (copied[i].Id == id)
                {
                    copied[i] = updated;
                    break;
                }
            return new IslandSiteProgress(copied);
        }

        public uint Fingerprint()
        {
            var buffer = new StringBuilder();
            void Add(string v) => buffer.Append(v.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(v);
            foreach (var row in _sites)
            {
                Add(row.IslandId.Value);
                Add(row.Id.Value);
                Add(row.IsLooted ? "1" : "0");
                Add(row.DamagePermille.ToString(CultureInfo.InvariantCulture));
            }
            return StableHash32.Of(buffer.ToString());
        }
    }
}
