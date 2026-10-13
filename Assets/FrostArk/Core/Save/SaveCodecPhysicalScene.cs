using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        /// <summary>
        /// V13 trailing scene-reentry checkpoint. Flag=0 is exactly one byte,
        /// so v12 blobs decode without inventing a physical location.
        /// Coordinates/Unity references are not serialized.
        /// </summary>
        private static void WriteSceneCheckpoint(BinaryWriter writer, PhysicalSceneCheckpoint scene)
        {
            if (scene == null)
            {
                writer.Write((byte)0);
                return;
            }

            writer.Write((byte)1);
            writer.Write((byte)scene.Theater);
            WriteId(writer, scene.SceneId);
            if (scene.Theater == PhysicalTheater.Shore)
            {
                WriteId(writer, scene.SiteId);
                WriteId(writer, scene.SpawnAnchorId);
                WriteCount(writer, scene.PartyIds.Count);
                foreach (var id in scene.PartyIds) WriteId(writer, id);
            }
            else if (scene.Theater == PhysicalTheater.Ship)
            {
                WriteId(writer, scene.SpawnAnchorId);
            }
            else
                throw new ArgumentException("Unsupported physical theater.", nameof(scene));
        }

        private static PhysicalSceneCheckpoint ReadSceneCheckpoint(BinaryReader reader)
        {
            var present = reader.ReadByte();
            if (present == 0) return null;
            if (present != 1)
                throw new InvalidDataException("Invalid scene checkpoint presence flag.");

            var theater = reader.ReadByte();
            var scene = ReadId(reader);
            if (theater == (byte)PhysicalTheater.Ship)
                return PhysicalSceneCheckpoint.Ship(scene, ReadId(reader));
            if (theater == (byte)PhysicalTheater.Shore)
            {
                var site = ReadId(reader);
                var anchor = ReadId(reader);
                var party = new List<EntityId>();
                for (int i = 0, count = ReadCount(reader); i < count; i++)
                    party.Add(ReadId(reader));
                return PhysicalSceneCheckpoint.Shore(scene, site, anchor, party);
            }

            throw new InvalidDataException("Unrecognized physical theater kind.");
        }
    }
}
