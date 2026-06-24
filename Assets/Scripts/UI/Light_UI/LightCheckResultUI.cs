using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LightCheckResultUI : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI resultText;
    public TextMeshProUGUI instructionText;
    public Image resultImage;

    [Header("Sprites")]
    public Sprite roomLightSprite;
    public Sprite roomDarkSprite;
    public Sprite bulbOnSprite;
    public Sprite bulbOffSprite;

    public void ShowRoomLightResult(bool isBright)
    {
        if (titleText != null)
            titleText.text = "VERIFICARE LUMINÃ";

        if (resultText != null)
            resultText.text = isBright ? "LUMINOASÃ" : "ÎNTUNERIC";

        if (instructionText != null)
            instructionText.text = isBright
                ? "Camera pare luminatã."
                : "Camera pare întunecatã.";

        if (resultImage != null)
            resultImage.sprite = isBright ? roomLightSprite : roomDarkSprite;
    }

    public void ShowBulbResult(bool isOn)
    {
        if (titleText != null)
            titleText.text = "VERIFICARE BEC";

        if (resultText != null)
            resultText.text = isOn ? "BEC APRINS" : "BEC STINS";

        if (instructionText != null)
            instructionText.text = isOn
                ? "Becul pare aprins."
                : "Becul pare stins.";

        if (resultImage != null)
            resultImage.sprite = isOn ? bulbOnSprite : bulbOffSprite;
    }
}