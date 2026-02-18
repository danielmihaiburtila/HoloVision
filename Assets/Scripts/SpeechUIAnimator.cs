using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class SpeechUIAnimator : MonoBehaviour
{
    public CanvasGroup group;
    public TextMeshProUGUI textField;
    public Image background;
    public RectTransform micIcon;

    [Header("Culori Interfață")]
    public Color normalColor = new Color(0, 0, 0, 0.8f);
    public Color processingColor = new Color(0, 0.4f, 1, 0.8f);
    public Color successColor = new Color(0, 0.8f, 0, 0.8f);
    public Color errorColor = new Color(0.8f, 0, 0, 0.8f);

    [Header("Setări Animație")]
    public float fadeSpeed = 3f;
    public float visibleTime = 4f;
    public float pulseSpeed = 2f;
    public float scalePop = 1.1f;

    bool isVisible = false;

    void Start()
    {
        if (group != null) group.alpha = 0;
        if (background != null) background.color = normalColor;
    }

    void Update()
    {
        // Iconița de microfon pulsează doar când UI-ul este vizibil
        if (isVisible && micIcon != null)
        {
            float scale = 1f + Mathf.Sin(Time.time * pulseSpeed) * 0.1f;
            micIcon.localScale = new Vector3(scale, scale, scale);
        }
    }

    public void ShowProcessing(string message)
    {
        if (background != null) background.color = processingColor;
        ShowUI(message);
    }

    public void ShowSuccess(string message)
    {
        if (background != null) background.color = successColor;
        ShowUI(message);
    }

    public void ShowError(string message)
    {
        if (background != null) background.color = errorColor;
        ShowUI(message);
    }

    public void ShowUI(string message)
    {
        if (textField != null) textField.text = message;
        StopAllCoroutines();
        StartCoroutine(FadeInAndOut());
    }

    IEnumerator FadeInAndOut()
    {
        isVisible = true;

        // Efect de POP (mărire ușoară la apariție)
        transform.localScale = Vector3.one * scalePop;

        // FADE IN
        while (group.alpha < 1)
        {
            group.alpha += Time.deltaTime * fadeSpeed;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.deltaTime * fadeSpeed);
            yield return null;
        }

        yield return new WaitForSeconds(visibleTime);

        // FADE OUT
        while (group.alpha > 0)
        {
            group.alpha -= Time.deltaTime * fadeSpeed;
            yield return null;
        }

        isVisible = false;
        if (background != null) background.color = normalColor;
    }
}