using System.Globalization;
using System.Text;
using UnityEngine;

public class ScanCommandHandler : MonoBehaviour
{
    [Header("Deps")]
    public AzureTTS tts;
    public ObstacleDetectorMR detector;

    [Header("Always-on safety")]
    public bool startInPassiveMode = true;

    private void Awake()
    {
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        if (detector == null) detector = FindObjectOfType<ObstacleDetectorMR>(true);

        if (detector == null)
        {
            Debug.LogError("[ScanHandler] ObstacleDetectorMR not found in scene!");
            return;
        }

        // IMPORTANT:
        // Detector stays enabled always. We only switch mode.
        detector.enabled = true;

        if (startInPassiveMode)
        {
            detector.SetMode(ObstacleDetectorMR.Mode.Passive);
        }
    }

    /// <summary>
    /// "SIGURANTA" / "SCAN" -> Active mode (more feedback)
    /// </summary>
    public void StartSafetyActive()
    {
        if (detector == null) return;

        detector.SetMode(ObstacleDetectorMR.Mode.Active);

        // Text scurt, să nu devină enervant
        if (tts != null) tts.Speak("Siguranță activă.");
        Debug.Log("[ScanHandler] Mode = ACTIVE");
    }

    /// <summary>
    /// "LINISTE" / "STOP" -> Passive mode (quiet safety still on)
    /// </summary>
    public void StopSafetyActive()
    {
        if (detector == null) return;

        detector.SetMode(ObstacleDetectorMR.Mode.Passive);

        if (tts != null) tts.Speak("Liniște. Siguranța rămâne activă.");
        Debug.Log("[ScanHandler] Mode = PASSIVE");
    }

    // Compatibilitate cu apelurile tale existente
    public void StartScanFromRouter() => StartSafetyActive();
    public void StopScanFromRouter() => StopSafetyActive();

    // Fallback (dacă cineva încă trimite text direct aici)
    public void OnUserSpeech(string rawCommand)
    {
        string cmd = Normalize(rawCommand);

        if (cmd.Contains("siguranta") || cmd.Contains("siguranta activa") || cmd.Contains("scan") || cmd.Contains("detect"))
        {
            StartSafetyActive();
            return;
        }

        if (cmd.Contains("liniste") || cmd.Contains("stop") || cmd.Contains("gata") || cmd.Contains("opreste") || cmd.Contains("termin"))
        {
            StopSafetyActive();
            return;
        }
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        input = input.ToLowerInvariant().Trim();

        string normalized = input.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();

        foreach (char c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}