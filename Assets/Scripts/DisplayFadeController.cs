using UnityEngine;
using TMPro;

public class DisplayFadeController : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public float visibleDuration = 2f;
    public float fadeSpeed = 2f;

    private float timer = 0f;
    private bool isVisible = false;

    void Start()
    {
        canvasGroup.alpha = 0f; // începe invizibil
    }

    void Update()
    {
        if (isVisible)
        {
            timer -= Time.deltaTime;
            if (timer <= 0)
            {
                FadeOut();
            }
        }
    }

    public void Show()
    {
        timer = visibleDuration;
        isVisible = true;
        StopAllCoroutines();
        StartCoroutine(FadeInRoutine());
    }

    private void FadeOut()
    {
        isVisible = false;
        StopAllCoroutines();
        StartCoroutine(FadeOutRoutine());
    }

    private System.Collections.IEnumerator FadeInRoutine()
    {
        while (canvasGroup.alpha < 1)
        {
            canvasGroup.alpha += Time.deltaTime * fadeSpeed;
            yield return null;
        }
    }

    private System.Collections.IEnumerator FadeOutRoutine()
    {
        while (canvasGroup.alpha > 0)
        {
            canvasGroup.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }
    }
}
