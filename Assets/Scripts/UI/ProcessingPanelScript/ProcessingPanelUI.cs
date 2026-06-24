using TMPro;
using UnityEngine;

public class ProcessingPanelUI : MonoBehaviour
{
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI messageText;

    public void Show(string featureName)
    {
        gameObject.SetActive(true);

        if (titleText != null)
            titleText.text = "PROCESARE";

        if (messageText != null)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                messageText.text = "Comanda a fost recepționată. Procesez informația.";
            else
                messageText.text = "Procesez: " + featureName;
        }
    }
}