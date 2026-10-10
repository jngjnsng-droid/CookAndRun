# 백엔드 A 진행 시스템 연결 안내

## 이번 구현 범위

- CH 1~5의 180초 영업, 정산, 상점 준비, 실패/성공 결과, CH 1 재시작.
- 현재 보유금과 해당 챕터의 정산 수익을 분리한다.
- 목표 수익: 20,000 / 30,000 / 45,000 / 60,000 / 80,000원. CH 5는 정확도 80% 이상도 필요하다.
- 초기 보유금 0원, 음수 잔액 허용(2026-10-10 사용자 확정).
- 완료 주문은 최대 감액률 하나만 적용하고 현금 차액을 별도로 차감한다. 원 미만 지급액은 버린다.
- 중복 결과 및 지난 회차/챕터의 결과를 거부한다.
- 일시정지와 로딩 중 시간 정지. 조립/배달/NPC 반응 중 시간 진행.
- 상점에서는 보유금만 차감. 구매는 다음 챕터에 유지되고 새 회차에서는 초기화된다.

실제 주문 생성·조리·배달 판정은 B가 구현한다. 튜토리얼 실습, 최종 아트/UI, 챕터별 엔딩 컷씬·크레딧, 번역·메모·조리 장비 효과는 아직 연결할 작업이다. 이번 기본 화면은 A의 진행을 확인하기 위한 교체 가능한 화면이다.

## Unity에서 바로 확인

1. `Assets/Scenes/MainMenuScene.unity`를 열고 Play → 게임 시작.
2. Kitchen 로딩/페이드 완료 후 CH 1의 03:00 타이머가 시작된다. Kitchen을 직접 Play해도 동일하다.
3. Esc 또는 일시정지 버튼으로 정지/재개한다.
4. Unity 상단 **CookAndRun → Backend A → Test Panel**을 연다.
5. `정확한 배달 +10,000원`을 두 번 누르고 `남은 시간을 1초로`를 누른다.
6. CH 1 수익 20,000원/통과 확인 → 상점 → 다음 챕터. CH 2 수익은 0원, 보유금은 20,000원으로 유지되어야 한다.
7. CH 2에서 아무 배달 없이 종료 → 실패 → 새로 시작. CH 1/180초/보유금 0원으로 돌아온다.

테스트 버튼은 Editor 폴더에 있어서 배포 게임에 포함되지 않는다. 실제 게임에는 가짜 주문이나 자동 보상이 없다. 따라서 B의 주문 연결 전에는 일반 플레이만으로 수익이 생기지 않는다.

## 파일 구성

| 위치 | 역할 |
| --- | --- |
| `Assets/Codes/BackendA/Core/GameSession.cs` | Unity와 독립적인 챕터·시간·재화·구매 규칙 |
| `Core/OrderOutcome.cs` | B가 전달하는 불변 배달 결과, 감액 계산/정산 영수증 |
| `Core/ChapterRules.cs`, `ChapterResult.cs` | 챕터 조건과 변경되지 않는 종료 결과 |
| `Runtime/GameFlowController.cs` | Unity 수명 주기, 장면 전환, 이벤트, 시간 마감 |
| `Runtime/ProgressionSettings.cs` | 챕터 및 상점 설정 |
| `Runtime/ProgressionHud.cs` | 기본 시간·정산·상점·엔딩 화면 |
| `Assets/Editor/BackendA/BackendATestWindow.cs` | A 담당 로컬 테스트 창 |
| `Assets/Tests/BackendA` | EditMode 규칙 테스트 및 PlayMode 연결 테스트 |

기존 MainMenuManager의 시작 버튼은 유지된다. 내부 실행 경로만 GameFlow로 연결했다. SceneFadeIn은 로딩 시간을 등록하고 unscaledDeltaTime으로 페이드한다. PlayerMovement는 진행이 중지된 동안 입력을 받지 않는다. 기존 장면 YAML과 이미지 배치는 수정하지 않는다.

## B 담당 연결 예시

```csharp
using CookAndRun.Progression;

// 주문 생성 시 저장할 값. 완료 시점에 새 토큰을 읽어 넣으면 안 된다.
var flow = GameFlowController.Instance;
int runId = flow.Session.RunId;
int chapter = flow.Session.ChapterNumber;
string orderId = "order-001"; // 같은 챕터 안에서 유일한 ID
if (!flow.TryBeginOrder(orderId)) return;

// 조리/포장 완료 시
if (!flow.TryBeginDelivery()) return;
// 배달 장면을 만들고 Build Profiles의 Scene List에 넣은 뒤:
// flow.LoadGameplayScene("Apartment");

// 음식 전달 + 필요한 결제까지 판정이 확정되는 순간
var outcome = new OrderOutcome(runId, chapter, orderId, 10000,
    new[] {
        new OrderViolation("NO_BELL", 50),
        new OrderViolation("RECIPE", 70)
    }, changeLoss: 0, timePenaltySeconds: 0);
bool accepted = flow.TryCompleteOrder(outcome); // 3,000원, 정확하지 않은 주문 1건
// accepted가 false이면 연출/추가 보상 등을 진행하지 않는다.

// 결과 확정 이후 NPC 반응을 재생한다. 반응 중에도 시간은 흐른다.
// 반응이 끝나면 다음 주문을 받을 상태로 전환한다.
flow.TryFinishCustomerReaction();
```

예시는 시점별 호출을 한 곳에 모아 설명한 것으로 그대로 한 Update에서 실행하는 코드가 아니다. 실수 없는 주문은 violations를 생략한다. 감액률 0%라도 요청 위반이라면 OrderViolation을 반드시 넣는다. 환불은 해당 위반의 감액률 100%로 전달한다. 현금 차액은 플레이어가 부담할 음수가 아닌 금액이다. 미완료 주문에는 TryCompleteOrder를 호출하지 않는다.

### 종료 및 시간 계약

- B는 판정이 확정된 프레임의 Update/일반 입력 콜백에서 TryCompleteOrder를 호출한다.
- A는 실행 순서 10000의 LateUpdate에서 시간을 한 번 차감하고, 0초면 즉시 Result로 전환한다.
- 같은 갱신 프레임에 확정된 배달을 먼저 반영한다(SFR-022). 다음 프레임까지 이어서 완성하는 유예는 없다.
- B가 10000 이후 LateUpdate, WaitForEndOfFrame 또는 다음 프레임에 완료 처리를 미루면 종료 후 결과로 거부된다.
- ChapterEnded 이벤트에서 진행 중 주문·조리·포장 입력/코루틴을 정리한다. 미완료 주문의 보상·완료 건수는 추가하지 않는다.
- RunReset 이벤트에서 B의 주문·음식·포장 상태를 전부 초기화한다.
- 입력 처리 전 flow.CanAcceptActions를 검사한다. timeScale=0만으로 Update나 클릭 콜백까지 중지되지는 않는다.
- 조리 시간은 Time.deltaTime 또는 동일한 일시정지 상태를 따른다. 실제 요리를 중지할 때 B가 스스로 코루틴과 작업 데이터를 정리해야 한다.
- 실제 장면 전환은 LoadGameplayScene을 이용한다. SceneManager.LoadScene을 직접 부르면 시작 전 로딩 시간을 A가 알 수 없다.
- 추가 비동기 준비 작업은 `int token = flow.BeginLoading()`으로 회차 토큰을 보관하고, finally에서 `flow.EndLoading(token)`을 호출한다. 이전 회차 작업의 늦은 완료가 새 회차 로딩을 해제하지 못하게 한다. NPC 반응은 로딩으로 표시하지 않는다.
- 결과/상점/엔딩에서는 다음 챕터 타이머를 시작하지 않는다. 다음 챕터 시작을 선택하고 Kitchen의 로딩이 끝난 후 시작한다.

### 이벤트 구독

OnEnable에서 ChapterStarted, ChapterEnded, RunReset 등을 구독하고 OnDisable에서 해제한다. 장면 로딩 후 구독한 경우에는 이벤트를 놓쳤을 수 있으므로 Session.State/Phase를 한 번 읽어서 화면과 현재 주문 상태를 동기화한다.

## 프론트 담당 연결

- 읽기: Session.RemainingSeconds, Wallet, ChapterRevenue, ChapterNumber, AccuracyPercent, LastResult.
- UI 갱신: Changed 이벤트를 구독하거나 UI의 Update에서 읽는다. 돈이나 시간을 UI에서 직접 변경하지 않는다.
- 버튼: SetPaused, ContinueFromResult, StartNextChapter, StartNewGame, ReturnToMenu, TryBuy(itemId).
- 실제 UI로 교체할 때 아래 설정에서 showBasicHud를 끈다.
- 일시정지/결과/상점 UI의 애니메이션은 unscaled time을 사용한다.

## 설정 및 미정 값

Unity 메뉴 **CookAndRun → Backend A → Create Settings Asset**에서 `Assets/Resources/ProgressionSettings.asset`을 만든다. 기존 파일이 있으면 선택만 한다. 기본 설정으로도 바로 실행되므로 파일 생성은 선택 사항이다. 설정을 바꾼 뒤 Play를 새로 시작한다.

- 챕터당 기본 180초, 목표 금액, 최종 정확도, 초기 보유금, 음수 잔액, 기본 HUD 표시를 조절할 수 있다.
- 실제 상점 가격은 문서에서 미정이므로 기본 상품 목록은 비워 뒀다. 테스트용 3,000원을 실제 가격으로 채택하지 않았다.
- 확정 후 shopItems에 고유 id, 표시 이름, 효과 설명, 가격, 최대 단계를 등록한다. 예: translator, memo, grill, fryer. 같은 id를 중복 등록하지 않는다.
- B/프론트는 Session.GetPurchaseLevel(id)와 ItemPurchased 이벤트로 소유 상태를 확인하고 번역/메모/조리 효과를 적용한다.
- 장비 효과 수치와 각 단계별 가격은 팀 합의 후 확장해야 한다. 현재 동일 상품은 설정된 단일 가격으로 maxLevel까지 구매하는 구조다.
- 미정 감액률·시간 차감은 B가 확정된 규칙으로 전달한다. A에서 임의 비율을 덧붙이지 않는다.
- Tutorial CH 0은 현재 씬에 없으므로 자동 생성하지 않았다. 완성 후 튜토리얼 종료 지점에서 StartNewGame을 호출해 본편과 연결할 수 있다.

## 테스트 및 Git 공유

Unity의 Window → General → Test Runner에서 EditMode/PlayMode 테스트를 실행한다. PlayMode 테스트는 장면을 전환하므로 저장할 작업을 마치고 실행한다. 현재 작업 검증은 별도 프로젝트 복사본에서 실행한다.

2026-10-10 Unity 6000.0.65f1 검증: EditMode 21개, PlayMode 8개 모두 통과. 실제 프로젝트 화면에서도 시작 → 일시정지/재개 → 테스트 배달 두 건 → 20,000원 통과 정산 → 상점 → CH 2를 확인했다. CH 2 진입 시 정산 수익은 0원, 보유금은 20,000원으로 유지된다.

이 폴더의 코드·테스트·문서와 Unity가 생성한 `.meta`를 함께 공유한다. 씬에 GameFlow를 수동으로 여러 개 붙일 필요는 없다. 게임 실행 시 한 개가 자동 생성되어 씬 전환에도 유지된다. 코드 추가 전부터 있던 ProjectSettings 수정 등은 A의 변경과 별도로 검토한다.

## 근거

- 사용자 제공 「쿡앤런_요구사항_명세서.pdf」v1.2: SFR-003, 013, 021~025, 027~030.
- 사용자 제공 「쿡앤런_최종_게임기획서.pdf」v2.0: 챕터/시간/정산/재시작 규칙.
- 2026-10-10 직접 요청: 즉시 종료, 정산 수익 판정, 일시정지·로딩 중 정지, 초기 0원·음수 기록.
- Unity 공식 문서: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/SceneManagement.SceneManager.LoadSceneAsync.html
- Unity 공식 문서: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/MonoBehaviour.LateUpdate.html
- Unity 공식 문서: https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Time-timeScale.html
