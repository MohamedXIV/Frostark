using System;
using System.Collections.Generic;
using System.Globalization;
using FrostArk.Core.World;

namespace FrostArk.Core.Worldgen
{
    /// <summary>
    /// Version-1 graph generator. Uses labeled FNV derivation, no global random
    /// state. A guaranteed safe spine permits extraction; optional attached
    /// salvage sites represent branching exploration and unsafe approaches.
    /// </summary>
    public static class IslandGenerator
    {
        public const int GeneratorVersion = 1;
        public const int MaxNodes = 64;

        public static IslandLayout Generate(EntityId islandId, int seed,
            WorldZoneProfile zone, int nodeCount)
        {
            if (!islandId.IsValid)
                throw new ArgumentException("Island requires persistent ID.", nameof(islandId));
            if (zone == null) throw new ArgumentNullException(nameof(zone));
            if (nodeCount < 4 || nodeCount > MaxNodes)
                throw new ArgumentOutOfRangeException(nameof(nodeCount));

            uint Hash(int index, string label)
            {
                string token = islandId.Value + "|" +
                    seed.ToString(CultureInfo.InvariantCulture) + "|v" +
                    GeneratorVersion.ToString(CultureInfo.InvariantCulture) + "|" +
                    index.ToString(CultureInfo.InvariantCulture) + "|" + label;
                return StableHash32.Of(token);
            }

            int spineCount = Math.Max(3, nodeCount / 2 + 1);
            var points = new List<IslandNode>(nodeCount);
            var links = new List<IslandEdge>(nodeCount - 1);
            EntityId Site(int index) => EntityId.Parse(islandId.Value +
                "/site:" + index.ToString("D3", CultureInfo.InvariantCulture));

            for (int i = 0; i < nodeCount; ++i)
            {
                bool onSpine = i < spineCount;
                int x = checked(i * 11 + (int)(Hash(i, "x") % 7));
                int y = onSpine
                    ? (int)(Hash(i, "spine-y") % 21)
                    : 50 + (int)(Hash(i, "side-y") % 31);
                var kind = i == 0 ? IslandNodeKind.Landing :
                    i == spineCount - 1 ? IslandNodeKind.Extraction :
                    IslandNodeKind.Salvage;
                points.Add(new IslandNode(Site(i), x, y, kind));

                if (i > 0 && i < spineCount)
                    links.Add(new IslandEdge(Site(i - 1), Site(i), isSafe: true));
                if (i >= spineCount)
                {
                    int attachTo = 1 + (int)(Hash(i, "branch") % (uint)(spineCount - 2));
                    links.Add(new IslandEdge(Site(attachTo), Site(i), isSafe: false));
                }
            }
            return IslandLayout.Create(islandId, zone, points, links);
        }
    }
}
