using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SpeechUIAnimator : MonoBehaviour
{
    [Header("References")]
    public CanvasGroup group;
    public TextMeshProUGUI textField;
    public Image background;
    public RectTransform micIcon;

    [Header("Colors")]
    public Color normalColor = new Color(0f, 0f, 0f, 0.8f);
    public Color processingColor = new Color(0f, 0.4f, 1f, 0.8f);
    public Color successColor = new Color(0f, 0.8f, 0f, 0.8f);
    public Color errorColor = new Color(0.8f, 0f, 0f, 0.8f);

    [Header("Animation")]
    public float fadeSpeed = 3f;
    public float visibleTime = 4f;
    public float pulseSpeed = 2f;
    public float scalePop = 1.1f;

    private bool isVisible;
    private Coroutine activeRoutine;

    private void Reset()
    {
        group = GetComponent<CanvasGroup>();
    }

    private void Awake()
    {
        if (group == null) group = GetComponent<CanvasGroup>();
    }

    private void Start()
    {
        if (group != null) group.alpha = 0f;
        if (background != null) background.color = normalColor;
        transform.localScale = Vector3.one;
        isVisible = false;
    }

    private void Update()
    {
        // Mic pulse doar când UI-ul e vizibil
        if (isVisible && micIcon != null)
        {
            float scale = 1f + Mathf.Sin(Time.unscaledTime * pulseSpeed) * 0.1f;
            micIcon.localScale = new Vector3(scale, scale, scale);
        }
        else if (micIcon != null)
        {
            micIcon.localScale = Vector3.one;
        }
    }

    public void ShowProcessing(string message)
    {
        SetBackground(processingColor);
        ShowUI(message);
    }

    public void ShowSuccess(string message)
    {
        SetBackground(successColor);
        ShowUI(message);
    }

    public void ShowError(string message)
    {
        SetBackground(errorColor);
        ShowUI(message);
    }

    public void ShowUI(string message)
    {
        if (textField != null) textField.text = message;

        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        activeRoutine = StartCoroutine(FadeInAndOut());
    }

    private void SetBackground(Color c)
    {
        if (background != null) background.color = c;
    }

    private IEnumerator FadeInAndOut()
    {
        if (group == null)
        {
            // Fallback: dacă nu există CanvasGroup, măcar păstrăm textul setat.
            yield break;
        }

        isVisible = true;

        // POP la apariție
        transform.localScale = Vector3.one * Mathf.Max(0.01f, scalePop);

        // Fade IN
        while (group.alpha < 1f)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 1f, Time.unscaledDeltaTime * fadeSpeed);
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.unscaledDeltaTime * fadeSpeed);
            yield return null;
        }

        // Stă vizibil
        float t = 0f;
        while (t < visibleTime)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Fade OUT
        while (group.alpha > 0f)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, 0f, Time.unscaledDeltaTime * fadeSpeed);
            yield return null;
        }

        isVisible = false;
        SetBackground(normalColor);
        transform.localScale = Vector3.one;
        activeRoutine = null;
    }

    // Opțional: dacă vrei să forțezi ascunderea imediată
    public void HideNow()
    {
        if (activeRoutine != null)
        {
            StopCoroutine(activeRoutine);
            activeRoutine = null;
        }

        isVisible = false;

        if (group != null) group.alpha = 0f;
        SetBackground(normalColor);
        transform.localScale = Vector3.one;

        if (micIcon != null) micIcon.localScale = Vector3.one;
    }
}