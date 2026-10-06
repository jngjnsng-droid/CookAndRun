using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class SceneFadeIn : MonoBehaviour
{
    public Image fadeImage;          // 어두운 화면을 담당할 Panel의 Image
    public float fadeDuration = 1f;  // 밝아지는 데 걸리는 시간(초)

    private void Start()
    {
        // Inspector에서 연결하지 않았다면 이 오브젝트의 Image를 가져옵니다.
        if (fadeImage == null)
            fadeImage = GetComponent<Image>();

        // 씬이 시작되자마자 서서히 밝아지는 코루틴 실행
        StartCoroutine(FadeInRoutine());
    }

    private IEnumerator FadeInRoutine()
    {
        float timer = 0f;
        Color color = fadeImage.color;

        while (timer < fadeDuration)
        {
            timer += Time.deltaTime;
            // 알파(A) 값을 1(검은색)에서 0(투명)으로 줄입니다.
            color.a = Mathf.Lerp(1f, 0f, timer / fadeDuration);
            fadeImage.color = color;
            yield return null; // 다음 프레임까지 대기
        }

        // 완전히 투명해지면 마우스 클릭 등을 방해하지 않도록 패널을 비활성화합니다.
        fadeImage.gameObject.SetActive(false);
    }
}