using TMPro;
using UnityEngine;

public class HoloVisionVisualStatusUI : MonoBehaviour
{
    [Header("Onboarding UI")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI subtitleText;
    public TextMeshProUGUI detectedNameText;
    public TextMeshProUGUI modeText;
    public TextMeshProUGUI lastCommandText;

    [Header("Main menu UI")]
    public TextMeshProUGUI mainMenuModeText;

    [Header("Optional")]
    public TMP_InputField nameInputField;

    private const string DefaultTitle = "Bine ai venit!";

    public void ShowWelcome(string msg)
    {
        if (titleText != null)
            titleText.text = "Bine ai venit!";

        if (subtitleText != null)
            subtitleText.text = string.IsNullOrWhiteSpace(msg) ? "" : msg;

        if (detectedNameText != null)
            detectedNameText.text = "Nume: —";

        if (modeText != null)
            modeText.text = "Mod selectat: —";

        if (lastCommandText != null)
            lastCommandText.text = "Ultima comandă: —";
    }

    public void ShowOnboarding(string msg)
    {
        if (titleText != null)
            titleText.text = DefaultTitle;

        if (subtitleText != null)
            subtitleText.text = msg;
    }

    public void ShowPersonalWelcome(string userName, string subtitle = null)
    {
        string safeName = string.IsNullOrWhiteSpace(userName) ? "" : userName.Trim();

        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(safeName)
                ? "Bine ai venit!"
                : $"Bine ai venit, {safeName}!";

        if (subtitleText != null)
            subtitleText.text = string.IsNullOrWhiteSpace(subtitle)
                ? ""
                : subtitle;

        if (detectedNameText != null)
            detectedNameText.text = "";

        if (nameInputField != null)
            nameInputField.text = safeName;
    }

    public void SetDetectedName(string name)
    {
        string safeName = string.IsNullOrWhiteSpace(name) ? "" : name.Trim();

        if (detectedNameText != null)
            detectedNameText.text = safeName;

        if (nameInputField != null)
            nameInputField.text = safeName;
    }

    public void SetMode(string mode)
    {
        string safeMode = string.IsNullOrWhiteSpace(mode) ? "—" : mode.Trim();

        if (modeText != null)
            modeText.text = "Mod selectat: " + safeMode;

        if (mainMenuModeText != null)
            mainMenuModeText.text = "Mod selectat: " + safeMode;
    }

    public void SetLastCommand(string cmd)
    {
        string safeCmd = string.IsNullOrWhiteSpace(cmd) ? "—" : cmd.Trim();

        if (lastCommandText != null)
            lastCommandText.text = "Ultima comandă: " + safeCmd;
    }

    public void ShowOnboardingWithName(string userName)
    {
        string safeName = string.IsNullOrWhiteSpace(userName) ? "" : userName.Trim();

        if (titleText != null)
            titleText.text = string.IsNullOrEmpty(safeName)
                ? "Bine ai venit!"
                : $"Bine ai venit, {safeName}!";

        if (subtitleText != null)
            subtitleText.text = "";

        if (detectedNameText != null)
            detectedNameText.text = "";

        if (nameInputField != null)
            nameInputField.text = safeName;
    }
    public void ShowWelcomeOnly()
    {
        if (titleText != null)
            titleText.text = "Bine ai venit!";

        if (subtitleText != null)
            subtitleText.text = "";

        if (detectedNameText != null)
            detectedNameText.text = "";

        if (modeText != null)
            modeText.text = "Mod selectat: —";

        if (lastCommandText != null)
            lastCommandText.text = "Ultima comandă: —";

        if (nameInputField != null)
            nameInputField.text = "";
    }
}