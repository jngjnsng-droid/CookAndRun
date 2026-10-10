using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using CookAndRun.Progression;

public class MainMenuManager : MonoBehaviour
{
    [Header("페이드 연출 설정")]
    public Image fadeImage;          // 1단계에서 만든 FadePanel의 Image
    public float fadeDuration = 2f;  // 어두워지는 데 걸리는 시간(초)
    private bool isStarting;

    // [게임시작] 버튼을 눌렀을 때 실행되는 함수
    public void GameStart()
    {
        if (isStarting) return;
        isStarting = true;
        // 씬을 바로 전환하지 않고 페이드 아웃 코루틴을 먼저 실행합니다.
        StartCoroutine(FadeOutAndLoadScene("Kitchen"));
    }

    // 화면이 서서히 어두워진 후 씬을 전환하는 코루틴
    private IEnumerator FadeOutAndLoadScene(string sceneName)
    {
        float timer = 0f;
        Color color = fadeImage != null ? fadeImage.color : Color.black;
        if (fadeImage != null) fadeImage.gameObject.SetActive(true);

        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;
            // Alpha(A) 값을 0(투명)에서 1(불투명 검은색)로 올립니다.
            color.a = Mathf.Lerp(0f, 1f, timer / fadeDuration);
            if (fadeImage != null) fadeImage.color = color;
            yield return null; // 다음 프레임까지 대기
        }

        // 완전히 검은색이 되면 씬을 변경합니다.
        GameFlowController.EnsureExists().StartNewGame();
        isStarting = false;
    }

    // 기타 버튼 함수들...
    public void GameQuit()
    {
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
        #else
            Application.Quit();
        #endif
    }

    public void GameHelp()
    {
        if (isStarting) return;
        SceneManager.LoadScene("Help");
    }

    public void GameSetting()
    {
        if (isStarting) return;
        SceneManager.LoadScene("Setting");
    }
}
