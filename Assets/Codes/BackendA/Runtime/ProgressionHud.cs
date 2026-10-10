using System;
using UnityEngine;

namespace CookAndRun.Progression
{
    // 프론트의 정식 UI가 준비되기 전에도 전체 진행을 확인할 수 있는 기본 화면.
    // 정식 UI 연결 후 Settings.showBasicHud를 끄면 된다. 테스트 보상 버튼은 게임에 넣지 않는다.
    public sealed class ProgressionHud : MonoBehaviour
    {
        private GUIStyle title, body, button;
        private Font font;
        private Vector2 shopScroll;
        private string purchaseMessage = "";

        private void CreateStyles()
        {
            font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Arial" }, 20);
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true };
            body.normal.textColor = Color.white;
            title = new GUIStyle(body) { fontSize = 28, fontStyle = FontStyle.Bold };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 19, fixedHeight = 42 };
        }

        private void OnDestroy() { if (font != null) Destroy(font); }

        private void OnGUI()
        {
            var flow = GameFlowController.Instance;
            if (flow == null || !flow.HasRun || !flow.Settings.showBasicHud) return;
            if (body == null) CreateStyles();
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.TRS(new Vector3((Screen.width - 1280 * scale) / 2, (Screen.height - 720 * scale) / 2), Quaternion.identity, Vector3.one * scale);
            var session = flow.Session;
            if (session.State == SessionState.Playing)
            {
                DrawBackdrop(new Rect(16, 16, 500, 114));
                int time = (int)Math.Ceiling(session.RemainingSeconds);
                GUI.Label(new Rect(32, 25, 470, 38), $"CH {session.ChapterNumber}   {time / 60:00}:{time % 60:00}", title);
                GUI.Label(new Rect(32, 67, 470, 54), $"정산 수익 {session.ChapterRevenue:N0} / {session.CurrentRules.RevenueTarget:N0}원\n보유금 {session.Wallet:N0}원  ·  정확도 {session.AccuracyPercent:0.0}%", body);
                if (GUI.Button(new Rect(1120, 20, 140, 42), "일시정지", button)) flow.SetPaused(true);
            }

            if (flow.IsSwitchingScene || session.LoadingDepth > 0)
                DrawPanel("화면 준비 중", "로딩 중에는 영업 시간이 흐르지 않습니다.", null);
            else if (session.IsPaused)
                DrawPanel("일시정지", "영업 시간과 조리 시간이 멈춰 있습니다.", () =>
                {
                    if (GUILayout.Button("계속하기 (Esc)", button)) flow.SetPaused(false);
                    if (GUILayout.Button("메인 메뉴", button)) flow.ReturnToMenu();
                });
            else if (session.State == SessionState.Ready)
                DrawPanel($"CH {session.ChapterNumber} 영업 준비", $"제한 시간 {session.CurrentRules.DurationSeconds:0}초 · 목표 {session.CurrentRules.RevenueTarget:N0}원", () =>
                {
                    if (GUILayout.Button("영업 시작", button)) flow.StartPreparedChapter();
                });
            else if (session.State == SessionState.Result)
            {
                var r = session.LastResult;
                DrawPanel($"CH {r.ChapterNumber} 정산 · {(r.Passed ? "통과" : "목표 미달")}",
                    $"기본 보상  {r.BaseRewardTotal:N0}원\n감액  -{r.DiscountTotal:N0}원   /   현금 차액  -{r.ChangeLossTotal:N0}원\n\n정산 수익  {r.NetRevenue:N0}원   /   목표  {r.RevenueTarget:N0}원\n보유금  {r.WalletAtFinish:N0}원\n완료 {r.CompletedOrders}건 · 정확 {r.AccurateOrders}건 · 정확도 {r.AccuracyPercent:0.0}%" +
                    (r.MinimumAccuracyPercent > 0 ? $"\n추가 통과 조건: 정확도 {r.MinimumAccuracyPercent}% 이상" : ""), () =>
                    {
                        if (GUILayout.Button(r.Passed && r.ChapterNumber < 5 ? "상점으로" : "엔딩 확인", button)) flow.ContinueFromResult();
                    });
            }
            else if (session.State == SessionState.Shop)
                DrawPanel("다음 챕터 준비", $"보유금 {session.Wallet:N0}원 · 구매는 통과 판정용 정산 수익에 영향을 주지 않습니다.", () =>
                {
                    shopScroll = GUILayout.BeginScrollView(shopScroll, GUILayout.Height(220));
                    var items = flow.Settings.shopItems;
                    if (items == null || items.Length == 0)
                        GUILayout.Label("등록된 상품이 없습니다.\n팀에서 가격과 효과를 정한 후 상점 설정에 등록합니다.", body);
                    else foreach (var item in items)
                    {
                        if (item == null) continue;
                        int level = session.GetPurchaseLevel(item.id);
                        GUILayout.Label($"{item.displayName} · {item.price:N0}원 · {level}/{item.maxLevel}\n{item.effectDescription}", body);
                        GUI.enabled = level < item.maxLevel && session.Wallet >= item.price;
                        if (GUILayout.Button("구매: " + item.displayName, button))
                            purchaseMessage = flow.TryBuy(item.id) ? "구매했습니다." : "구매할 수 없습니다.";
                        GUI.enabled = true;
                    }
                    GUILayout.EndScrollView();
                    GUILayout.Label(purchaseMessage, body);
                    if (GUILayout.Button("다음 챕터 시작", button)) { purchaseMessage = ""; flow.StartNextChapter(); }
                });
            else if (session.State == SessionState.Ending)
                DrawPanel(session.LastResult.Passed ? "전국 500호점 달성!" : $"CH {session.ChapterNumber} 도전 종료",
                    session.LastResult.Passed ? "작은 가게에서 프랜차이즈 CEO까지!\n최종 챕터의 매출과 정확도 조건을 달성했습니다." : "이번 챕터의 통과 조건을 충족하지 못했습니다.\n다시 도전하면 재화와 구매 상태를 초기화하고 CH 1에서 시작합니다.", () =>
                    {
                        if (GUILayout.Button("CH 1부터 새로 시작", button)) flow.StartNewGame();
                        if (GUILayout.Button("메인 메뉴", button)) flow.ReturnToMenu();
                    });
            GUI.color = oldColor;
            GUI.matrix = oldMatrix;
        }

        private void DrawPanel(string heading, string message, Action actions)
        {
            DrawBackdrop(new Rect(300, 140, 680, 500));
            GUILayout.BeginArea(new Rect(330, 160, 620, 460));
            GUILayout.Label(heading, title);
            GUILayout.Space(18);
            GUILayout.Label(message, body);
            GUILayout.Space(20);
            actions?.Invoke();
            GUILayout.EndArea();
        }

        private static void DrawBackdrop(Rect area)
        {
            var previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.09f, 0.12f, 0.97f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }
    }
}
