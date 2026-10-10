using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace CookAndRun.Progression.Tests
{
    /// <summary>
    /// Run only in a disposable validation clone with the real Kitchen/MainMenuScene
    /// in Build Settings. These tests load scenes and never save project assets.
    /// They use the core clock to move near a deadline; the Unity LateUpdate owns
    /// the actual finish and ChapterEnded notification.
    /// </summary>
    public sealed class GameFlowPlayModeTests
    {
        private GameFlowController flow;
        private readonly List<Action> unsubscribe = new List<Action>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.CanStreamedLevelBeLoaded("Kitchen"), Is.True,
                "Validation clone must contain Kitchen in Build Settings.");
            Assert.That(Application.CanStreamedLevelBeLoaded("MainMenuScene"), Is.True,
                "Validation clone must contain MainMenuScene in Build Settings.");
            flow = GameFlowController.EnsureExists();
            yield return WaitUntil(() => !flow.IsSwitchingScene, "previous scene load");
            flow.StartNewGame();
            yield return WaitForPlaying();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Kitchen"));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var remove in unsubscribe) remove();
            unsubscribe.Clear();
            if (flow != null)
            {
                yield return WaitUntil(() => !flow.IsSwitchingScene, "teardown scene load");
                flow.ReturnToMenu();
                yield return WaitUntil(() => !flow.IsSwitchingScene && flow.Session.LoadingDepth == 0,
                    "teardown menu/fade");
                UnityEngine.Object.Destroy(flow.gameObject);
                yield return null;
            }
            Time.timeScale = 1f;
        }

        [UnityTest]
        public IEnumerator NewGame_StartsOnceAfterSceneIsReady_ThenCountsDown()
        {
            int started = 0;
            Action onStarted = () => started++;
            flow.ChapterStarted += onStarted;
            unsubscribe.Add(() => flow.ChapterStarted -= onStarted);
            int previousRun = flow.Session.RunId;

            flow.StartNewGame();
            flow.StartNewGame(); // Repeated clicks during a load must not create a second run.
            Assert.That(flow.IsSwitchingScene, Is.True);
            Assert.That(flow.Session.State, Is.EqualTo(SessionState.Ready));
            Assert.That(flow.CanAcceptActions, Is.False);
            Assert.That(flow.Session.RemainingSeconds, Is.EqualTo(180).Within(0.001));
            Assert.That(Time.timeScale, Is.Zero);

            yield return WaitForPlaying();
            Assert.That(started, Is.EqualTo(1));
            Assert.That(flow.Session.RunId, Is.Not.EqualTo(previousRun));
            Assert.That(flow.Session.ChapterNumber, Is.EqualTo(1));
            Assert.That(flow.CanAcceptActions, Is.True);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            double before = flow.Session.RemainingSeconds;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.LessThan(before));
        }

        [UnityTest]
        public IEnumerator PauseAndNestedLoading_StayFrozenUntilEveryReasonIsCleared()
        {
            flow.SetPaused(true);
            double frozen = flow.Session.RemainingSeconds;
            flow.BeginLoading();
            flow.BeginLoading();
            flow.EndLoading();
            Assert.That(flow.Session.LoadingDepth, Is.EqualTo(1));
            Assert.That(flow.CanAcceptActions, Is.False);
            Assert.That(flow.TryBeginOrder("paused-order"), Is.False);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.EqualTo(frozen));
            Assert.That(Time.timeScale, Is.Zero);

            flow.EndLoading();
            Assert.That(flow.Session.IsPaused, Is.True, "Finishing a load must not clear manual pause.");
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.EqualTo(frozen));
            Assert.That(flow.CanAcceptActions, Is.False);

            flow.SetPaused(false);
            Assert.That(flow.CanAcceptActions, Is.True);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.LessThan(frozen));

            flow.BeginLoading();
            flow.BeginLoading();
            frozen = flow.Session.RemainingSeconds;
            flow.EndLoading();
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.EqualTo(frozen));
            flow.EndLoading();
            Assert.That(flow.CanAcceptActions, Is.True);
        }

        [UnityTest]
        public IEnumerator BackendBContract_EnforcesPhases_AndReactionKeepsClockRunning()
        {
            int settled = 0;
            Action onSettled = () => settled++;
            flow.OrderSettled += onSettled;
            unsubscribe.Add(() => flow.OrderSettled -= onSettled);
            var outcome = Outcome("order-1", 10000);

            Assert.That(flow.TryBeginDelivery(), Is.False);
            Assert.That(flow.TryBeginOrder("order-1"), Is.True);
            Assert.That(flow.Phase, Is.EqualTo(OrderPhase.Cooking));
            Assert.That(flow.TryBeginOrder("order-2"), Is.False);
            Assert.That(flow.TryCompleteOrder(outcome), Is.False, "Cooking is not a completed delivery.");
            Assert.That(flow.TryBeginDelivery(), Is.True);
            Assert.That(flow.TryCompleteOrder(Outcome("wrong-id", 10000)), Is.False);

            flow.SetPaused(true);
            Assert.That(flow.TryCompleteOrder(outcome), Is.False);
            flow.SetPaused(false);
            Assert.That(flow.TryCompleteOrder(outcome), Is.True);
            Assert.That(flow.TryCompleteOrder(outcome), Is.False);
            Assert.That(settled, Is.EqualTo(1));
            Assert.That(flow.Session.CompletedOrders, Is.EqualTo(1));
            Assert.That(flow.Phase, Is.EqualTo(OrderPhase.CustomerReaction));
            Assert.That(flow.TryBeginOrder("order-2"), Is.False);
            double beforeReaction = flow.Session.RemainingSeconds;
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.LessThan(beforeReaction));
            Assert.That(flow.TryFinishCustomerReaction(), Is.True);
            Assert.That(flow.TryBeginOrder("order-1"), Is.False, "A completed order ID cannot start another delivery.");
            Assert.That(flow.TryBeginOrder("order-2"), Is.True);
        }

        [UnityTest]
        public IEnumerator PreviousRunLoadingCompletion_CannotReleaseNewRunLoading()
        {
            int oldLoading = flow.BeginLoading();
            flow.StartNewGame();
            Assert.That(flow.IsSwitchingScene, Is.True);
            int depth = flow.Session.LoadingDepth;
            flow.EndLoading(oldLoading);
            Assert.That(flow.Session.LoadingDepth, Is.EqualTo(depth));
            Assert.That(flow.CanAcceptActions, Is.False);
            yield return WaitForPlaying();
            Assert.That(flow.Session.LoadingDepth, Is.Zero);
        }

        [UnityTest]
        public IEnumerator CompletionInDeadlineFrame_IsIncluded_AndEndEventFiresOnlyOnce()
        {
            int ended = 0;
            ChapterResult observed = null;
            Action<ChapterResult> onEnded = result => { ended++; observed = result; };
            flow.ChapterEnded += onEnded;
            unsubscribe.Add(() => flow.ChapterEnded -= onEnded);
            Assert.That(flow.TryBeginOrder("last-order"), Is.True);
            Assert.That(flow.TryBeginDelivery(), Is.True);
            MoveJustBeforeDeadline();
            Assert.That(flow.TryCompleteOrder(Outcome("last-order", 20000)), Is.True);

            yield return WaitForResult();
            Assert.That(ended, Is.EqualTo(1));
            Assert.That(observed, Is.SameAs(flow.Session.LastResult));
            Assert.That(observed.CompletedOrders, Is.EqualTo(1));
            Assert.That(observed.NetRevenue, Is.EqualTo(20000));
            Assert.That(observed.AccurateOrders, Is.EqualTo(1));
            Assert.That(observed.Passed, Is.True);
            Assert.That(flow.CanAcceptActions, Is.False);
            Assert.That(flow.ActiveOrderId, Is.Null);
            Assert.That(Time.timeScale, Is.Zero);
            Assert.That(flow.TryBeginOrder("too-late"), Is.False);
            Assert.That(flow.TryCompleteOrder(Outcome("last-order", 20000)), Is.False);
            yield return null;
            yield return null;
            Assert.That(ended, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnfinishedDeliveryAtDeadline_IsDiscarded_AndCannotSettleLater()
        {
            Assert.That(flow.TryBeginOrder("unfinished"), Is.True);
            Assert.That(flow.TryBeginDelivery(), Is.True);
            var lateOutcome = Outcome("unfinished", 50000);
            MoveJustBeforeDeadline();
            yield return WaitForResult();

            Assert.That(flow.Session.LastResult.CompletedOrders, Is.Zero);
            Assert.That(flow.Session.LastResult.NetRevenue, Is.Zero);
            Assert.That(flow.Session.LastResult.Passed, Is.False);
            Assert.That(flow.ActiveOrderId, Is.Null);
            Assert.That(flow.TryCompleteOrder(lateOutcome), Is.False);
            Assert.That(flow.ContinueFromResult(), Is.True);
            Assert.That(flow.Session.State, Is.EqualTo(SessionState.Ending));
        }

        [UnityTest]
        public IEnumerator GameplaySceneReload_PreservesSession_OrderAndMoney_WithoutLoadingTimeLoss()
        {
            CompleteOrder("paid", 10000);
            Assert.That(flow.TryFinishCustomerReaction(), Is.True);
            Assert.That(flow.TryBeginOrder("still-cooking"), Is.True);
            GameSession session = flow.Session;
            int run = session.RunId;
            long wallet = session.Wallet;
            double before = session.RemainingSeconds;

            Assert.That(flow.LoadGameplayScene("Kitchen"), Is.True);
            Assert.That(flow.IsSwitchingScene, Is.True);
            Assert.That(flow.CanAcceptActions, Is.False);
            yield return WaitForPlaying();
            Assert.That(GameFlowController.Instance, Is.SameAs(flow));
            Assert.That(flow.Session, Is.SameAs(session));
            Assert.That(session.RunId, Is.EqualTo(run));
            Assert.That(session.Wallet, Is.EqualTo(wallet));
            Assert.That(session.CompletedOrders, Is.EqualTo(1));
            Assert.That(flow.ActiveOrderId, Is.EqualTo("still-cooking"));
            Assert.That(flow.Phase, Is.EqualTo(OrderPhase.Cooking));
            Assert.That(session.RemainingSeconds, Is.EqualTo(before).Within(0.05),
                "Scene loading and the scene fade must not spend chapter time.");
        }

        [UnityTest]
        public IEnumerator ShopAndNextChapter_PreservePurchases_ButFailureRestartClearsTheRun()
        {
            var previousOffers = flow.Settings.shopItems;
            flow.Settings.shopItems = new[]
            {
                new ShopItemDefinition { id = "test-translator", displayName = "Test translator", price = 3000, maxLevel = 1 }
            };
            unsubscribe.Add(() => flow.Settings.shopItems = previousOffers);
            int originalRun = flow.Session.RunId;
            var oldOutcome = Outcome("reused-id", 20000);
            CompleteOrder("reused-id", 20000);
            MoveJustBeforeDeadline();
            yield return WaitForResult();
            ChapterResult result = flow.Session.LastResult;
            Assert.That(flow.ContinueFromResult(), Is.True);
            Assert.That(flow.Session.State, Is.EqualTo(SessionState.Shop));
            Assert.That(flow.CanAcceptActions, Is.False);
            long beforePurchase = flow.Session.Wallet;
            Assert.That(flow.TryBuy("test-translator"), Is.True);
            Assert.That(flow.TryBuy("test-translator"), Is.False);
            Assert.That(flow.Session.Wallet, Is.EqualTo(beforePurchase - 3000));
            Assert.That(result.NetRevenue, Is.EqualTo(20000));
            Assert.That(result.Passed, Is.True);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(flow.Session.RemainingSeconds, Is.Zero);

            Assert.That(flow.StartNextChapter(), Is.True);
            yield return WaitForPlaying();
            Assert.That(flow.Session.ChapterNumber, Is.EqualTo(2));
            Assert.That(flow.Session.RunId, Is.EqualTo(originalRun));
            Assert.That(flow.Session.GetPurchaseLevel("test-translator"), Is.EqualTo(1));
            Assert.That(flow.Session.Wallet, Is.EqualTo(beforePurchase - 3000));
            Assert.That(flow.Session.CompletedOrders, Is.Zero);
            Assert.That(flow.Session.ChapterRevenue, Is.Zero);

            MoveJustBeforeDeadline();
            yield return WaitForResult();
            Assert.That(flow.ContinueFromResult(), Is.True);
            Assert.That(flow.Session.State, Is.EqualTo(SessionState.Ending));
            flow.StartNewGame();
            yield return WaitForPlaying();
            Assert.That(flow.Session.RunId, Is.Not.EqualTo(originalRun));
            Assert.That(flow.Session.ChapterNumber, Is.EqualTo(1));
            Assert.That(flow.Session.Wallet, Is.EqualTo(flow.Settings.initialWallet));
            Assert.That(flow.Session.GetPurchaseLevel("test-translator"), Is.Zero);
            Assert.That(flow.Session.CompletedOrders, Is.Zero);
            Assert.That(flow.Session.ChapterRevenue, Is.Zero);
            Assert.That(flow.TryBeginOrder("reused-id"), Is.True);
            Assert.That(flow.TryBeginDelivery(), Is.True);
            Assert.That(flow.TryCompleteOrder(oldOutcome), Is.False,
                "A callback from a previous run must be rejected even if the order ID is reused.");
        }

        private OrderOutcome Outcome(string id, long reward)
        {
            return new OrderOutcome(flow.Session.RunId, flow.Session.ChapterNumber, id, reward);
        }

        private void CompleteOrder(string id, long reward)
        {
            Assert.That(flow.TryBeginOrder(id), Is.True);
            Assert.That(flow.TryBeginDelivery(), Is.True);
            Assert.That(flow.TryCompleteOrder(Outcome(id, reward)), Is.True);
        }

        private void MoveJustBeforeDeadline()
        {
            Assert.That(flow.Session.RemainingSeconds, Is.GreaterThan(0.000001));
            flow.Session.AdvanceTime(flow.Session.RemainingSeconds - 0.000001);
            Assert.That(flow.Session.State, Is.EqualTo(SessionState.Playing));
        }

        private IEnumerator WaitForPlaying()
        {
            return WaitUntil(() => !flow.IsSwitchingScene && flow.Session.LoadingDepth == 0 &&
                flow.Session.State == SessionState.Playing, "Kitchen ready and chapter playing");
        }

        private IEnumerator WaitForResult()
        {
            return WaitUntil(() => flow.Session.State == SessionState.Result, "LateUpdate chapter finish");
        }

        private static IEnumerator WaitUntil(Func<bool> predicate, string description)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (!predicate())
            {
                Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline),
                    "Timed out waiting for " + description);
                yield return null;
            }
        }
    }
}
