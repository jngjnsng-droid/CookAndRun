using UnityEngine;

public class ClickCursor : MonoBehaviour
{
    public static ClickCursor Instance { get; private set; }

    [Header("클릭했을 때 바뀔 커서 이미지")]
    public Texture2D clickCursor;

    [Header("손가락 끝 클릭 지점 (X, Y)")]
    public Vector2 clickHotSpot = new Vector2(16, 2);

    private void Awake()
    {
        // 이미 다른 씬에서 넘어온 ClickCursor가 있다면 중복 생성을 방지하고 자기 자신을 삭제합니다.
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 자기 자신을 저장하고, 씬이 전환되어도 이 오브젝트를 파괴하지 않도록 설정합니다.
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        // 마우스 왼쪽 버튼을 눌렀을 때
        if (Input.GetMouseButtonDown(0))
        {
            if (clickCursor != null)
            {
                Cursor.SetCursor(clickCursor, clickHotSpot, CursorMode.Auto);
            }
        }

        // 마우스 왼쪽 버튼을 뗐을 때 (에디터 기본 커서로 복구)
        if (Input.GetMouseButtonUp(0))
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}