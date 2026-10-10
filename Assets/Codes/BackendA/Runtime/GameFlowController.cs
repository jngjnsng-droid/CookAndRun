using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

namespace CookAndRun.Progression
{
    public enum OrderPhase { AwaitingOrder, Cooking, Delivery, CustomerReaction }

    // B의 Update에서 확정된 배달 결과를 먼저 받은 후 이 LateUpdate에서 시간을 마감한다.
    [DefaultExecutionOrder(10000)]
    public sealed class GameFlowController : MonoBehaviour
    {
        public static GameFlowController Instance { get; private set; }
        public GameSession Session { get; private set; }
        public ProgressionSettings Settings { get; private set; }
        public OrderPhase Phase { get; private set; }
        public string ActiveOrderId { get; private set; }
        public bool HasRun { get; private set; }
        public bool IsSwitchingScene { get; private set; }
        public bool CanAcceptActions => HasRun && Session.CanAcceptActions && !IsSwitchingScene;

        public event Action Changed;
        public event Action RunReset;
        public event Action ChapterStarted;
        public event Action<ChapterResult> ChapterEnded;
        public event Action OrderSettled;
        public event Action<string> ItemPurchased;

        private int skipClockThroughFrame = -1;
        private bool ownsSettings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearStatic() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            EnsureExists();
        }

        public static GameFlowController EnsureExists()
        {
            if (Instance != null) return Instance;
            return new GameObject("GameFlow (Backend A)").AddComponent<GameFlowController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Settings = Resources.Load<ProgressionSettings>("ProgressionSettings");
            if (Settings == null)
            {
                Settings = ScriptableObject.CreateInstance<ProgressionSettings>();
                ownsSettings = true;
            }
            Session = new GameSession(Settings.initialWallet, Settings.allowNegativeWallet, Settings.BuildRules());
            SceneManager.sceneLoaded += OnSceneLoaded;
            gameObject.AddComponent<ProgressionHud>();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Time.timeScale = 1f;
            Instance = null;
            if (ownsSettings) Destroy(Settings);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // Kitchen을 에디터에서 직접 실행해도 동일한 CH 1 초기화 경로를 사용한다.
            if (scene.name == "Kitchen" && !IsSwitchingScene && !HasRun)
            {
                // Awake에서 등록된 페이드 로딩 토큰을 ResetRun으로 지우지 않는다.
                HasRun = true;
                Phase = OrderPhase.AwaitingOrder;
                ActiveOrderId = null;
                RunReset?.Invoke();
                Publish();
                StartCoroutine(StartWhenReady());
            }
            else if (scene.name == "MainMenuScene" && !IsSwitchingScene)
            {
                HasRun = false;
                Session.ResetRun();
                Phase = OrderPhase.AwaitingOrder;
                ActiveOrderId = null;
                RunReset?.Invoke();
                Publish();
            }
        }

        private void Update()
        {
            if (HasRun && Session.State == SessionState.Playing && !IsSwitchingScene &&
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetPaused(!Session.IsPaused);
        }

        private void LateUpdate()
        {
            if (!HasRun) return;
            bool wasPlaying = Session.State == SessionState.Playing;
            // 재개/로딩 완료 프레임의 delta에 정지 구간이 섞이는 것을 막는다.
            double delta = Time.frameCount <= skipClockThroughFrame ? 0d : Time.unscaledDeltaTime;
            Session.AdvanceTime(delta);
            if (wasPlaying && Session.State == SessionState.Result)
            {
                ActiveOrderId = null; // 미완료 주문은 정산하지 않는다.
                Phase = OrderPhase.AwaitingOrder;
                ApplyTimeScale();
                ChapterEnded?.Invoke(Session.LastResult); // B는 조리/입력/진행 중 주문을 정리한다.
            }
            Publish();
        }

        private void ApplyTimeScale()
        {
            // 조리에서 Time.deltaTime/WaitForSeconds를 사용하면 일시정지와 함께 멈춘다.
            Time.timeScale = !HasRun || (Session.State == SessionState.Playing &&
                !Session.IsPaused && Session.LoadingDepth == 0 && !IsSwitchingScene) ? 1f : 0f;
        }

        private void Publish() { ApplyTimeScale(); Changed?.Invoke(); }

        private void ResetRun()
        {
            Session.ResetRun();
            HasRun = true;
            ActiveOrderId = null;
            Phase = OrderPhase.AwaitingOrder;
            skipClockThroughFrame = Time.frameCount;
            RunReset?.Invoke();
            Publish();
        }

        public void StartNewGame()
        {
            if (IsSwitchingScene) return;
            if (!CanLoad("Kitchen")) return;
            ResetRun();
            StartCoroutine(LoadSceneRoutine("Kitchen", true));
        }

        public bool StartPreparedChapter()
        {
            if (!HasRun || IsSwitchingScene || Session.LoadingDepth > 0 || !Session.StartChapter()) return false;
            Phase = OrderPhase.AwaitingOrder;
            ActiveOrderId = null;
            skipClockThroughFrame = Time.frameCount;
            Publish();
            ChapterStarted?.Invoke();
            return true;
        }

        private IEnumerator StartWhenReady()
        {
            int runId = Session.RunId;
            // sceneLoaded는 다른 오브젝트의 Start보다 먼저 발생할 수 있다.
            yield return null;
            while (Session.RunId == runId && Session.LoadingDepth > 0) yield return null;
            if (!HasRun || Session.RunId != runId || IsSwitchingScene) yield break;
            StartPreparedChapter();
        }

        public void SetPaused(bool paused)
        {
            if (!HasRun || Session.State != SessionState.Playing) return;
            Session.SetPaused(paused);
            skipClockThroughFrame = Time.frameCount;
            Publish();
        }

        // 페이드/추가 로딩도 BeginLoading과 EndLoading을 한 쌍으로 호출한다.
        public int BeginLoading() { Session.BeginLoading(); Publish(); return Session.RunId; }
        public void EndLoading(int runId)
        {
            if (Session.RunId == runId) EndLoading();
        }
        public void EndLoading()
        {
            Session.EndLoading();
            skipClockThroughFrame = Time.frameCount;
            Publish();
        }

        public bool TryBeginOrder(string orderId)
        {
            if (!CanAcceptActions || Phase != OrderPhase.AwaitingOrder || string.IsNullOrWhiteSpace(orderId)) return false;
            foreach (var settled in Session.SettledOrders)
                if (settled.Outcome.OrderId == orderId) return false;
            ActiveOrderId = orderId;
            Phase = OrderPhase.Cooking;
            Publish();
            return true;
        }

        public bool TryBeginDelivery()
        {
            if (!CanAcceptActions || Phase != OrderPhase.Cooking) return false;
            Phase = OrderPhase.Delivery;
            Publish();
            return true;
        }

        public bool TryCompleteOrder(OrderOutcome outcome)
        {
            if (!CanAcceptActions || Phase != OrderPhase.Delivery || outcome == null ||
                outcome.OrderId != ActiveOrderId || !Session.TrySettleOrder(outcome)) return false;
            ActiveOrderId = null;
            Phase = OrderPhase.CustomerReaction;
            OrderSettled?.Invoke();
            Publish();
            return true;
        }

        public bool TryFinishCustomerReaction()
        {
            if (!CanAcceptActions || Phase != OrderPhase.CustomerReaction) return false;
            Phase = OrderPhase.AwaitingOrder;
            Publish();
            return true;
        }

        public bool ContinueFromResult()
        {
            if (!Session.ContinueAfterResult()) return false;
            Publish();
            return true;
        }

        public bool StartNextChapter()
        {
            if (IsSwitchingScene || Session.LoadingDepth > 0 || !CanLoad("Kitchen") || !Session.PrepareNextChapter()) return false;
            ActiveOrderId = null;
            Phase = OrderPhase.AwaitingOrder;
            StartCoroutine(LoadSceneRoutine("Kitchen", true));
            return true;
        }

        public bool TryBuy(string itemId)
        {
            if (IsSwitchingScene || Session.State != SessionState.Shop) return false;
            var items = Settings.shopItems;
            if (items == null) return false;
            foreach (var item in items)
            {
                if (item == null || item.id != itemId) continue;
                if (string.IsNullOrWhiteSpace(item.id) || item.price < 0 || item.maxLevel < 1) return false;
                if (!Session.TryPurchase(new ShopOffer(item.id, item.price, item.maxLevel))) return false;
                ItemPurchased?.Invoke(item.id);
                Publish();
                return true;
            }
            return false;
        }

        public bool LoadGameplayScene(string sceneName)
        {
            if (!CanAcceptActions || !CanLoad(sceneName)) return false;
            StartCoroutine(LoadSceneRoutine(sceneName, false));
            return true;
        }

        public void ReturnToMenu()
        {
            if (IsSwitchingScene || !CanLoad("MainMenuScene")) return;
            Session.ResetRun();
            HasRun = false;
            ActiveOrderId = null;
            Phase = OrderPhase.AwaitingOrder;
            RunReset?.Invoke();
            StartCoroutine(LoadSceneRoutine("MainMenuScene", false));
        }

        private static bool CanLoad(string sceneName)
        {
            if (!string.IsNullOrWhiteSpace(sceneName) && Application.CanStreamedLevelBeLoaded(sceneName)) return true;
            Debug.LogError($"장면 '{sceneName}'을 열 수 없습니다. Build Profiles의 Scene List에 등록하세요.");
            return false;
        }

        private IEnumerator LoadSceneRoutine(string sceneName, bool startChapter)
        {
            IsSwitchingScene = true;
            BeginLoading();
            try
            {
                var operation = SceneManager.LoadSceneAsync(sceneName);
                while (operation != null && !operation.isDone) yield return null;
                yield return null;
            }
            finally
            {
                IsSwitchingScene = false;
                EndLoading();
            }
            if (startChapter) yield return StartWhenReady();
        }
    }
}
