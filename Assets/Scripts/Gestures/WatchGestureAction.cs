using System;
using System.Globalization;
using UnityEngine;

public class WatchGestureAction : MonoBehaviour
{
    [Header("References")]
    public VoiceCommandRouter router;

    [Header("Locale")]
    public string cultureTag = "ro-RO";

    [Header("Debug")]
    public bool debugLogs = true;

    private CultureInfo _ro;

    private void Awake()
    {
        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);

        try { _ro = new CultureInfo(cultureTag); }
        catch { _ro = CultureInfo.InvariantCulture; }
    }

    public void HandleWatchGesture()
    {
        if (debugLogs) Debug.Log("[WatchGestureAction] HandleWatchGesture()");

        if (router == null || router.azureTTS == null)
        {
            Debug.LogError("[WatchGestureAction] Router/AzureTTS lipsă. Setează AppRoot (Voice Command Router) în Inspector.");
            return;
        }

        router.azureTTS.StopNow();

        DateTime now = DateTime.Now;

        string dayName = now.ToString("dddd", _ro);
        string datePart = now.ToString("d MMMM yyyy", _ro);
        string timePart = now.ToString("HH:mm", _ro);

        string text = $"Este {dayName}, {datePart}. Este {timePart}.";
        router.azureTTS.Speak(text);
    }
}