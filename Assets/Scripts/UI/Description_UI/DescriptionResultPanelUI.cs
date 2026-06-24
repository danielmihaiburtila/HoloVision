using TMPro;
using UnityEngine;

public class DescriptionResultPanelUI : MonoBehaviour
{
    [Header("Texts")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI timeText;

    private string lastDescription = "";

    public void ShowResult(string description)
    {
        lastDescription = string.IsNullOrWhiteSpace(description)
            ? "Nu am putut genera o descriere clară a mediului."
            : description.Trim();

        gameObject.SetActive(true);

        if (titleText != null)
            titleText.text = "DESCRIERE MEDIU";

        if (resultText != null)
            resultText.text = lastDescription;

        if (statusText != null)
            statusText.text = "Rezultat generat cu succes.";

        if (timeText != null)
            timeText.text = System.DateTime.Now.ToString("HH:mm");
    }

    public string GetLastDescription()
    {
        return lastDescription;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}