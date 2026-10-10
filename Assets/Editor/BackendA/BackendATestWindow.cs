#if UNITY_EDITOR
using System;
using CookAndRun.Progression;
using UnityEditor;
using UnityEngine;
using SessionState = CookAndRun.Progression.SessionState;

// 실제 게임에는 포함되지 않는 A 담당 검증 도구. B의 주문 결과를 대신 전달한다.
public sealed class BackendATestWindow : EditorWindow
{
    private string lastAction = "";
    private Font koreanFont;
    private GUIStyle labelStyle;
    private GUIStyle buttonStyle;
    private GUIStyle noticeStyle;

    private void EnsureStyles()
    {
        if (koreanFont != null && labelStyle != null) return;
        koreanFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 14);
        labelStyle = new GUIStyle(GUI.skin.label)
        {
            font = koreanFont, fontSize = 13, wordWrap = true
        };
        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            font = koreanFont, fontSize = 13, fixedHeight = 28
        };
        noticeStyle = new GUIStyle(GUI.skin.box)
        {
            font = koreanFont, fontSize = 13, wordWrap = true,
            alignment = TextAnchor.UpperLeft, padding = new RectOffset(8, 8, 8, 8)
        };
    }

    private void OnDisable()
    {
        if (koreanFont != null) DestroyImmediate(koreanFont);
        koreanFont = null;
        labelStyle = null;
        buttonStyle = null;
        noticeStyle = null;
    }

    [MenuItem("CookAndRun/Backend A/Test Panel")]
    public static void Open() { GetWindow<BackendATestWindow>("Backend A Test"); }

    [MenuItem("CookAndRun/Backend A/Create Settings Asset")]
    public static void CreateSettings()
    {
        const string path = "Assets/Resources/ProgressionSettings.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ProgressionSettings>(path);
        if (existing != null) { Selection.activeObject = existing; return; }
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        var asset = CreateInstance<ProgressionSettings>();
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        Selection.activeObject = asset;
    }

    private void OnInspectorUpdate() { Repaint(); }

    private void OnGUI()
    {
        EnsureStyles();
        GUILayout.Label("Play 모드에서 사용하는 테스트 전용 도구입니다. 실제 주문·조리 구현을 대신하지 않습니다.", noticeStyle);
        var flow = GameFlowController.Instance;
        if (!EditorApplication.isPlaying || flow == null)
        {
            GUILayout.Label("MainMenuScene 또는 Kitchen을 열고 Play를 누르세요.", labelStyle);
            return;
        }
        var s = flow.Session;
        GUILayout.Label($"CH {s.ChapterNumber} / {s.State} / {flow.Phase}", labelStyle);
        GUILayout.Label($"시간 {s.RemainingSeconds:0.00}s / 수익 {s.ChapterRevenue:N0} / 보유금 {s.Wallet:N0}", labelStyle);
        GUILayout.Label($"완료 {s.CompletedOrders} / 정확 {s.AccurateOrders} / {s.AccuracyPercent:0.00}%", labelStyle);
        GUI.enabled = !flow.IsSwitchingScene;
        if (GUILayout.Button("새 게임 (CH 1 초기화)", buttonStyle)) flow.StartNewGame();
        GUI.enabled = flow.CanAcceptActions && flow.Phase == OrderPhase.AwaitingOrder;
        if (GUILayout.Button("정확한 배달 +10,000원", buttonStyle)) CompleteSample(flow, false);
        if (GUILayout.Button("벨 50% + 레시피 70% → +3,000원", buttonStyle)) CompleteSample(flow, true);
        GUI.enabled = flow.CanAcceptActions;
        if (GUILayout.Button("남은 시간을 1초로 (종료 테스트)", buttonStyle))
            s.AdvanceTime(Math.Max(0, s.RemainingSeconds - 1));
        GUI.enabled = s.State == SessionState.Playing && !flow.IsSwitchingScene;
        if (GUILayout.Button(s.IsPaused ? "일시정지 해제" : "일시정지", buttonStyle)) flow.SetPaused(!s.IsPaused);
        if (GUILayout.Button("로딩 시작 (중첩 가능)", buttonStyle)) flow.BeginLoading();
        if (GUILayout.Button("로딩 완료 (한 단계)", buttonStyle)) flow.EndLoading();
        GUI.enabled = true;
        GUILayout.Label($"로딩 깊이 {s.LoadingDepth}", labelStyle);
        GUILayout.Label(lastAction.Length == 0 ? "테스트 배달은 이 창에서만 만들 수 있습니다." : lastAction, noticeStyle);
    }

    private void CompleteSample(GameFlowController flow, bool violations)
    {
        string id = "test-" + Guid.NewGuid().ToString("N");
        if (!flow.TryBeginOrder(id) || !flow.TryBeginDelivery()) return;
        var s = flow.Session;
        var outcome = new OrderOutcome(s.RunId, s.ChapterNumber, id, 10000,
            violations ? new[] { new OrderViolation("NO_BELL", 50), new OrderViolation("RECIPE", 70) } : null);
        bool accepted = flow.TryCompleteOrder(outcome);
        flow.TryFinishCustomerReaction();
        lastAction = accepted ? "테스트 배달 1건을 정산했습니다." : "배달 결과가 거부됐습니다.";
    }
}
#endif
