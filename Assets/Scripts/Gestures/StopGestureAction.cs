using UnityEngine;

public class StopGestureAction : MonoBehaviour
{
    [Header("References (drag in Inspector)")]
    public VoiceCommandRouter router;

    [Header("Fallback (dacă nu știm ce feature era)")]
    public string fallbackConfirmationText = "Am oprit.";

    [Header("Debug")]
    public bool debugLogs = true;

    private void Awake()
    {
        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);
    }

    public void HandleStopGesture()
    {
        if (debugLogs) Debug.Log("[StopGestureAction] HandleStopGesture()");

        if (router == null)
        {
            Debug.LogError("[StopGestureAction] VoiceCommandRouter nu a fost găsit în scenă.");
            return;
        }

      
        router.azureTTS?.StopNow();
        string text = fallbackConfirmationText;
        if (!string.IsNullOrWhiteSpace(router.ActiveFeatureName))
        {
            text = $"Am oprit {router.ActiveFeatureName}.";
        }
        router.StopAllFromGesture(text, speakConfirmation: true);
    }
}