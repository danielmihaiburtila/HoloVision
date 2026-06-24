using UnityEngine;

public class SafetyTtsBridge : MonoBehaviour
{
    [Header("Optional reference to your existing TTS system")]
    public MonoBehaviour ttsSystem; // pune componenta ta reala TTS_System aici daca vrei

    [Header("Debug")]
    public bool logOnly = true;

    // Aceasta metoda apare in Inspector si poate primi string din UnityEvent (Dynamic string)
    public void SpeakFromSafety(string text)
    {
        if (logOnly)
        {
            Debug.Log("[SafetyTtsBridge] " + text);
            return;
        }

        // TODO: aici chemi API-ul real din TTS_System-ul tau, ex:
        // ((TTS_System)ttsSystem).Speak(text);
        // IMPORTANT: inlocuiesti linia de mai sus cu metoda ta reala.
        Debug.LogWarning("[SafetyTtsBridge] logOnly=false, dar nu ai implementat apelul catre TTS_System.");
    }
}