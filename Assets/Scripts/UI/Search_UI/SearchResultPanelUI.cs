using TMPro;
using UnityEngine;

public class SearchResultPanelUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject panelRoot;

    [Header("Texts")]
    public TextMeshProUGUI objectText;
    public TextMeshProUGUI guideText;
    public TextMeshProUGUI arrowText;

    private void Awake()
    {
        Hide();
    }

    public void ShowSearchStarted(string objectName)
    {
        if (panelRoot != null)
            panelRoot.SetActive(true);

        if (objectText != null)
            objectText.text = "Obiect căutat: " + objectName;

        SetGuide("Aștept indicații...", "•");
    }

    public void SetGuide(string message, string arrow)
    {
        if (guideText != null)
            guideText.text = message;

        if (arrowText != null)
            arrowText.text = arrow;
    }

    public void ShowLeft()
    {
        SetGuide("Îndreaptă-te ușor spre stânga", "←");
    }

    public void ShowRight()
    {
        SetGuide("Îndreaptă-te ușor spre dreapta", "→");
    }

    public void ShowForward()
    {
        SetGuide("Mergi înainte cu grijă", "↑");
    }

    public void ShowFound()
    {
        SetGuide("Obiectul este în fața ta", "✓");
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }
}