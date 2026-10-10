using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Economy
{
    /// <summary>Finite counterparty stock and funds; bid must be below ask.</summary>
    public sealed class MarketListing
    {
        public EntityId ItemId { get; }
        public long Stock { get; }
        public long AskPrice { get; }
        public long BidPrice { get; }
        public MarketListing(EntityId itemId, long stock, long askPrice, long bidPrice)
        {
            if (!itemId.IsValid) throw new ArgumentException("Market item ID needed.", nameof(itemId));
            if (stock < 0) throw new ArgumentOutOfRangeException(nameof(stock));
            if (askPrice <= 0) throw new ArgumentOutOfRangeException(nameof(askPrice));
            if (bidPrice <= 0 || bidPrice > askPrice)
                throw new ArgumentOutOfRangeException(nameof(bidPrice));
            ItemId = itemId; Stock = stock; AskPrice = askPrice; BidPrice = bidPrice;
        }
        internal MarketListing WithStock(long amount) =>
            new MarketListing(ItemId, amount, AskPrice, BidPrice);
    }

    public sealed class MarketState
    {
        private readonly MarketListing[] _offers;
        public EntityId Id { get; }
        public long Credits { get; }
        public IReadOnlyList<MarketListing> Offers => Array.AsReadOnly(_offers);

        public MarketState(EntityId id, long credits, IEnumerable<MarketListing> listings)
        {
            if (!id.IsValid) throw new ArgumentException("Market ID required.", nameof(id));
            if (credits < 0) throw new ArgumentOutOfRangeException(nameof(credits));
            if (listings == null) throw new ArgumentNullException(nameof(listings));
            var rows = listings.ToArray();
            if (rows.Any(x => x == null)) throw new ArgumentException("Null market listing.", nameof(listings));
            rows = rows.OrderBy(x => x.ItemId.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < rows.Length; i++)
                if (rows[i].ItemId == rows[i - 1].ItemId)
                    throw new ArgumentException("Duplicate listed item.", nameof(listings));
            Id = id; Credits = credits; _offers = rows;
        }

        public MarketListing Get(EntityId itemId) =>
            itemId.IsValid ? Array.Find(_offers, x => x.ItemId == itemId) : null;

        internal MarketState BuyFrom(long quantity, EntityId itemId, long amount)
        {
            var offer = Get(itemId) ?? throw new ArgumentException("Unknown listing.");
            if (quantity <= 0 || offer.Stock < quantity || amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            return new MarketState(Id, checked(Credits + amount),
                _offers.Select(x => x.ItemId == itemId ? x.WithStock(x.Stock - quantity) : x));
        }

        internal MarketState SellTo(long quantity, EntityId itemId, long payout)
        {
            var offer = Get(itemId) ?? throw new ArgumentException("Unknown listing.");
            if (quantity <= 0 || Credits < payout || payout <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            return new MarketState(Id, Credits - payout,
                _offers.Select(x => x.ItemId == itemId ?
                    x.WithStock(checked(x.Stock + quantity)) : x));
        }
    }
}
