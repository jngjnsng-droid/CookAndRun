using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CookAndRun.Progression
{
    /// <summary>Finalized result; later shopping or a new run cannot change this snapshot.</summary>
    public sealed class ChapterResult
    {
        public int RunId { get; }
        public int ChapterNumber { get; }
        public long RevenueTarget { get; }
        public int MinimumAccuracyPercent { get; }
        public long BaseRewardTotal { get; }
        public long DiscountTotal { get; }
        public long ChangeLossTotal { get; }
        public long NetRevenue { get; }
        public long WalletAtFinish { get; }
        public int CompletedOrders { get; }
        public int AccurateOrders { get; }
        public double AccuracyPercent { get; }
        public bool Passed { get; }
        public IReadOnlyList<OrderSettlement> Orders { get; }

        internal ChapterResult(int runId, ChapterRules rules, long baseRewardTotal, long discountTotal,
            long changeLossTotal, long netRevenue, long wallet, int accurateOrders,
            IList<OrderSettlement> orders)
        {
            RunId = runId;
            ChapterNumber = rules.ChapterNumber;
            RevenueTarget = rules.RevenueTarget;
            MinimumAccuracyPercent = rules.MinimumAccuracyPercent;
            BaseRewardTotal = baseRewardTotal;
            DiscountTotal = discountTotal;
            ChangeLossTotal = changeLossTotal;
            NetRevenue = netRevenue;
            WalletAtFinish = wallet;
            Orders = new ReadOnlyCollection<OrderSettlement>(new List<OrderSettlement>(orders));
            CompletedOrders = orders.Count;
            AccurateOrders = accurateOrders;
            AccuracyPercent = CompletedOrders == 0 ? 0 : 100.0 * AccurateOrders / CompletedOrders;

            // Compare integer ratios, never a rounded display value such as "80%".
            bool accuracyPassed = CompletedOrders == 0
                ? rules.MinimumAccuracyPercent == 0
                : (long)AccurateOrders * 100 >= (long)CompletedOrders * rules.MinimumAccuracyPercent;
            Passed = NetRevenue >= rules.RevenueTarget && accuracyPassed;
        }
    }
}
