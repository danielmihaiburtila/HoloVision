using UnityEngine;
using UnityEngine.Windows.Speech;
using System.Text;

public class WindowsDictationInput : MonoBehaviour
{
    public VoiceCommandRouter router;
    public bool autoStart = true;

    private DictationRecognizer dictation;
    private StringBuilder buffer = new StringBuilder();
    private bool running;

    void Start()
    {
        if (router == null)
            router = FindObjectOfType<VoiceCommandRouter>(true);

        dictation = new DictationRecognizer(ConfidenceLevel.Low);

        dictation.DictationResult += OnResult;
        dictation.DictationComplete += OnComplete;
        dictation.DictationError += OnError;

        if (autoStart)
            StartDictation();
    }

    public void StartDictation()
    {
        if (running) return;
        dictation.Start();
        running = true;
        Debug.Log("DICTATION STARTED");
    }

    public void StopDictation()
    {
        if (!running) return;
        dictation.Stop();
        running = false;
        Debug.Log("DICTATION STOPPED");
    }

    private void OnResult(string text, ConfidenceLevel confidence)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        Debug.Log("Heard: " + text);

        // trimitem textul direct în routerul tău existent
        router?.AcceptCommand(text, text);
    }

    private void OnComplete(DictationCompletionCause cause)
    {
        running = false;

        // HL2 oprește automat dictation după pauză → restart
        if (cause != DictationCompletionCause.Complete)
        {
            Invoke(nameof(StartDictation), 0.5f);
        }
    }

    private void OnError(string error, int hresult)
    {
        Debug.LogError("Dictation error: " + error);
        running = false;
        Invoke(nameof(StartDictation), 1f);
    }

    private void OnDestroy()
    {
        if (dictation != null)
        {
            dictation.Stop();
            dictation.Dispose();
        }
    }
}