using System;
using System.Collections.Generic;
using FrostArk.Core.World;

namespace FrostArk.Core.Ship
{
    public sealed class ThermalTickResult
    {
        public ShipThermalState State { get; }
        public long FuelConsumed { get; }
        public long HeatAdded { get; }
        /// <summary>Signed energy transferred FROM outside TO ship (negative = loss).</summary>
        public long ExteriorTransfer { get; }
        public long TotalEnergyBefore { get; }
        public long TotalEnergyAfter => State.TotalEnergy;

        internal ThermalTickResult(ShipThermalState state, long fuel, long added,
            long exterior, long before)
        {
            State = state;
            FuelConsumed = fuel;
            HeatAdded = added;
            ExteriorTransfer = exterior;
            TotalEnergyBefore = before;
        }
    }

    /// <summary>
    /// Pure one-world-minute thermal reducer; all energy changes accounted for
    /// by symmetric internal transfers, ambient exchange and boiler fuel.
    /// Deliberately no wall clock, Unity physics, float or hidden RNG.
    /// </summary>
    public static class ShipThermalModel
    {
        public static ThermalTickResult Step(ShipThermalState state,
            long ambientMilliC, long availableFuelUnits)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (availableFuelUnits < 0)
                throw new ArgumentOutOfRangeException(nameof(availableFuelUnits));
            var updates = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var room in state.Rooms) updates.Add(room.Id.Value, 0L);

            // All edge fluxes derive from PRE-tick temperatures. Pairwise
            // changes are symmetric and cannot create net internal energy.
            foreach (var link in state.Connections)
            {
                var a = state.Get(link.A);
                var b = state.Get(link.B);
                long deltaTemp = checked(a.TemperatureMilliC - b.TemperatureMilliC);
                long flux = checked(deltaTemp * link.Conductance) / 1000;
                long cap = checked(Math.Abs(deltaTemp) * Math.Min(a.HeatCapacity, b.HeatCapacity)) / 2;
                flux = Math.Max(-cap, Math.Min(cap, flux));
                updates[a.Id.Value] = checked(updates[a.Id.Value] - flux);
                updates[b.Id.Value] = checked(updates[b.Id.Value] + flux);
            }

            long exterior = 0;
            foreach (var room in state.Rooms)
            {
                long difference = checked(ambientMilliC - room.TemperatureMilliC);
                // Conductance is energy/milli-degree per minute scaled by 1k.
                // Undamaged insulation narrows the leak; hull damage enlarges it.
                long gain = checked(difference * room.ExteriorConductance);
                gain = checked(gain * (1000 - room.InsulationPermille));
                gain = checked(gain * (2000 - room.IntegrityPermille)) / 1_000_000_000L;
                long limit = checked(Math.Abs(difference) * room.HeatCapacity);
                gain = Math.Max(-limit, Math.Min(limit, gain));
                updates[room.Id.Value] = checked(updates[room.Id.Value] + gain);
                exterior = checked(exterior + gain);
            }

            long fuel = 0;
            long heat = 0;
            if (state.Boiler.IsEnabled)
            {
                fuel = Math.Min(state.Boiler.FuelUnitsPerMinute, availableFuelUnits);
                heat = checked(fuel * state.Boiler.EnergyPerFuelUnit);
                updates[state.Boiler.RoomId.Value] =
                    checked(updates[state.Boiler.RoomId.Value] + heat);
            }

            var nextRooms = new List<ThermalCompartment>();
            foreach (var room in state.Rooms)
                nextRooms.Add(room.AtEnergy(checked(room.Energy + updates[room.Id.Value])));
            var next = ShipThermalState.Create(nextRooms, state.Connections, state.Boiler);
            if (checked(next.TotalEnergy - state.TotalEnergy) != checked(heat + exterior))
                throw new InvalidOperationException("Thermal conservation ledger mismatch.");
            return new ThermalTickResult(next, fuel, heat, exterior, state.TotalEnergy);
        }
    }
}
