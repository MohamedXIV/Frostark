using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.World;
using FrostArk.Core.Worldgen;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        /// <summary>
        /// V12 append after medical catalog: persisted known site registry and
        /// monotonic consumed/damaged flags. No physical scene state fabricated.
        /// </summary>
        private static void WriteSiteProgress(BinaryWriter writer, IslandSiteProgress state)
        {
            WriteCount(writer, state.Sites.Count);
            foreach (var site in state.Sites)
            {
                WriteId(writer, site.IslandId);
                WriteId(writer, site.Id);
                writer.Write(site.IsLooted ? (byte)1 : (byte)0);
                writer.Write(site.DamagePermille);
            }
        }

        private static IslandSiteProgress ReadSiteProgress(BinaryReader reader)
        {
            var sites = new List<IslandSiteRecord>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var islandId = ReadId(reader);
                var siteId = ReadId(reader);
                var looted = reader.ReadByte();
                if (looted > 1)
                    throw new InvalidDataException("Invalid looted flag.");
                var damage = reader.ReadInt32();
                sites.Add(new IslandSiteRecord(islandId, siteId, looted == 1, damage));
            }
            return IslandSiteProgress.Restore(sites);
        }
    }
}
