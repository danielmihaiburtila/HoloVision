using TMPro;
using UnityEngine;

public class TextResultPanelUI : MonoBehaviour
{
    [Header("Texts")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI resultText;

    private string lastText = "";

    //private void Awake()
    //{
    //    gameObject.SetActive(false);
    //}

    public void ShowResult(string text)
    {
        lastText = string.IsNullOrWhiteSpace(text)
            ? "Nu am putut citi text clar."
            : text.Trim();

        gameObject.SetActive(true);

        if (titleText != null)
            titleText.text = "TEXT CITIT";

        if (resultText != null)
            resultText.text = lastText;
    }

    public string GetLastText()
    {
        return lastText;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}