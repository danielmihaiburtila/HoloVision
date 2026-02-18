using UnityEngine;

/// <summary>
/// Wrapper corect pentru AzureTTS cu coadă:
/// - Speak() -> forward la AzureTTS
/// - StopNow() -> oprește COADA + request + audio (prin AzureTTS.StopNow)
/// - IsSpeaking() -> folosește AzureTTS.IsSpeaking (include request în zbor)
/// </summary>
public class AzureTTSInterruptible : MonoBehaviour
{
    public AzureTTS tts;

    private void Awake()
    {
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
    }

    public void Speak(string text)
    {
        if (tts == null) return;
        tts.Speak(text);
    }

    public void StopNow()
    {
        if (tts == null) return;
        tts.StopNow(); // IMPORTANT: oprește și coada și request-ul
    }

    public bool IsSpeaking()
    {
        if (tts == null) return false;
        return tts.IsSpeaking(); // IMPORTANT: include și requestInFlight
    }
}