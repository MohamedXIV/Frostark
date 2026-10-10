using System;
using FrostArk.Core.Commands;
using FrostArk.Core.Economy;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;
using NUnit.Framework;

namespace FrostArk.Core.Tests
{
    public sealed class MarketTradeTests
    {
        private static readonly EntityId Scrap = EntityId.Parse("item:scrap");
        private static readonly EntityId Heater = EntityId.Parse("item:heater");
        private static readonly EntityId Coin = EntityId.Parse("supply:credits");
        private static readonly EntityId Market = EntityId.Parse("market:port-a");

        private static WorldState Start(long wallet = 100, int shipSlots = 4)
        {
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Scrap, 1, 100, 10),
                new ItemDefinition(Heater, 1, 2000, 1)
            }), Array.Empty<SalvagePickup>(), 20000, 4, 20000, shipSlots);
            var market = new MarketState(Market, credits: 200,
                new[] { new MarketListing(Heater, stock: 2, askPrice: 30, bidPrice: 20) });
            var economy = EconomyState.Create(new WorkshopCatalog(Array.Empty<WorkshopRecipe>()),
                Array.Empty<EntityId>(), markets: new[] { market });
            return WorldState.Create(WorldMinute.Zero, new[]
                { new SupplyStock(Coin, wallet) }, Array.Empty<EntityId>(),
                inventory: inventory, economy: economy);
        }

        [Test]
        public void BuyAndSellMoveRealCreditsStockAndItemsInBothDirections()
        {
            var bus = new WorldCommandBus();
            var begin = Start();
            var buy = bus.Execute(begin, new BuyMarketItemCommand(
                EntityId.Parse("cmd:buy"), Market, Heater, 1, 0));
            Assert.That(buy.Success, Is.True);
            Assert.That(buy.Events[0].Kind, Is.EqualTo(WorldEventKind.MarketBought));
            Assert.That(buy.State.GetSupply(Coin), Is.EqualTo(70));
            Assert.That(buy.State.Economy.GetMarket(Market).Credits, Is.EqualTo(230));
            Assert.That(buy.State.Economy.GetMarket(Market).Get(Heater).Stock, Is.EqualTo(1));
            Assert.That(buy.State.Inventory.GetAmount(InventoryContainer.ShipHold, Heater),
                Is.EqualTo(1));

            var sell = bus.Execute(buy.State, new SellMarketItemCommand(
                EntityId.Parse("cmd:sell"), Market, Heater, 1, 1));
            Assert.That(sell.Success, Is.True);
            Assert.That(sell.Events[0].Kind, Is.EqualTo(WorldEventKind.MarketSold));
            Assert.That(sell.State.GetSupply(Coin), Is.EqualTo(90));
            Assert.That(sell.State.Economy.GetMarket(Market).Credits, Is.EqualTo(210));
            Assert.That(sell.State.Economy.GetMarket(Market).Get(Heater).Stock, Is.EqualTo(2));
            Assert.That(sell.State.Inventory.GetAmount(InventoryContainer.ShipHold, Heater),
                Is.Zero);
            Assert.That(begin.Fingerprint(), Is.Not.EqualTo(sell.State.Fingerprint()));
        }

        [Test]
        public void NoFreeItemsFromInsufficientWalletMerchantStockOrShipSpace()
        {
            var bus = new WorldCommandBus();
            var broke = Start(wallet: 0);
            var noCoins = bus.Execute(broke, new BuyMarketItemCommand(
                EntityId.Parse("cmd:coins"), Market, Heater, 1, 0));
            Assert.That(noCoins.Failure, Is.EqualTo(CommandFailure.InsufficientCurrency));
            Assert.That(noCoins.State, Is.SameAs(broke));

            var noItems = bus.Execute(Start(), new BuyMarketItemCommand(
                EntityId.Parse("cmd:stock"), Market, Heater, 3, 0));
            Assert.That(noItems.Failure, Is.EqualTo(CommandFailure.MarketOutOfStock));
            Assert.That(noItems.State.Inventory.GetAmount(InventoryContainer.ShipHold, Heater), Is.Zero);

            var noSpace = bus.Execute(Start(shipSlots: 0), new BuyMarketItemCommand(
                EntityId.Parse("cmd:space"), Market, Heater, 1, 0));
            Assert.That(noSpace.Success, Is.False);
            Assert.That(noSpace.State, Is.SameAs(noSpace.State));
            Assert.That(noSpace.State.GetSupply(Coin), Is.EqualTo(100));
        }

        [Test]
        public void SellingWithoutOwnedItemOrMerchantFundsNeverCreatesCurrency()
        {
            var bus = new WorldCommandBus();
            var before = Start();
            var sell = bus.Execute(before, new SellMarketItemCommand(
                EntityId.Parse("cmd:sell-none"), Market, Heater, 1, 0));
            Assert.That(sell.Failure, Is.EqualTo(CommandFailure.InsufficientInventory));
            Assert.That(sell.State, Is.SameAs(before));
        }

        [Test]
        public void SaveAfterBuyRestoresMarketCashStockAndInventoryExactly()
        {
            var bought = new WorldCommandBus().Execute(Start(),
                new BuyMarketItemCommand(EntityId.Parse("cmd:buy"), Market, Heater, 1, 0));
            Assert.That(bought.Success, Is.True);
            var timer = new SimulationScheduler(new WorldClock(bought.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var blob = SaveCodec.Encode(SaveGameDto.Capture(bought.State, timer));
            var loaded = SaveCodec.TryDecode(blob);
            Assert.That(loaded.Success, Is.True, loaded.Error.ToString());
            Assert.That(loaded.Value.State.Fingerprint(), Is.EqualTo(bought.State.Fingerprint()));
            Assert.That(SaveCodec.Encode(loaded.Value), Is.EqualTo(blob));
            var replay = new WorldCommandBus().Execute(loaded.Value.State,
                new BuyMarketItemCommand(EntityId.Parse("cmd:buy"), Market, Heater, 1, 1));
            Assert.That(replay.Failure, Is.EqualTo(CommandFailure.DuplicateCommand));
        }

        [Test]
        public void MerchantCannotPurchaseWhenItsActualWalletIsEmpty()
        {
            var issued = Start().Inventory.TryExchangeShip(
                Array.Empty<System.Collections.Generic.KeyValuePair<EntityId,long>>(),
                new[] { new System.Collections.Generic.KeyValuePair<EntityId,long>(Heater, 1) });
            Assert.That(issued.Success, Is.True);
            var market = new MarketState(Market, credits: 0,
                new[] { new MarketListing(Heater, 1, 30, 20) });
            var economy = EconomyState.Create(
                new WorkshopCatalog(Array.Empty<WorkshopRecipe>()),
                Array.Empty<EntityId>(), markets: new[] { market });
            var before = WorldState.Create(WorldMinute.Zero,
                new[] { new SupplyStock(Coin, 100) }, Array.Empty<EntityId>(),
                inventory: issued.State, economy: economy);
            var sold = new WorldCommandBus().Execute(before,
                new SellMarketItemCommand(EntityId.Parse("cmd:drain"), Market, Heater, 1, 0));
            Assert.That(sold.Failure, Is.EqualTo(CommandFailure.MerchantCannotAfford));
            Assert.That(sold.State, Is.SameAs(before));
            Assert.That(before.Inventory.GetAmount(InventoryContainer.ShipHold, Heater),
                Is.EqualTo(1));
            Assert.That(before.GetSupply(Coin), Is.EqualTo(100));
        }

        [Test]
        public void ForgedNegativeMarketCreditsRejectedEvenWithCorrectChecksum()
        {
            var bought = new WorldCommandBus().Execute(Start(),
                new BuyMarketItemCommand(EntityId.Parse("cmd:buy-safe"), Market, Heater, 1, 0));
            Assert.That(bought.Success, Is.True);
            var timer = new SimulationScheduler(new WorldClock(bought.State.Minute),
                Array.Empty<IWorldMinuteSystem>());
            var blob = SaveCodec.Encode(SaveGameDto.Capture(bought.State, timer));
            var marker = System.Text.Encoding.UTF8.GetBytes("market:port-a");
            int position = -1;
            for (int i = 0; i <= blob.Length - 32 - marker.Length; i++)
            {
                bool same = true;
                for (int j = 0; j < marker.Length; j++)
                    if (blob[i + j] != marker[j]) { same = false; break; }
                if (same) { position = i; break; }
            }
            Assert.That(position, Is.GreaterThanOrEqualTo(0));
            // Market cash follows length-prefixed merchant ID.
            Array.Copy(BitConverter.GetBytes(-1L), 0, blob, position + marker.Length, 8);
            using (var sha = System.Security.Cryptography.SHA256.Create())
                Array.Copy(sha.ComputeHash(blob, 0, blob.Length - 32),
                    0, blob, blob.Length - 32, 32);
            var invalid = SaveCodec.TryDecode(blob);
            Assert.That(invalid.Success, Is.False);
            Assert.That(invalid.Error, Is.EqualTo(SaveDecodeError.InvalidData));
        }

        [Test]
        public void InvalidMerchantListingsAndPricesAreRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new MarketListing(Heater, 1, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new MarketListing(Heater, -1, 30, 20));
            Assert.Throws<ArgumentException>(() => new MarketState(Market, 100, new[]
            {
                new MarketListing(Heater, 1, 30, 20),
                new MarketListing(Heater, 1, 30, 20)
            }));
        }
    }
}
