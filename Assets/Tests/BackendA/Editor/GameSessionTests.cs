using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CookAndRun.Progression.Tests
{
    public sealed class GameSessionTests
    {
        private static OrderOutcome Outcome(GameSession session, string id, long reward,
            IEnumerable<OrderViolation> violations = null, long cashLoss = 0, double timePenalty = 0)
        {
            return new OrderOutcome(session.RunId, session.ChapterNumber, id, reward, violations, cashLoss, timePenalty);
        }

        private static void PassAndOpenShop(GameSession session)
        {
            Assert.That(session.StartChapter(), Is.True);
            Assert.That(session.TrySettleOrder(Outcome(session, "pass", session.CurrentRules.RevenueTarget)), Is.True);
            session.AdvanceTime(180);
            Assert.That(session.LastResult.Passed, Is.True);
            Assert.That(session.ContinueAfterResult(), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Shop));
        }

        [Test]
        public void StartsReady_OnlyExplicitStartRuns180SecondTimer()
        {
            var session = new GameSession();
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.ChapterNumber, Is.EqualTo(1));
            session.AdvanceTime(500);
            Assert.That(session.RemainingSeconds, Is.EqualTo(180));
            Assert.That(session.TrySettleOrder(Outcome(session, "early", 1000)), Is.False);
            Assert.That(session.StartChapter(), Is.True);
            Assert.That(session.StartChapter(), Is.False);
            session.AdvanceTime(179.5);
            Assert.That(session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(session.RemainingSeconds, Is.EqualTo(0.5));
            session.AdvanceTime(0.5);
            Assert.That(session.State, Is.EqualTo(SessionState.Result));
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That(session.LastResult.Passed, Is.False);
        }

        [Test]
        public void PauseAndNestedLoadingFreezeTimeAndRejectActions()
        {
            var session = new GameSession();
            session.BeginLoading();
            Assert.That(session.StartChapter(), Is.False);
            session.EndLoading();
            session.StartChapter();
            session.AdvanceTime(10);
            session.SetPaused(true);
            session.AdvanceTime(30);
            Assert.That(session.RemainingSeconds, Is.EqualTo(170));
            Assert.That(session.TrySettleOrder(Outcome(session, "paused", 1000)), Is.False);
            session.BeginLoading();
            session.BeginLoading();
            session.SetPaused(false);
            session.EndLoading();
            session.AdvanceTime(30);
            Assert.That(session.RemainingSeconds, Is.EqualTo(170));
            Assert.That(session.TrySettleOrder(Outcome(session, "loading", 1000)), Is.False);
            session.EndLoading();
            session.EndLoading();
            Assert.That(session.LoadingDepth, Is.Zero);
            session.AdvanceTime(1);
            Assert.That(session.RemainingSeconds, Is.EqualTo(169));
            Assert.That(session.CanAcceptActions, Is.True);
        }

        [Test]
        public void CompletionBeforeEndOfFrameCutoffCounts_LateCallbackDoesNot()
        {
            var session = new GameSession();
            session.StartChapter();
            session.AdvanceTime(179.5);
            Assert.That(session.TrySettleOrder(Outcome(session, "same-frame", 20000)), Is.True);
            session.AdvanceTime(0.5); // Unity facade does this after B's Update callbacks.
            Assert.That(session.LastResult.NetRevenue, Is.EqualTo(20000));
            Assert.That(session.LastResult.CompletedOrders, Is.EqualTo(1));
            Assert.That(session.LastResult.Passed, Is.True);
            Assert.That(session.TrySettleOrder(Outcome(session, "unfinished", 99999)), Is.False);
            var result = session.LastResult;
            session.AdvanceTime(999);
            Assert.That(session.LastResult, Is.SameAs(result));
            Assert.That(session.Wallet, Is.EqualTo(20000));
        }

        [Test]
        public void NoCompletedOrders_HasZeroRevenueZeroAccuracyAndFails()
        {
            var session = new GameSession();
            session.StartChapter();
            session.AdvanceTime(180);
            Assert.That(session.LastResult.CompletedOrders, Is.Zero);
            Assert.That(session.LastResult.AccuracyPercent, Is.Zero);
            Assert.That(session.LastResult.NetRevenue, Is.Zero);
            Assert.That(session.LastResult.Passed, Is.False);
            Assert.That(session.ContinueAfterResult(), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Ending));
            Assert.That(session.PrepareNextChapter(), Is.False);
        }

        [Test]
        public void MaximumPenaltyOnly_ThenSeparateCashLoss_AndAllReasonsRemain()
        {
            var violations = new List<OrderViolation>
            {
                new OrderViolation("bell", 50),
                new OrderViolation("recipe", 70),
                new OrderViolation("destination", 0)
            };
            var session = new GameSession();
            session.StartChapter();
            var completed = Outcome(session, "mixed", 10000, violations, 500);
            violations.Clear(); // Outcome must have copied the incoming collection.
            Assert.That(session.TrySettleOrder(completed), Is.True);
            var receipt = session.SettledOrders[0];
            Assert.That(receipt.AppliedPenaltyPercent, Is.EqualTo(70));
            Assert.That(receipt.DiscountAmount, Is.EqualTo(7000));
            Assert.That(receipt.RewardAfterDiscount, Is.EqualTo(3000));
            Assert.That(receipt.NetReward, Is.EqualTo(2500));
            Assert.That(receipt.Outcome.Violations.Count, Is.EqualTo(3));
            Assert.That(receipt.IsAccurate, Is.False);
            session.AdvanceTime(180);
            Assert.That(session.LastResult.BaseRewardTotal, Is.EqualTo(10000));
            Assert.That(session.LastResult.DiscountTotal, Is.EqualTo(7000));
            Assert.That(session.LastResult.ChangeLossTotal, Is.EqualTo(500));
            Assert.That(session.LastResult.NetRevenue, Is.EqualTo(2500));
        }

        [Test]
        public void DuplicateOrderIsRejectedWithoutMoneyCountOrTimeChange()
        {
            var session = new GameSession();
            session.StartChapter();
            var order = Outcome(session, "duplicate", 10000, timePenalty: 5);
            Assert.That(session.TrySettleOrder(order), Is.True);
            Assert.That(session.TrySettleOrder(order), Is.False);
            Assert.That(session.TrySettleOrder(Outcome(session, "duplicate", 99999)), Is.False);
            Assert.That(session.Wallet, Is.EqualTo(10000));
            Assert.That(session.CompletedOrders, Is.EqualTo(1));
            Assert.That(session.RemainingSeconds, Is.EqualTo(175));
        }

        [Test]
        public void RefundAndCashLossCanMakeNetNegative_WalletPolicyDoesNotChangeNet()
        {
            foreach (bool allowNegative in new[] { true, false })
            {
                var session = new GameSession(0, allowNegative);
                session.StartChapter();
                session.TrySettleOrder(Outcome(session, "refund", 10000,
                    new[] { new OrderViolation("refund", 100) }, 1500));
                Assert.That(session.ChapterRevenue, Is.EqualTo(-1500));
                Assert.That(session.Wallet, Is.EqualTo(allowNegative ? -1500 : 0));
                Assert.That(session.AccurateOrders, Is.Zero);
            }
        }

        [Test]
        public void FractionalWonPaymentIsTruncatedOnce()
        {
            var session = new GameSession();
            session.StartChapter();
            session.TrySettleOrder(Outcome(session, "fraction", 101,
                new[] { new OrderViolation("half", 50) }));
            Assert.That(session.SettledOrders[0].RewardAfterDiscount, Is.EqualTo(50));
            Assert.That(session.SettledOrders[0].DiscountAmount, Is.EqualTo(51));
        }

        [Test]
        public void AnyViolationOrCashMistakeIsInaccurate_EvenWithNoPercentageDiscount()
        {
            var session = new GameSession();
            session.StartChapter();
            session.TrySettleOrder(Outcome(session, "perfect", 1000));
            session.TrySettleOrder(Outcome(session, "zero", 1000,
                new[] { new OrderViolation("zero-percent-rule", 0) }));
            session.TrySettleOrder(Outcome(session, "change", 1000, cashLoss: 1));
            session.TrySettleOrder(Outcome(session, "time", 1000, timePenalty: 1));
            Assert.That(session.CompletedOrders, Is.EqualTo(4));
            Assert.That(session.AccurateOrders, Is.EqualTo(1));
            Assert.That(session.AccuracyPercent, Is.EqualTo(25));
        }

        [Test]
        public void TimePenaltySettlesMoneyFirst_ThenDeferredFinishRejectsFurtherActions()
        {
            var session = new GameSession();
            session.StartChapter();
            session.AdvanceTime(178);
            Assert.That(session.TrySettleOrder(Outcome(session, "last", 20000, timePenalty: 5)), Is.True);
            Assert.That(session.RemainingSeconds, Is.Zero);
            Assert.That(session.State, Is.EqualTo(SessionState.Playing));
            Assert.That(session.CanAcceptActions, Is.False);
            Assert.That(session.LastResult, Is.Null);
            Assert.That(session.TrySettleOrder(Outcome(session, "too-late", 1000)), Is.False);
            session.SetPaused(true);
            session.BeginLoading();
            session.AdvanceTime(0);
            Assert.That(session.State, Is.EqualTo(SessionState.Result));
            Assert.That(session.LastResult.NetRevenue, Is.EqualTo(20000));
            Assert.That(session.LastResult.CompletedOrders, Is.EqualTo(1));
        }

        [Test]
        public void OldRunAndOldChapterCallbacksCannotSettleNewChapter()
        {
            var session = new GameSession();
            var oldRun = Outcome(session, "id", 50000);
            session.ResetRun();
            session.StartChapter();
            Assert.That(session.TrySettleOrder(oldRun), Is.False);
            var oldChapter = Outcome(session, "late-ch1", 50000);
            session.TrySettleOrder(Outcome(session, "id", 20000));
            session.AdvanceTime(180);
            session.ContinueAfterResult();
            session.PrepareNextChapter();
            session.StartChapter();
            Assert.That(session.TrySettleOrder(oldChapter), Is.False);
            Assert.That(session.TrySettleOrder(Outcome(session, "id", 30000)), Is.True);
            Assert.That(session.CompletedOrders, Is.EqualTo(1));
        }

        [TestCase(19999, false)]
        [TestCase(20000, true)]
        [TestCase(20001, true)]
        public void RevenueTargetUsesChapterNet_NotInitialWallet(long earned, bool expected)
        {
            var session = new GameSession(1000000);
            session.StartChapter();
            session.TrySettleOrder(Outcome(session, "boundary", earned));
            session.AdvanceTime(180);
            Assert.That(session.LastResult.Passed, Is.EqualTo(expected));
            Assert.That(session.LastResult.NetRevenue, Is.EqualTo(earned));
        }

        [Test]
        public void ShopSpendingDoesNotChangeResult_AndPurchasesPersistNextChapter()
        {
            var session = new GameSession();
            Assert.That(session.TryPurchase(new ShopOffer("translator", 3000)), Is.False);
            PassAndOpenShop(session);
            var result = session.LastResult;
            session.AdvanceTime(500);
            Assert.That(session.State, Is.EqualTo(SessionState.Shop));
            Assert.That(session.TryPurchase(new ShopOffer("translator", 3000)), Is.True);
            Assert.That(session.Wallet, Is.EqualTo(17000));
            Assert.That(session.ChapterRevenue, Is.EqualTo(20000));
            Assert.That(result.NetRevenue, Is.EqualTo(20000));
            Assert.That(result.WalletAtFinish, Is.EqualTo(20000));
            Assert.That(session.TryPurchase(new ShopOffer("translator", 3000)), Is.False);
            Assert.That(session.TryPurchase(new ShopOffer("grill", 17001)), Is.False);
            Assert.That(session.GetPurchaseLevel("grill"), Is.Zero);
            Assert.That(session.TryPurchase(new ShopOffer("grill", 17000)), Is.True);
            Assert.That(session.Wallet, Is.Zero);
            Assert.That(session.PrepareNextChapter(), Is.True);
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.ChapterNumber, Is.EqualTo(2));
            Assert.That(session.ChapterRevenue, Is.Zero);
            Assert.That(session.RemainingSeconds, Is.EqualTo(180));
            Assert.That(session.GetPurchaseLevel("translator"), Is.EqualTo(1));
            Assert.That(result.Orders.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpgradeLevelsRespectOfferMaximum()
        {
            var session = new GameSession();
            PassAndOpenShop(session);
            var offer = new ShopOffer("grill", 1000, 2);
            Assert.That(session.TryPurchase(offer), Is.True);
            Assert.That(session.TryPurchase(offer), Is.True);
            Assert.That(session.TryPurchase(offer), Is.False);
            Assert.That(session.GetPurchaseLevel("grill"), Is.EqualTo(2));
            Assert.That(session.Wallet, Is.EqualTo(18000));
        }

        [Test]
        public void ResetRestoresInitialWalletChapterTimeAndAllPurchasedState()
        {
            var session = new GameSession(250);
            PassAndOpenShop(session);
            session.TryPurchase(new ShopOffer("memo", 1000));
            session.PrepareNextChapter();
            session.StartChapter();
            session.TrySettleOrder(Outcome(session, "second", 1000));
            session.SetPaused(true);
            session.BeginLoading();
            int oldRunId = session.RunId;
            session.ResetRun();
            Assert.That(session.RunId, Is.Not.EqualTo(oldRunId));
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.ChapterNumber, Is.EqualTo(1));
            Assert.That(session.RemainingSeconds, Is.EqualTo(180));
            Assert.That(session.Wallet, Is.EqualTo(250));
            Assert.That(session.ChapterRevenue, Is.Zero);
            Assert.That(session.CompletedOrders, Is.Zero);
            Assert.That(session.AccurateOrders, Is.Zero);
            Assert.That(session.IsPaused, Is.False);
            Assert.That(session.LoadingDepth, Is.Zero);
            Assert.That(session.LastResult, Is.Null);
            Assert.That(session.GetPurchaseLevel("memo"), Is.Zero);
        }

        [Test]
        public void FiveChaptersUseDocumentTargets_FinalRequiresExactly80PercentOrBetter()
        {
            var session = new GameSession();
            long[] targets = { 20000, 30000, 45000, 60000, 80000 };
            for (int i = 0; i < 4; i++)
            {
                Assert.That(session.CurrentRules.RevenueTarget, Is.EqualTo(targets[i]));
                PassAndOpenShop(session);
                session.PrepareNextChapter();
            }
            Assert.That(session.CurrentRules.RevenueTarget, Is.EqualTo(80000));
            session.StartChapter();
            for (int i = 0; i < 5; i++)
            {
                var violations = i == 4 ? new[] { new OrderViolation("missed", 0) } : null;
                session.TrySettleOrder(Outcome(session, i.ToString(), 16000, violations));
            }
            session.AdvanceTime(180);
            Assert.That(session.LastResult.AccuracyPercent, Is.EqualTo(80));
            Assert.That(session.LastResult.NetRevenue, Is.EqualTo(80000));
            Assert.That(session.LastResult.Passed, Is.True);
            session.ContinueAfterResult();
            Assert.That(session.State, Is.EqualTo(SessionState.Ending));
            Assert.That(session.PrepareNextChapter(), Is.False);
        }

        [Test]
        public void AccuracyThatWouldDisplayAs80AfterRoundingStillFails()
        {
            var session = new GameSession(rules: new[] { new ChapterRules(1, 180, 80000, 80) });
            session.StartChapter();
            for (int i = 0; i < 99; i++)
            {
                var violations = i >= 79 ? new[] { new OrderViolation("missed", 0) } : null;
                session.TrySettleOrder(Outcome(session, i.ToString(), 1000, violations));
            }
            session.AdvanceTime(180);
            Assert.That(Math.Round(session.LastResult.AccuracyPercent), Is.EqualTo(80));
            Assert.That(session.LastResult.Passed, Is.False);
        }

        [Test]
        public void OverflowIsRejectedAtomically_AndOrderIdRemainsAvailable()
        {
            var session = new GameSession(long.MaxValue);
            session.StartChapter();
            Assert.That(session.TrySettleOrder(Outcome(session, "overflow", 1)), Is.False);
            Assert.That(session.CompletedOrders, Is.Zero);
            Assert.That(session.ChapterRevenue, Is.Zero);
            Assert.That(session.Wallet, Is.EqualTo(long.MaxValue));
            Assert.That(session.TrySettleOrder(Outcome(session, "overflow", 0)), Is.True);
        }

        [Test]
        public void InvalidInputsAreRejectedBeforeStateChanges()
        {
            var session = new GameSession();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.AdvanceTime(double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OrderViolation("rule", 101));
            Assert.Throws<ArgumentException>(() => new OrderViolation(" ", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => Outcome(session, "negative", -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Outcome(session, "cash", 1, cashLoss: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => Outcome(session, "time", 1, timePenalty: double.NaN));
            Assert.Throws<ArgumentException>(() => Outcome(session, "null-rule", 1, new OrderViolation[] { null }));
            Assert.Throws<ArgumentException>(() => new GameSession(rules: new[] { new ChapterRules(2, 180, 1) }));
            Assert.That(session.TrySettleOrder(null), Is.False);
            Assert.That(session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(session.RemainingSeconds, Is.EqualTo(180));
        }
    }
}
