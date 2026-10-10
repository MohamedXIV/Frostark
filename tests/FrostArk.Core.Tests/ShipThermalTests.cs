using System;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class ShipThermalTests
    {
        private static readonly EntityId BoilerRoom = EntityId.Parse("room:boiler");
        private static readonly EntityId Cabin = EntityId.Parse("room:cabin");

        private static ShipThermalState Ship(int cabinInsulation = 700, int cabinIntegrity = 1000,
            long boilerFuelPerMinute = 1)
        {
            return ShipThermalState.Create(new[]
            {
                new ThermalCompartment(BoilerRoom, heatCapacity: 1000, energy: 20_000_000,
                    exteriorConductance: 2000, insulationPermille: 900, integrityPermille: 1000),
                new ThermalCompartment(Cabin, heatCapacity: 1000, energy: 20_000_000,
                    exteriorConductance: 5000, insulationPermille: cabinInsulation,
                    integrityPermille: cabinIntegrity),
            }, new[] { new ThermalConnection(BoilerRoom, Cabin, conductance: 200) },
                boilerFuelPerMinute == 0 ? BoilerConfiguration.Disabled :
                    new BoilerConfiguration(BoilerRoom, boilerFuelPerMinute, energyPerFuelUnit: 3000));
        }

        [Test]
        public void InternalTransfersDoNotCreateOrDestroyHeat()
        {
            var model = Ship();
            var first = ShipThermalModel.Step(model, ambientMilliC: 20_000, availableFuelUnits: 0);
            Assert.That(first.FuelConsumed, Is.Zero);
            Assert.That(first.HeatAdded, Is.Zero);
            Assert.That(first.ExteriorTransfer, Is.Zero);
            Assert.That(first.TotalEnergyAfter, Is.EqualTo(first.TotalEnergyBefore));

            var coldCabin = ShipThermalState.Create(new[]
            {
                new ThermalCompartment(BoilerRoom, 1000, 20_000_000, 0, 1000, 1000),
                new ThermalCompartment(Cabin, 1000, 0, 0, 1000, 1000)
            }, new[] { new ThermalConnection(BoilerRoom, Cabin, 250) },
                new BoilerConfiguration(BoilerRoom, 1, 1000));
            var transfer = ShipThermalModel.Step(coldCabin, ambientMilliC: 0, availableFuelUnits: 0);
            Assert.That(transfer.TotalEnergyAfter, Is.EqualTo(transfer.TotalEnergyBefore));
            Assert.That(transfer.State.Get(Cabin).Energy,
                Is.GreaterThan(coldCabin.Get(Cabin).Energy));
            Assert.That(transfer.State.Get(BoilerRoom).Energy,
                Is.LessThan(coldCabin.Get(BoilerRoom).Energy));
        }

        [Test]
        public void FuelBurnIsLimitedByActualStockAndAuditable()
        {
            var ship = Ship();
            var tick = ShipThermalModel.Step(ship, ambientMilliC: -20_000, availableFuelUnits: 1);

            Assert.That(tick.FuelConsumed, Is.EqualTo(1));
            Assert.That(tick.HeatAdded, Is.EqualTo(3000));
            Assert.That(tick.TotalEnergyAfter - tick.TotalEnergyBefore,
                Is.EqualTo(tick.HeatAdded + tick.ExteriorTransfer));

            var empty = ShipThermalModel.Step(ship, -20_000, availableFuelUnits: 0);
            Assert.That(empty.FuelConsumed, Is.Zero);
            Assert.That(empty.HeatAdded, Is.Zero);
        }

        [Test]
        public void InsulationAndIntegrityChangeColdExposureWithoutInventingHeat()
        {
            var insulated = Ship(cabinInsulation: 950, cabinIntegrity: 1000, boilerFuelPerMinute: 0);
            var damaged = Ship(cabinInsulation: 200, cabinIntegrity: 300, boilerFuelPerMinute: 0);
            long insulatedOut = 0;
            long damagedOut = 0;
            for (int i = 0; i < 480; i++)
            {
                var a = ShipThermalModel.Step(insulated, -30_000, 0);
                var b = ShipThermalModel.Step(damaged, -30_000, 0);
                insulatedOut += a.ExteriorTransfer;
                damagedOut += b.ExteriorTransfer;
                insulated = a.State;
                damaged = b.State;
            }

            Assert.That(damaged.Get(Cabin).TemperatureMilliC,
                Is.LessThan(insulated.Get(Cabin).TemperatureMilliC));
            Assert.That(damagedOut, Is.LessThan(insulatedOut));
            Assert.That(damaged.TotalEnergy, Is.EqualTo(40_000_000 + damagedOut));
        }

        [Test]
        public void ColdStreakWarnsAfterThirtyMinutesAndCriticalAfterNinety()
        {
            var cold = ShipThermalState.Create(new[]
            {
                new ThermalCompartment(Cabin, 1000, -10_000_000, 0, 1000, 1000)
            }, Array.Empty<ThermalConnection>(), BoilerConfiguration.Disabled);
            for (int minute = 1; minute <= 90; minute++)
            {
                cold = ShipThermalModel.Step(cold, -10_000, 0).State;
                if (minute == 29)
                    Assert.That(cold.Get(Cabin).Warning, Is.EqualTo(ThermalWarning.None));
                if (minute == 30)
                    Assert.That(cold.Get(Cabin).Warning, Is.EqualTo(ThermalWarning.Cold));
            }
            Assert.That(cold.Get(Cabin).Warning, Is.EqualTo(ThermalWarning.CriticalCold));
            Assert.That(cold.Get(Cabin).ColdExposureMinutes, Is.EqualTo(90));
        }

        [Test]
        public void InvalidHeatGraphAndNegativeFuelAreRejected()
        {
            Assert.Throws<ArgumentException>(() => ShipThermalState.Create(
                new[] { new ThermalCompartment(Cabin, 1000, 0, 0, 1000, 1000) },
                new[] { new ThermalConnection(Cabin, BoilerRoom, 5) }, BoilerConfiguration.Disabled));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalCompartment(
                Cabin, 0, 0, 0, 1000, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ThermalCompartment(
                Cabin, 1000, 0, 0, 1001, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => ShipThermalModel.Step(Ship(), 0, -1));
        }
    }
}
