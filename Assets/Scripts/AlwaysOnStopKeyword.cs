using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Windows.Speech;

public class AlwaysOnStopKeyword : MonoBehaviour
{
    [Header("Targets (set in Inspector)")]
    public AzureReadOCR ocrSystem;
    public ScanCommandHandler scanHandler;
    public AzureTTS azureTTS;
    public SpeechUIAnimator speechUI;

    [Header("Keywords (RO)")]
    public string[] stopKeywords = new[] { "stop", "gata", "oprește", "opreste", "termină", "termina" };

    [Header("Recognizer")]
    public ConfidenceLevel confidence = ConfidenceLevel.Medium;

    private KeywordRecognizer recognizer;

    void Awake()
    {
        if (ocrSystem == null) ocrSystem = FindObjectOfType<AzureReadOCR>(true);
        if (scanHandler == null) scanHandler = FindObjectOfType<ScanCommandHandler>(true);
        if (azureTTS == null) azureTTS = FindObjectOfType<AzureTTS>(true);
        if (speechUI == null) speechUI = FindObjectOfType<SpeechUIAnimator>(true);
    }

    // 🚫 IMPORTANT: Pe HOLOLENS NU pornim recognizerul paralel
    void OnEnable()
    {
#if UNITY_EDITOR
        StartRecognizer();
#endif
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        StopRecognizer();
#endif
    }

    private void StartRecognizer()
    {
        if (recognizer != null) return;

        var unique = new HashSet<string>(stopKeywords);
        recognizer = new KeywordRecognizer(new List<string>(unique).ToArray(), confidence);
        recognizer.OnPhraseRecognized += OnPhraseRecognized;
        recognizer.Start();

        Debug.Log("[AlwaysOnStopKeyword] KeywordRecognizer started (EDITOR ONLY).");
    }

    private void StopRecognizer()
    {
        if (recognizer == null) return;

        try
        {
            recognizer.OnPhraseRecognized -= OnPhraseRecognized;
            if (recognizer.IsRunning) recognizer.Stop();
            recognizer.Dispose();
        }
        catch { }

        recognizer = null;
    }

    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        Debug.Log("[AlwaysOnStopKeyword] STOP keyword: " + args.text);

        ocrSystem?.StopReading();
        scanHandler?.StopScanFromRouter();
        azureTTS?.StopNow();

        if (speechUI != null) speechUI.ShowSuccess("Am oprit.");
    }
}