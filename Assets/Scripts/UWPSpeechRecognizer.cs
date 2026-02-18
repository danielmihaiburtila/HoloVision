using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
using Windows.Media.SpeechRecognition;
using Windows.Globalization;
using System.Threading.Tasks;
#endif

public class UWPSpeechRecognizer : MonoBehaviour
{
    [Header("UI (optional)")]
    public TextMeshProUGUI debugOutputText;

    [Header("Router")]
    public VoiceCommandRouter router;

    [Header("Language")]
    [Tooltip("Ex: ro-RO. Va fi folosit DOAR dacă există în SupportedGrammarLanguages; altfel fallback la SystemSpeechLanguage.")]
    public string languageTag = "ro-RO";

    [Header("Reliability")]
    public bool autoRestartSession = true;
    public bool disableTimeouts = true;
    public float initDelaySeconds = 0.25f;

    [Header("Constraints")]
    [Tooltip("Pentru 'cuvinte random' la CAUTĂ ai nevoie de dictation. Se activează DOAR dacă SystemSpeechLanguage e română.")]
    public bool enableDictation = true;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private SpeechRecognizer recognizer;

    private bool shouldRun = false;
    private bool initInProgress = false;
    private int runToken = 0;

    // STOP low confidence
    private DateTime lastStopUtc = DateTime.MinValue;
    private readonly TimeSpan stopRepeatWindow = TimeSpan.FromSeconds(1.2);
#endif

    private void Awake()
    {
        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);
    }

    private void OnEnable()
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        shouldRun = true;
        runToken++;
        CancelInvoke(nameof(BeginInit));
        Invoke(nameof(BeginInit), Mathf.Max(0.01f, initDelaySeconds));
#else
        SetUI("Speech: activ doar pe device (UWP).");
#endif
    }

    private void OnDisable()
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        shouldRun = false;
        runToken++;
        CancelInvoke(nameof(BeginInit));
        _ = StopRecognizerInternal();
#endif
    }

    private void OnApplicationPause(bool pause)
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        if (pause)
        {
            shouldRun = false;
            runToken++;
            CancelInvoke(nameof(BeginInit));
            _ = StopRecognizerInternal();
        }
        else
        {
            shouldRun = true;
            runToken++;
            CancelInvoke(nameof(BeginInit));
            Invoke(nameof(BeginInit), Mathf.Max(0.01f, initDelaySeconds));
        }
#endif
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private void BeginInit()
    {
        if (!shouldRun) return;
        InitRecognizer();
    }

    private Language PickBestLanguageForListConstraint()
    {
        try
        {
            var requested = new Language(languageTag);

            foreach (var lang in SpeechRecognizer.SupportedGrammarLanguages)
            {
                if (string.Equals(lang.LanguageTag, requested.LanguageTag, StringComparison.OrdinalIgnoreCase))
                    return requested;
            }

            var sys = SpeechRecognizer.SystemSpeechLanguage;
            if (sys != null)
            {
                foreach (var lang in SpeechRecognizer.SupportedGrammarLanguages)
                {
                    if (string.Equals(lang.LanguageTag, sys.LanguageTag, StringComparison.OrdinalIgnoreCase))
                        return sys;
                }
            }

            foreach (var lang in SpeechRecognizer.SupportedGrammarLanguages)
                return lang;

            return new Language("en-US");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[UWPSpeechRecognizer] PickBestLanguage exception: " + e);
            var sys = SpeechRecognizer.SystemSpeechLanguage;
            return sys ?? new Language("en-US");
        }
    }

    private bool IsTopicLanguageSupported(Language lang)
    {
        if (lang == null) return false;
        foreach (var t in SpeechRecognizer.SupportedTopicLanguages)
            if (string.Equals(t.LanguageTag, lang.LanguageTag, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private bool SystemSpeechIsRomanian()
    {
        var sys = SpeechRecognizer.SystemSpeechLanguage;
        if (sys == null) return false;
        return sys.LanguageTag.StartsWith("ro", StringComparison.OrdinalIgnoreCase);
    }

    private async void InitRecognizer()
    {
        if (initInProgress) return;
        initInProgress = true;

        int myToken = runToken;

        try
        {
            SetUI("Speech: init...");

            await StopRecognizerInternal();
            if (!shouldRun || myToken != runToken) return;

            var lang = PickBestLanguageForListConstraint();
            recognizer = new SpeechRecognizer(lang);

            if (disableTimeouts)
            {
                recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.Zero;
                recognizer.Timeouts.EndSilenceTimeout = TimeSpan.Zero;
                recognizer.Timeouts.BabbleTimeout = TimeSpan.Zero;
            }
            else
            {
                recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(10);
                recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1.0);
                recognizer.Timeouts.BabbleTimeout = TimeSpan.FromSeconds(2);
            }

            recognizer.Constraints.Clear();

            // LISTA: comenzi stabile + câteva obiecte uzuale (pentru two-step)
            recognizer.Constraints.Add(new SpeechRecognitionListConstraint(new[]
            {
                // MAIN MENU
                "start","pornește","porneste","începe","incepe",
                "înapoi","inapoi","meniu",
                "exit","ieșire","iesire","închide","inchide",

                // DESCRIERE
                "descriere","descrie",

                // OCR
                "text","citire","citește","citeste",

                // PRODUCT SCAN
                "scanează produs","scaneaza produs","scanare produs",
                "scanează cod","scaneaza cod","scanează codul","scaneaza codul","citește codul","citeste codul",
                "oprește scanarea","opreste scanarea","oprește scanarea produsului","opreste scanarea produsului",
                "stop scanare","gata scanare",

                // FIND / CAUTARE
                "caută","cauta","căutare","cautare","găsește","gaseste","unde e","unde este",

                // Obiecte uzuale (two-step)
                "ușă","usa","scaun","masă","masa","birou","telefon","laptop","televizor","tv","pat","canapea","sticlă","sticla","cană","cana","dulap",

                // FOUND
                "am găsit","am gasit","l-am găsit","l am găsit","l-am gasit","l am gasit",

                // SAFETY
                "siguranță","siguranta","liniște","liniste",

                // STOP global
                "stop","gata","oprește","opreste","termină","termina","termin"
            }, "commands"));

            // Dictation: doar dacă sistemul e pe română (altfel îți “englezeste” și strică)
            bool systemRo = SystemSpeechIsRomanian();
            bool topicOk = IsTopicLanguageSupported(lang);

            if (enableDictation && systemRo && topicOk)
            {
                recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation, "dictation"));
            }
            else if (enableDictation)
            {
                Debug.LogWarning($"[UWPSpeechRecognizer] Dictation OFF. systemRo={systemRo} topicOk={topicOk} lang={lang.LanguageTag} sys={SpeechRecognizer.SystemSpeechLanguage?.LanguageTag}");
            }

            var compile = await recognizer.CompileConstraintsAsync();
            if (!shouldRun || myToken != runToken) return;

            if (compile.Status != SpeechRecognitionResultStatus.Success)
            {
                Debug.LogError("[UWPSpeechRecognizer] Compile failed: " + compile.Status);
                SetUI("Speech: compile failed: " + compile.Status);
                return;
            }

            recognizer.ContinuousRecognitionSession.ResultGenerated += OnResultGenerated;
            if (autoRestartSession)
                recognizer.ContinuousRecognitionSession.Completed += OnSessionCompleted;

            try
            {
                await recognizer.ContinuousRecognitionSession.StartAsync();
            }
            catch (UnauthorizedAccessException)
            {
                Debug.LogError("[UWPSpeechRecognizer] No microphone access (UnauthorizedAccessException).");
                SetUI("Speech: FĂRĂ ACCES MICROFON. Verifică Capabilities: Microphone.");
                return;
            }
            catch (Exception e)
            {
                Debug.LogError("[UWPSpeechRecognizer] StartAsync exception: " + e);
                SetUI("Speech StartAsync exception: " + e.Message);
                return;
            }

            if (!shouldRun || myToken != runToken) return;

            string sysTag = SpeechRecognizer.SystemSpeechLanguage != null ? SpeechRecognizer.SystemSpeechLanguage.LanguageTag : "null";
            SetUI($"Ascult... (grammar={lang.LanguageTag}, system={sysTag}, dictation={(enableDictation && systemRo && topicOk ? "ON" : "OFF")})");
        }
        catch (Exception e)
        {
            Debug.LogError("[UWPSpeechRecognizer] Init exception: " + e);
            SetUI("Speech init exception: " + e.Message);
        }
        finally
        {
            initInProgress = false;
        }
    }

    private void OnSessionCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        if (!shouldRun) return;

        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
            if (!shouldRun) return;
            Debug.LogWarning("[UWPSpeechRecognizer] Session completed: " + args.Status + " -> restart");
            SetUI("Speech: restart...");
            CancelInvoke(nameof(BeginInit));
            Invoke(nameof(BeginInit), Mathf.Max(0.01f, initDelaySeconds));
        }, false);
    }

    private void OnResultGenerated(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args?.Result == null) return;
        if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected) return;

        string raw = args.Result.Text ?? "";
        string cleaned = CleanForRouter(raw);

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length <= 1)
            return;

        // False-positive STOP control
        if (cleaned == "stop" && args.Result.Confidence == SpeechRecognitionConfidence.Low)
        {
            var now = DateTime.UtcNow;
            if (now - lastStopUtc > stopRepeatWindow)
            {
                lastStopUtc = now;
                return;
            }
        }

        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
            if (!shouldRun) return;

            if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);

            router?.azureTTS?.StopNow();
            SetUI("Am auzit: " + cleaned);

            router?.AcceptCommand(cleaned, raw);
        }, false);
    }

    private async Task StopRecognizerInternal()
    {
        if (recognizer == null) return;

        try { recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated; } catch { }
        try
        {
            if (autoRestartSession)
                recognizer.ContinuousRecognitionSession.Completed -= OnSessionCompleted;
        }
        catch { }

        try { await recognizer.ContinuousRecognitionSession.StopAsync(); } catch { }
        try { recognizer.Dispose(); } catch { }

        recognizer = null;
    }
#endif

    private void SetUI(string msg)
    {
        if (debugOutputText != null) debugOutputText.text = msg;
    }

    // Curățare: diacritice OUT, punctuație -> spațiu, spații multiple -> unul
    private static string CleanForRouter(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        input = input.ToLowerInvariant().Trim();
        string normalized = input.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();

        foreach (char ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        string s = sb.ToString().Normalize(NormalizationForm.FormC);
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }
}