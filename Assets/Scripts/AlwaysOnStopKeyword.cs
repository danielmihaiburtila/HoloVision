using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Windows.Speech;

[DisallowMultipleComponent]
public class AlwaysOnStopKeyword : MonoBehaviour
{
    public static AlwaysOnStopKeyword Instance { get; private set; }

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
    private bool recognizerStarted = false;

    private void Awake()
    {
        // singleton defensiv: nu permitem două instanțe active
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[AlwaysOnStopKeyword] Duplicate instance detected. Destroying this one.");
            Destroy(this);
            return;
        }

        Instance = this;

        if (ocrSystem == null) ocrSystem = FindObjectOfType<AzureReadOCR>(true);
        if (scanHandler == null) scanHandler = FindObjectOfType<ScanCommandHandler>(true);
        if (azureTTS == null) azureTTS = FindObjectOfType<AzureTTS>(true);
        if (speechUI == null) speechUI = FindObjectOfType<SpeechUIAnimator>(true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

#if UNITY_EDITOR
        StopRecognizer();
#endif
    }

    // Pe device NU pornim recognizerul paralel.
    private void OnEnable()
    {
#if UNITY_EDITOR
        StartRecognizer();
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        StopRecognizer();
#endif
    }

    private void StartRecognizer()
    {
        if (recognizerStarted) return;
        if (recognizer != null) return;

        var unique = new HashSet<string>();
        if (stopKeywords != null)
        {
            foreach (var kw in stopKeywords)
            {
                if (string.IsNullOrWhiteSpace(kw)) continue;
                unique.Add(kw.Trim().ToLowerInvariant());
            }
        }

        if (unique.Count == 0)
        {
            Debug.LogWarning("[AlwaysOnStopKeyword] No valid stop keywords configured.");
            return;
        }

        try
        {
            recognizer = new KeywordRecognizer(new List<string>(unique).ToArray(), confidence);
            recognizer.OnPhraseRecognized += OnPhraseRecognized;
            recognizer.Start();
            recognizerStarted = true;

            Debug.Log("[AlwaysOnStopKeyword] KeywordRecognizer started (EDITOR ONLY).");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[AlwaysOnStopKeyword] Failed to start KeywordRecognizer: " + ex.Message);
            StopRecognizer();
        }
    }

    private void StopRecognizer()
    {
        recognizerStarted = false;

        if (recognizer == null) return;

        try
        {
            recognizer.OnPhraseRecognized -= OnPhraseRecognized;

            if (recognizer.IsRunning)
                recognizer.Stop();

            recognizer.Dispose();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[AlwaysOnStopKeyword] StopRecognizer exception: " + ex.Message);
        }

        recognizer = null;
    }

    private void OnPhraseRecognized(PhraseRecognizedEventArgs args)
    {
        Debug.Log("[AlwaysOnStopKeyword] STOP keyword: " + args.text);
        StopAllNow("Am oprit.");
    }

    /// <summary>
    /// API public - poate fi chemat și din gest, și din alte sisteme.
    /// Oprește OCR + Scan + TTS și arată mesaj în UI.
    /// </summary>
    public void StopAllNow(string uiText = "Am oprit.")
    {
        ocrSystem?.StopReading();
        scanHandler?.StopScanFromRouter();
        azureTTS?.StopNow();

        if (speechUI != null)
            speechUI.ShowSuccess(uiText);

        Debug.Log("[AlwaysOnStopKeyword] StopAllNow() executed.");
    }
}