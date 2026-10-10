using System;

namespace CookAndRun.Progression
{
    /// <summary>Team-owned offer data; prices/effects are not invented by the progression core.</summary>
    public sealed class ShopOffer
    {
        public string Id { get; }
        public long Price { get; }
        public int MaxLevel { get; }

        public ShopOffer(string id, long price, int maxLevel = 1)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("An offer ID is required.", nameof(id));
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            Id = id;
            Price = price;
            MaxLevel = maxLevel;
        }
    }
}
