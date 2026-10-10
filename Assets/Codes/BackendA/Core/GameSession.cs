using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace CookAndRun.Progression
{
    /// <summary>
    /// Backend A's single writer for progression and currency. Use on Unity's main thread.
    /// B submits completed order outcomes before the facade advances time in LateUpdate.
    /// </summary>
    public sealed class GameSession
    {
        private static int nextRunId;
        private readonly long initialWallet;
        private readonly bool allowNegativeWallet;
        private readonly ChapterRules[] rules;
        private readonly List<OrderSettlement> settledOrders = new List<OrderSettlement>();
        private readonly HashSet<string> settledOrderIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> purchases = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly ReadOnlyCollection<OrderSettlement> readOnlyOrders;
        private long baseRewardTotal;
        private long discountTotal;
        private long changeLossTotal;

        public SessionState State { get; private set; }
        public int ChapterNumber { get; private set; }
        public double RemainingSeconds { get; private set; }
        public long Wallet { get; private set; }
        public long ChapterRevenue { get; private set; }
        public int CompletedOrders => settledOrders.Count;
        public int AccurateOrders { get; private set; }
        public double AccuracyPercent => CompletedOrders == 0 ? 0 : 100.0 * AccurateOrders / CompletedOrders;
        public bool IsPaused { get; private set; }
        public int LoadingDepth { get; private set; }
        public bool CanAcceptActions => State == SessionState.Playing && !IsPaused && LoadingDepth == 0 && RemainingSeconds > 0;
        public ChapterResult LastResult { get; private set; }
        public int RunId { get; private set; }
        public ChapterRules CurrentRules => rules[ChapterNumber - 1];
        public IReadOnlyList<OrderSettlement> SettledOrders => readOnlyOrders;

        public GameSession(long initialWallet = 0, bool allowNegativeWallet = true, ChapterRules[] rules = null)
        {
            if (!allowNegativeWallet && initialWallet < 0) throw new ArgumentOutOfRangeException(nameof(initialWallet));
            var sourceRules = rules ?? ChapterRules.CreateDefaults();
            if (sourceRules.Length == 0) throw new ArgumentException("At least one chapter is required.", nameof(rules));
            this.rules = (ChapterRules[])sourceRules.Clone();
            for (int i = 0; i < this.rules.Length; i++)
            {
                if (this.rules[i] == null || this.rules[i].ChapterNumber != i + 1)
                    throw new ArgumentException("Chapter rules must be numbered consecutively from 1.", nameof(rules));
            }
            this.initialWallet = initialWallet;
            this.allowNegativeWallet = allowNegativeWallet;
            readOnlyOrders = settledOrders.AsReadOnly();
            ResetRun();
        }

        public bool StartChapter()
        {
            if (State != SessionState.Ready || LoadingDepth > 0) return false;
            IsPaused = false;
            State = SessionState.Playing;
            return true;
        }

        /// <summary>
        /// Pass elapsed seconds once per frame, after completed-order callbacks.
        /// Invoke with zero too: a settlement time penalty may have exhausted the timer.
        /// </summary>
        public void AdvanceTime(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (State != SessionState.Playing) return;
            if (RemainingSeconds <= 0)
            {
                FinishChapter();
                return;
            }
            if (IsPaused || LoadingDepth > 0) return;
            RemainingSeconds = Math.Max(0, RemainingSeconds - deltaSeconds);
            if (RemainingSeconds <= 0) FinishChapter();
        }

        public void SetPaused(bool paused)
        {
            if (State == SessionState.Playing) IsPaused = paused;
        }

        public void BeginLoading()
        {
            LoadingDepth = checked(LoadingDepth + 1);
        }

        /// <summary>Nested loading needs matching ends. An unmatched end is a harmless no-op.</summary>
        public void EndLoading()
        {
            if (LoadingDepth > 0) LoadingDepth--;
        }

        public bool TrySettleOrder(OrderOutcome outcome)
        {
            if (outcome == null || !CanAcceptActions || outcome.RunId != RunId ||
                outcome.ChapterNumber != ChapterNumber || settledOrderIds.Contains(outcome.OrderId)) return false;

            var settlement = new OrderSettlement(outcome);
            long newWallet;
            long newRevenue;
            long newBase;
            long newDiscount;
            long newLoss;
            try
            {
                // Check every aggregate before mutating, so a malformed huge input cannot half-settle.
                newWallet = checked(Wallet + settlement.NetReward);
                newRevenue = checked(ChapterRevenue + settlement.NetReward);
                newBase = checked(baseRewardTotal + outcome.BaseReward);
                newDiscount = checked(discountTotal + settlement.DiscountAmount);
                newLoss = checked(changeLossTotal + outcome.ChangeLoss);
            }
            catch (OverflowException)
            {
                return false;
            }

            Wallet = allowNegativeWallet ? newWallet : Math.Max(0, newWallet);
            ChapterRevenue = newRevenue;
            baseRewardTotal = newBase;
            discountTotal = newDiscount;
            changeLossTotal = newLoss;
            settledOrderIds.Add(outcome.OrderId);
            settledOrders.Add(settlement);
            if (settlement.IsAccurate) AccurateOrders++;
            RemainingSeconds = Math.Max(0, RemainingSeconds - outcome.TimePenaltySeconds);
            return true;
        }

        public bool ContinueAfterResult()
        {
            if (State != SessionState.Result) return false;
            State = LastResult.Passed && ChapterNumber < rules.Length ? SessionState.Shop : SessionState.Ending;
            return true;
        }

        /// <summary>Only a passed nonfinal chapter's shop can prepare the next chapter.</summary>
        public bool PrepareNextChapter()
        {
            if (State != SessionState.Shop || ChapterNumber >= rules.Length) return false;
            ChapterNumber++;
            ClearChapter();
            State = SessionState.Ready;
            return true;
        }

        public bool TryPurchase(ShopOffer offer)
        {
            if (offer == null || State != SessionState.Shop || LoadingDepth > 0 || Wallet < offer.Price) return false;
            int level = GetPurchaseLevel(offer.Id);
            if (level >= offer.MaxLevel) return false;
            Wallet -= offer.Price;
            purchases[offer.Id] = level + 1;
            return true;
        }

        public int GetPurchaseLevel(string id)
        {
            return id != null && purchases.TryGetValue(id, out int level) ? level : 0;
        }

        public void ResetRun()
        {
            int token = Interlocked.Increment(ref nextRunId);
            if (token <= 0) throw new InvalidOperationException("Run identity range exhausted.");
            RunId = token;
            ChapterNumber = 1;
            Wallet = initialWallet;
            LoadingDepth = 0;
            purchases.Clear();
            ClearChapter();
            State = SessionState.Ready;
        }

        private void ClearChapter()
        {
            RemainingSeconds = CurrentRules.DurationSeconds;
            ChapterRevenue = 0;
            AccurateOrders = 0;
            baseRewardTotal = 0;
            discountTotal = 0;
            changeLossTotal = 0;
            IsPaused = false;
            LastResult = null;
            settledOrders.Clear();
            settledOrderIds.Clear();
        }

        private void FinishChapter()
        {
            RemainingSeconds = 0;
            IsPaused = false;
            LastResult = new ChapterResult(RunId, CurrentRules, baseRewardTotal, discountTotal,
                changeLossTotal, ChapterRevenue, Wallet, AccurateOrders, settledOrders);
            State = SessionState.Result;
        }
    }
}
