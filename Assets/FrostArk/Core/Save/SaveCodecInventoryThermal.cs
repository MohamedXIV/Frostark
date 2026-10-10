using System;
using System.Collections.Generic;
using System.IO;
using FrostArk.Core.Inventory;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteInventory(BinaryWriter writer, InventoryState inventory)
        {
            writer.Write(inventory.PackMaxWeightGrams);
            writer.Write(inventory.PackMaxSlots);
            writer.Write(inventory.ShipMaxWeightGrams);
            writer.Write(inventory.ShipMaxSlots);

            var definitions = inventory.SnapshotDefinitions();
            WriteCount(writer, definitions.Count);
            foreach (var item in definitions)
            {
                WriteId(writer, item.Id);
                writer.Write(item.ContentVersion);
                writer.Write(item.UnitWeightGrams);
                writer.Write(item.MaxStackSize);
            }

            var pickups = inventory.SnapshotPickups();
            WriteCount(writer, pickups.Count);
            foreach (var pickup in pickups)
            {
                WriteId(writer, pickup.Id);
                WriteId(writer, pickup.ItemId);
                writer.Write(pickup.Quantity);
            }

            var collected = inventory.SnapshotCollected();
            WriteCount(writer, collected.Count);
            foreach (var id in collected) WriteId(writer, id);

            foreach (var location in new[] { InventoryContainer.Pack, InventoryContainer.ShipHold })
            {
                var stock = inventory.SnapshotStock(location);
                WriteCount(writer, stock.Count);
                foreach (var stack in stock)
                {
                    WriteId(writer, stack.Key);
                    writer.Write(stack.Value);
                }
            }
        }

        private static InventoryState ReadInventory(BinaryReader reader)
        {
            long packWeight = reader.ReadInt64();
            int packSlots = reader.ReadInt32();
            long shipWeight = reader.ReadInt64();
            int shipSlots = reader.ReadInt32();

            var definitions = new List<ItemDefinition>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                definitions.Add(new ItemDefinition(ReadId(reader), reader.ReadInt32(),
                    reader.ReadInt64(), reader.ReadInt32()));
            var catalog = new ItemCatalog(definitions);

            var pickups = new List<SalvagePickup>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                pickups.Add(new SalvagePickup(ReadId(reader), ReadId(reader), reader.ReadInt64()));

            var collected = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                collected.Add(ReadId(reader));

            List<KeyValuePair<EntityId, long>> ReadStock()
            {
                var values = new List<KeyValuePair<EntityId, long>>();
                for (int i = 0, count = ReadCount(reader); i < count; i++)
                    values.Add(new KeyValuePair<EntityId, long>(ReadId(reader), reader.ReadInt64()));
                return values;
            }

            var pack = ReadStock();
            var ship = ReadStock();
            return InventoryState.RestoreSnapshot(catalog, pickups, collected, pack, ship,
                packWeight, packSlots, shipWeight, shipSlots);
        }


        private static void WriteThermal(BinaryWriter writer, ShipThermalState state)
        {
            var rooms = state.Rooms;
            WriteCount(writer, rooms.Count);
            foreach (var room in rooms)
            {
                WriteId(writer, room.Id);
                writer.Write(room.HeatCapacity);
                writer.Write(room.Energy);
                writer.Write(room.ExteriorConductance);
                writer.Write(room.InsulationPermille);
                writer.Write(room.IntegrityPermille);
                writer.Write(room.ColdExposureMinutes);
            }

            var connections = state.Connections;
            WriteCount(writer, connections.Count);
            foreach (var connection in connections)
            {
                WriteId(writer, connection.A);
                WriteId(writer, connection.B);
                writer.Write(connection.Conductance);
            }

            // Disabled boiler is encoded as zero-length name + zero rates.
            WriteText(writer, state.Boiler.RoomId.Value ?? "");
            writer.Write(state.Boiler.FuelUnitsPerMinute);
            writer.Write(state.Boiler.EnergyPerFuelUnit);
        }

        private static ShipThermalState ReadThermal(BinaryReader reader)
        {
            var rooms = new List<ThermalCompartment>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                rooms.Add(new ThermalCompartment(ReadId(reader), reader.ReadInt64(),
                    reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32(),
                    reader.ReadInt32(), reader.ReadInt32()));

            var links = new List<ThermalConnection>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                links.Add(new ThermalConnection(ReadId(reader), ReadId(reader), reader.ReadInt32()));

            string boilerName = ReadText(reader);
            long burn = reader.ReadInt64();
            long heatYield = reader.ReadInt64();
            var boiler = new BoilerConfiguration(
                boilerName.Length == 0 ? default : EntityId.Parse(boilerName),
                burn, heatYield);
            return ShipThermalState.Create(rooms, links, boiler);
        }

    }
}
