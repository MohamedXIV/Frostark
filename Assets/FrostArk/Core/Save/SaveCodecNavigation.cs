using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Ship;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteNavigation(BinaryWriter writer, ShipNavigationState navigation)
        {
            if (!navigation.CurrentWaypoint.IsValid)
            {
                writer.Write((byte)0);
                return;
            }
            writer.Write((byte)1);
            WriteCount(writer, navigation.Waypoints.Count);
            foreach (var point in navigation.Waypoints)
            {
                WriteId(writer, point.Id);
                writer.Write(point.X);
                writer.Write(point.Y);
            }
            WriteCount(writer, navigation.Legs.Count);
            foreach (var leg in navigation.Legs)
            {
                WriteId(writer, leg.Id);
                WriteId(writer, leg.FromId);
                WriteId(writer, leg.ToId);
                writer.Write(leg.DistanceUnits);
                writer.Write(leg.IcePermille);
                writer.Write(leg.StormPermille);
                writer.Write(leg.VisibilityPermille);
            }
            var tuning = navigation.Tuning ??
                throw new InvalidDataException("Authored navigation lacks tuning.");
            writer.Write(tuning.SpeedUnitsPerMinute);
            writer.Write(tuning.FuelPerUnit);
            writer.Write(tuning.IceDragPermille);
            writer.Write(tuning.StormDragPermille);
            writer.Write(tuning.SafeIcePermille);
            writer.Write(tuning.IceDamagePerMinute);
            writer.Write(tuning.StormDamagePerMinute);
            WriteId(writer, navigation.CurrentWaypoint);
            writer.Write(navigation.HullIntegrityPermille);
            WriteCount(writer, navigation.ActiveLegIds.Count);
            foreach (var id in navigation.ActiveLegIds) WriteId(writer, id);
            writer.Write(navigation.ActiveLegIndex);
            writer.Write(navigation.ProgressUnits);
        }

        private static ShipNavigationState ReadNavigation(BinaryReader reader)
        {
            var present = reader.ReadByte();
            if (present == 0) return ShipNavigationState.Empty;
            if (present != 1) throw new InvalidDataException("Invalid navigation presence flag.");
            var points = new List<SeaWaypoint>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                points.Add(new SeaWaypoint(ReadId(reader), reader.ReadInt32(), reader.ReadInt32()));
            var legs = new List<SeaLeg>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                legs.Add(new SeaLeg(ReadId(reader), ReadId(reader), ReadId(reader),
                    reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));
            var tuning = new NavigationTuning(reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(),
                reader.ReadInt32(), reader.ReadInt32());
            var waypoint = ReadId(reader);
            int hull = reader.ReadInt32();
            var route = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                route.Add(ReadId(reader));
            return ShipNavigationState.RestoreSnapshot(points, legs, waypoint, tuning, hull,
                route, reader.ReadInt32(), reader.ReadInt32());
        }
    }
}
