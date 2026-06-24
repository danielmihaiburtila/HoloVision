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
    public string languageTag = "ro-RO";

    [Header("Reliability")]
    public bool autoRestartSession = true;
    public bool disableTimeouts = true;
    public float initDelaySeconds = 0.25f;

    [Header("Constraints")]
    public bool enableDictation = true;

    private bool nameCaptureMode = false;
    private bool nameDictationActuallyAvailable = false;
    private bool sosResponseMode = false;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private SpeechRecognizer recognizer;

    private bool shouldRun = false;
    private bool initInProgress = false;
    private int runToken = 0;

    private DateTime lastStopUtc = DateTime.MinValue;
    private readonly TimeSpan stopRepeatWindow = TimeSpan.FromSeconds(1.2);

    private DateTime lastStartUtc = DateTime.MinValue;
    private readonly TimeSpan startRepeatWindow = TimeSpan.FromSeconds(1.4);
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
        SetUI("Speech: activ doar pe device UWP / HoloLens.");
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
        {
            if (string.Equals(t.LanguageTag, lang.LanguageTag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

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

        bool systemRo = SystemSpeechIsRomanian();
        bool topicOk = IsTopicLanguageSupported(lang);
        nameDictationActuallyAvailable = false;

        // ==========================================================
        // PRIORITATE 1: MOD SOS
        // Când sistemul întreabă "Ești bine?",
        // folosim o gramatică scurtă și clară:
        // SUNT BINE / DA / NU / AJUTOR etc.
        // ==========================================================
        if (sosResponseMode)
        {
            recognizer.Constraints.Add(
                new SpeechRecognitionListConstraint(
                    BuildSosResponseGrammar(),
                    "sos_response"
                )
            );
        }
        // ==========================================================
        // PRIORITATE 2: CAPTURĂ NUME
        // Se păstrează exact logica ta existentă.
        // ==========================================================
        else if (nameCaptureMode)
        {
            if (enableDictation && systemRo && topicOk)
            {
                recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(
                    SpeechRecognitionScenario.Dictation,
                    "name_dictation"));

                nameDictationActuallyAvailable = true;
            }
            else
            {
                recognizer.Constraints.Add(new SpeechRecognitionListConstraint(new[]
                {
                    "numele meu este",
                    "ma cheama",
                    "mă cheamă",
                    "eu sunt",
                    "ma numesc",
                    "mă numesc",
                    "sari",
                    "fara nume",
                    "fără nume",
                    "da",
                    "nu"
                }, "name_fallback"));
            }
        }
        // ==========================================================
        // PRIORITATE 3: COMENZI NORMALE
        // DESCRIERE, TEXT, CAUTĂ, SCANEAZĂ PRODUS etc.
        // ==========================================================
        else
        {
            recognizer.Constraints.Add(
                new SpeechRecognitionListConstraint(
                    BuildCommandGrammar(),
                    "commands"
                )
            );
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
        catch (Exception e)
        {
            Debug.LogError("[UWPSpeechRecognizer] StartAsync exception: " + e);
            SetUI("Speech StartAsync exception: " + e.Message);
            return;
        }

        string sysTag = SpeechRecognizer.SystemSpeechLanguage != null
            ? SpeechRecognizer.SystemSpeechLanguage.LanguageTag
            : "null";

        if (sosResponseMode)
        {
            SetUI($"Ascult SOS... grammar={lang.LanguageTag}, system={sysTag}");
            Debug.Log($"[UWPSpeechRecognizer] SOS mode active. grammar={lang.LanguageTag}, system={sysTag}");
        }
        else if (nameCaptureMode)
        {
            SetUI($"Ascult numele... grammar={lang.LanguageTag}, system={sysTag}, nameDictation={(nameDictationActuallyAvailable ? "ON" : "FALLBACK")}");
            Debug.Log($"[UWPSpeechRecognizer] Name mode active. grammar={lang.LanguageTag}, system={sysTag}, dictation={(nameDictationActuallyAvailable ? "ON" : "FALLBACK")}");
        }
        else
        {
            SetUI($"Ascult... grammar={lang.LanguageTag}, system={sysTag}");
            Debug.Log($"[UWPSpeechRecognizer] Command mode active. grammar={lang.LanguageTag}, system={sysTag}");
        }
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

    private string[] BuildCommandGrammar()
    {
        return new[]
        {
            // MAIN MENU
            "start",
            "pornește",
            "porneste",
            "începe",
            "incepe",
            "activează",
            "activeaza",

            "mod vocal",
            "modul vocal",
            "vocal",

            "mod discret",
            "modul discret",
            "discret",

            "înapoi",
            "inapoi",
            "meniu",

            "exit",
            "ieșire",
            "iesire",
            "închide",
            "inchide",
            "închide aplicația",
            "inchide aplicatia",

            // DESCRIERE
            "descriere",
            "descrie",
            "descrie mediul",
            "descriere mediu",

            // OCR / TEXT
            "text",
            "citire",
            "citește",
            "citeste",
            "citește textul",
            "citeste textul",

            // PRODUCT SCAN
            "scanează produs",
            "scaneaza produs",
            "scanare produs",
            "scanează produsul",
            "scaneaza produsul",
            "scanează ambalajul",
            "scaneaza ambalajul",
            "scanează eticheta",
            "scaneaza eticheta",

            "scanează cod",
            "scaneaza cod",
            "scanează codul",
            "scaneaza codul",
            "citește codul",
            "citeste codul",

            "oprește scanarea",
            "opreste scanarea",
            "oprește scanarea produsului",
            "opreste scanarea produsului",
            "stop scanare",
            "gata scanare",

            // FIND START
            "caută",
            "cauta",
            "căutare",
            "cautare",
            "găsește",
            "gaseste",
            "unde e",
            "unde este",

            // SIMPLE OBJECTS FOR TWO-STEP: "CAUTĂ" -> "SCAUN"
            "scaun",
            "scaunul",
            "pat",
            "patul",
            "masă",
            "masa",
            "dulap",
            "dulapul",
            "canapea",
            "canapeaua",
            "telefon",
            "telefonul",
            "laptop",
            "televizor",
            "televizorul",
            "tv",
            "sticlă",
            "sticla",
            "sticlă de apă",
            "sticla de apa",
            "cană",
            "cana",
            "ușă",
            "usa",
            "ușa",
            "usă",
            "ușile",
            "usile",
            "ușa din față",
            "usa din fata",
            "ușă din față",
            "usa de la intrare",
            "ușa de la intrare",
            "fereastră",
            "fereastra",
            "geam",
            "geamul",
            "birou",
            "biroul",
            "ieșire",
            "iesire",
            "ieșirea",
            "iesirea",
            "exit",
            "semn exit",
            "semnul exit",
            "semn de exit",
            "semnul de exit",
            "semn ieșire",
            "semn iesire",
            "semnul ieșire",
            "semnul iesire",
            "semn de ieșire",
            "semn de iesire",
            "semnul de ieșire",
            "semnul de iesire",
            "coș de gunoi",
            "cos de gunoi",
            "coșul de gunoi",
            "cosul de gunoi",
            "gunoi",
            "ochelari",
            "ochelarii",
            "pereche de ochelari",
            "ochelari de vedere",
            "ochelari de soare",

            // DIRECT FIND COMMANDS
            "caută scaun",
            "cauta scaun",
            "caută scaunul",
            "cauta scaunul",

            "caută pat",
            "cauta pat",
            "caută patul",
            "cauta patul",

            "caută masă",
            "cauta masa",
            "caută masa",
            "cauta masă",

            "caută dulap",
            "cauta dulap",
            "caută dulapul",
            "cauta dulapul",

            "caută canapea",
            "cauta canapea",
            "caută canapeaua",
            "cauta canapeaua",

            "caută telefon",
            "cauta telefon",
            "caută telefonul",
            "cauta telefonul",

            "caută laptop",
            "cauta laptop",

            "caută televizor",
            "cauta televizor",
            "caută televizorul",
            "cauta televizorul",
            "caută tv",
            "cauta tv",

            "caută sticlă",
            "cauta sticla",
            "caută sticla",
            "cauta sticlă",
            "caută sticlă de apă",
            "cauta sticla de apa",

            "caută cană",
            "cauta cana",
            "caută cana",
            "cauta cană",

            "caută ușa",
            "cauta usa",
            "caută usa",
            "cauta ușa",
            "caută ușă",
            "cauta ușă",
            "caută usă",
            "cauta usă",
            "caută ușa din față",
            "cauta usa din fata",
            "caută ușă din față",
            "cauta usa de la intrare",
            "caută ușa de la intrare",

            "caută fereastra",
            "cauta fereastra",
            "caută fereastră",
            "cauta fereastră",

            "caută geam",
            "cauta geam",
            "caută geamul",
            "cauta geamul",

            "caută birou",
            "cauta birou",
            "caută biroul",
            "cauta biroul",

            "caută ieșirea",
            "cauta iesirea",
            "caută ieșire",
            "cauta iesire",
            "caută exit",
            "cauta exit",

            "caută ușa de ieșire",
            "cauta usa de iesire",
            "caută usa de iesire",
            "cauta ușa de ieșire",

            "caută semnul exit",
            "cauta semnul exit",
            "caută semn exit",
            "cauta semn exit",

            "caută semnul de ieșire",
            "cauta semnul de iesire",
            "caută semn de ieșire",
            "cauta semn de iesire",

            "caută coșul de gunoi",
            "cauta cosul de gunoi",
            "caută coș de gunoi",
            "cauta cos de gunoi",

            "caută ochelari",
            "cauta ochelari",
            "caută ochelarii",
            "cauta ochelarii",
            "găsește ochelari",
            "gaseste ochelari",
            "unde sunt ochelarii",
            "unde e ochelarii",
            "unde sunt ochelari",

            // FREE SEAT / FREE TABLE
            "scaun liber",
            "scaunul liber",
            "un scaun liber",
            "scaunul este liber",
            "scaunul e liber",

            "caută scaun liber",
            "cauta scaun liber",
            "caută un scaun liber",
            "cauta un scaun liber",
            "caută scaunul liber",
            "cauta scaunul liber",
            "caută scaunul este liber",
            "cauta scaunul este liber",
            "caută scaunul e liber",
            "cauta scaunul e liber",

            "masă liberă",
            "masa libera",
            "o masă liberă",
            "o masa libera",
            "masa este libera",
            "masa e libera",

            "caută masă liberă",
            "cauta masa libera",
            "caută masa liberă",
            "cauta masă liberă",
            "caută o masă liberă",
            "cauta o masa libera",
            "caută masa este liberă",
            "cauta masa este libera",
            "caută masa e liberă",
            "cauta masa e libera",

            "birou liber",
            "biroul liber",
            "caută birou liber",
            "cauta birou liber",
            "caută biroul liber",
            "cauta biroul liber",

            // FOUND / STOP
            "am găsit",
            "am gasit",
            "l-am găsit",
            "l am găsit",
            "l-am gasit",
            "l am gasit",

            "stop",
            "gata",
            "oprește",
            "opreste",
            "termină",
            "termina",
            "termin",

            // SAFETY
            "siguranță",
            "siguranta",
            "liniște",
            "liniste",

           // SOS - răspunsuri OK
"sunt bine",
"sunt ok",
"ma simt bine",
"mă simt bine",
"sunt în regulă",
"sunt in regula",
"totul e bine",
"totul este bine",
"este bine",
"e bine",
"nu am pățit nimic",
"nu am patit nimic",
"n-am pățit nimic",
"n-am patit nimic",
"nu am nevoie de ajutor",
"n-am nevoie de ajutor",
"fără ajutor",
"fara ajutor",
"da",
"bine",
"ok",

// SOS - răspunsuri HELP
"nu",
"nu sunt bine",
"nu sînt bine",
"nu ma simt bine",
"nu mă simt bine",
"nu sunt ok",
"nu e bine",
"nu este bine",
"nu sunt în regulă",
"nu sunt in regula",
"ajutor",
"am nevoie de ajutor",
"ajută-mă",
"ajuta ma",
"ajuta-ma",
"ajutați-mă",
"ajutati ma",
"trimite ajutor",
"cheama ajutor",
"cheamă ajutor",
"ambulanță",
"ambulanta",
"urgență",
"urgenta",
"suna la 112",
"sună la 112",
"112",
"m-am lovit",
"m am lovit",
"mă doare",
"ma doare",
"am căzut",
"am cazut",
"sunt rănit",
"sunt ranit",
"sunt rănită",
"sunt ranita",
"îmi este rău",
"imi este rau",
"mi-e rău",
"mi e rau",
// SOS - răspunsuri OK suplimentare
"eu sunt bine",
"sunt bine multumesc",
"sunt bine mulțumesc",
"sunt bine merci",
"sunt ok",
"e ok",
"este ok",
"sunt in regula",
"sînt în regulă",
"sint in regula",
"sunt în regulă",
"nu am nimic",
"n-am nimic",
"n am nimic",
"nu trebuie ajutor",

// SOS - răspunsuri HELP suplimentare
"nu sunt bine",
"nu sint bine",
"nu sant bine",
"nu sunt în regulă",
"nu sunt in regula",
"nu ma pot ridica",
"nu pot sa ma ridic",
"nu pot să mă ridic",
"cheama pe cineva",
"cheamă pe cineva",


            // LIGHT / BILL
            "factura curent",
            "factură curent",
            "verifica lumina",
            "verifică lumina",
            "lumina",
            "lumină",
            "mod factura",
            "mod factură",
            "consum curent",
            "consumă curent",
            "este lumină",
            "e lumină",
            "este intuneric",
            "este întuneric",
            "e intuneric",
            "e întuneric",
            "verifică luminozitatea",
            "verifica luminozitatea",
            "câtă lumină este",
            "cata lumina este",

            // BULB
            "verifica becul",
            "verifică becul",
            "verifica bec",
            "verifică bec",
            "bec",
            "becul",
            "bec aprins",
            "bec stins",
            "este becul aprins",
            "e becul aprins",
            "verifică dacă becul este aprins",
            "verifica daca becul este aprins",
            "verifică dacă becul e stins",
            "verifica daca becul e stins",
        };
    }

    private void OnSessionCompleted(
        SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionCompletedEventArgs args)
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

    string raw = args.Result.Text ?? "";
    string cleaned = CleanForRouter(raw);
    string confidenceText = args.Result.Confidence.ToString();

    // ==========================================================
    // PRIORITATE MAXIMĂ: MOD SOS
    // În modul SOS NU aruncăm automat rezultatele "Rejected".
    // Pentru urgență, chiar și un rezultat cu încredere mică trebuie
    // trimis către SosFallSystem, care decide dacă este:
    // AJUTOR / NU / SUNT BINE.
    // ==========================================================
    if (sosResponseMode)
    {
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length <= 1)
            return;

        UnityEngine.WSA.Application.InvokeOnAppThread(() =>
        {
            if (!shouldRun) return;

            if (router == null)
                router = FindObjectOfType<VoiceCommandRouter>(true);

            Debug.Log(
                "[UWPSpeechRecognizer][SOS] Heard: " +
                cleaned +
                " | raw: " +
                raw +
                " | confidence=" +
                confidenceText
            );

            SetUI("SOS răspuns: " + cleaned);

            router?.azureTTS?.StopNow();
            router?.AcceptCommand(cleaned, raw);

        }, false);

        return;
    }

    // ==========================================================
    // COMENZI NORMALE
    // Pentru restul aplicației păstrăm filtrul vechi.
    // Asta nu strică DESCRIERE / TEXT / CAUTĂ / SCANARE etc.
    // ==========================================================
    if (args.Result.Confidence == SpeechRecognitionConfidence.Rejected)
        return;

    bool originalWasBareFind =
        cleaned == "cauta" ||
        cleaned == "caută" ||
        cleaned == "cautare" ||
        cleaned == "căutare";

    if (string.IsNullOrWhiteSpace(cleaned) || cleaned.Length <= 1)
        return;

    if (cleaned == "stop" && args.Result.Confidence == SpeechRecognitionConfidence.Low)
    {
        var now = DateTime.UtcNow;
        if (now - lastStopUtc > stopRepeatWindow)
        {
            lastStopUtc = now;
            return;
        }
    }

    try
    {
        var alts = args.Result.GetAlternates(10);
        if (alts != null)
        {
            foreach (var a in alts)
            {
                string altRaw = a.Text ?? "";
                string altClean = CleanForRouter(altRaw);

                // Dacă utilizatorul a spus doar „caută”,
                // nu accepta o alternativă care inventează obiectul.
                if (originalWasBareFind &&
                    (
                        altClean.StartsWith("cauta ") ||
                        altClean.StartsWith("caută ") ||
                        altClean.StartsWith("gaseste ") ||
                        altClean.StartsWith("găsește ")
                    ))
                {
                    continue;
                }

                // Prioritate pentru ușă, ca să nu fie înlocuită greșit cu masă.
                if (HasDoorIntent(altClean))
                {
                    cleaned = altClean;
                    raw = altRaw;
                    break;
                }

                // Nu lăsa o alternativă "masă" să suprascrie o comandă care pare ușă.
                if (HasDoorIntent(cleaned) && HasTableIntent(altClean))
                {
                    continue;
                }

                if (HasObjectFamilyConflict(cleaned, altClean))
                    continue;

                if (string.IsNullOrWhiteSpace(altClean))
                    continue;

                if (IsStrongStartCommand(altClean))
                {
                    cleaned = altClean;
                    raw = altRaw;
                    break;
                }

                if (IsStrongFindOrObjectCommand(altClean))
                {
                    cleaned = altClean;
                    raw = altRaw;
                    break;
                }

                if (IsStrongFeatureCommand(altClean))
                {
                    cleaned = altClean;
                    raw = altRaw;
                    break;
                }
            }
        }
    }
    catch
    {
        // Pe unele versiuni UWP, GetAlternates poate arunca excepție.
    }

    UnityEngine.WSA.Application.InvokeOnAppThread(() =>
    {
        if (!shouldRun) return;

        if (router == null)
            router = FindObjectOfType<VoiceCommandRouter>(true);

        if (router != null && router.IsWaitingForUserNameCapture())
        {
            string bestNameRaw = PickBestNameCandidate(args);
            string bestNameClean = CleanForRouter(bestNameRaw);

            router?.azureTTS?.StopNow();
            SetUI("Am auzit nume: " + bestNameClean);

            router?.AcceptCommand(bestNameClean, bestNameRaw);
            return;
        }

        bool finderProbablyRunning = IsFinderProbablyRunning();
        bool commandUsefulDuringFind = IsUsefulDuringFind(cleaned);

        if (!finderProbablyRunning || commandUsefulDuringFind)
        {
            router?.azureTTS?.StopNow();
        }

        SetUI("Am auzit: " + cleaned);
        router?.AcceptCommand(cleaned, raw);

    }, false);
}

    private bool IsFinderProbablyRunning()
    {
        try
        {
            if (router == null)
                router = FindObjectOfType<VoiceCommandRouter>(true);

            if (router == null)
                return false;

            string active = CleanForRouter(router.ActiveFeatureName ?? "");

            if (active.Contains("caut"))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    private bool IsUsefulDuringFind(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        c = CleanForRouter(c);

        if (c == "stop") return true;
        if (c == "gata") return true;
        if (c == "opreste") return true;
        if (c == "oprește") return true;
        if (c == "termina") return true;
        if (c == "termină") return true;
        if (c == "termin") return true;

        if (c.Contains("am gasit")) return true;
        if (c.Contains("am găsit")) return true;
        if (c.Contains("l am gasit")) return true;
        if (c.Contains("l am găsit")) return true;
        if (c.Contains("l am gasit")) return true;
        if (c.Contains("l am găsit")) return true;

        return false;
    }

    private bool IsStrongStartCommand(string c)
    {
        c = CleanForRouter(c);

        return c == "start" ||
               c == "porneste" ||
               c == "pornește" ||
               c == "incepe" ||
               c == "începe" ||
               c == "activeaza" ||
               c == "activează";
    }

    private bool IsStrongFeatureCommand(string c)
    {
        c = CleanForRouter(c);

        if (c == "descriere" || c == "descrie") return true;
        if (c == "text" || c == "citire" || c == "citeste" || c == "citește") return true;

        if (c.Contains("scaneaza produs") || c.Contains("scanează produs")) return true;
        if (c.Contains("scanare produs")) return true;
        if (c.Contains("scaneaza cod") || c.Contains("scanează cod")) return true;

        if (c.Contains("factura curent") || c.Contains("factură curent")) return true;
        if (c.Contains("verifica lumina") || c.Contains("verifică lumina")) return true;

        if (c.Contains("verifica bec") || c.Contains("verifică bec")) return true;

        // SOS - comenzi / răspunsuri critice
        if (c == "ajutor") return true;
        if (c.Contains("ajutor")) return true;
        if (c.Contains("ambulanta")) return true;
        if (c.Contains("ambulanță")) return true;
        if (c.Contains("urgenta")) return true;
        if (c.Contains("urgență")) return true;
        if (c.Contains("112")) return true;

        if (c == "da" || c == "nu") return true;
        if (c == "bine" || c == "ok") return true;
        if (c.Contains("sunt bine")) return true;
        if (c.Contains("sint bine")) return true;
        if (c.Contains("sunt ok")) return true;
        if (c.Contains("ma simt bine")) return true;
        if (c.Contains("mă simt bine")) return true;
        if (c.Contains("sunt in regula")) return true;
        if (c.Contains("sunt în regulă")) return true;
        if (c.Contains("nu sunt bine")) return true;
        if (c.Contains("nu ma simt bine")) return true;
        if (c.Contains("nu mă simt bine")) return true;
        if (c.Contains("nu sunt ok")) return true;

        return false;
    }

    private bool IsStrongFindOrObjectCommand(string c)
    {
        c = CleanForRouter(c);

        if (string.IsNullOrWhiteSpace(c))
            return false;

        if (c == "cauta" || c == "caută" || c == "cautare" || c == "căutare")
            return true;

        if (c.StartsWith("cauta ")) return true;
        if (c.StartsWith("caută ")) return true;
        if (c.StartsWith("gaseste ")) return true;
        if (c.StartsWith("găsește ")) return true;
        if (c.StartsWith("unde e ")) return true;
        if (c.StartsWith("unde este ")) return true;

        if (IsSimpleObjectWord(c))
            return true;

        if (c.Contains("scaun liber")) return true;
        if (c.Contains("scaunul liber")) return true;
        if (c.Contains("masa libera")) return true;
        if (c.Contains("masa liberă")) return true;
        if (c.Contains("birou liber")) return true;
        if (c.Contains("biroul liber")) return true;

        if (c.Contains("semn iesire")) return true;
        if (c.Contains("semn ieșire")) return true;
        if (c.Contains("semnul iesire")) return true;
        if (c.Contains("semnul ieșire")) return true;
        if (c.Contains("semn exit")) return true;
        if (c.Contains("iesire")) return true;
        if (c.Contains("ieșire")) return true;
        if (c.Contains("iesirea")) return true;
        if (c.Contains("ieșirea")) return true;

        return false;
    }

    private bool IsSimpleObjectWord(string c)
    {
        c = CleanForRouter(c);

        switch (c)
        {
            case "scaun":
            case "scaunul":
            case "pat":
            case "patul":
            case "masa":
            case "masă":
            case "dulap":
            case "dulapul":
            case "canapea":
            case "canapeaua":
            case "telefon":
            case "telefonul":
            case "laptop":
            case "televizor":
            case "televizorul":
            case "tv":
            case "sticla":
            case "sticlă":
            case "sticla de apa":
            case "sticlă de apă":
            case "cana":
            case "cană":
            case "usa":
            case "ușa":
            case "fereastra":
            case "fereastră":
            case "geam":
            case "geamul":
            case "birou":
            case "biroul":
            case "iesire":
            case "ieșire":
            case "iesirea":
            case "ieșirea":
            case "exit":
            case "gunoi":
            case "cos de gunoi":
            case "coș de gunoi":
            case "cosul de gunoi":
            case "coșul de gunoi":
                return true;
            case "ochelari":
            case "ochelarii":
            case "pereche de ochelari":
            case "ochelari de vedere":
            case "ochelari de soare":
                return true;
        }

        return false;
    }

    private string PickBestNameCandidate(SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        string best = args?.Result?.Text ?? "";
        int bestScore = ScoreNameCandidate(best);

        try
        {
            var alts = args.Result.GetAlternates(10);
            if (alts != null)
            {
                foreach (var a in alts)
                {
                    string candidate = a.Text ?? "";
                    int score = ScoreNameCandidate(candidate);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
        }
        catch { }

        return best;
    }

    private int ScoreNameCandidate(string rawText)
    {
        string c = CleanForRouter(rawText);

        if (string.IsNullOrWhiteSpace(c))
            return -1000;

        if (c.Length < 2)
            return -1000;

        string[] blocked =
        {
            "start",
            "stop",
            "gata",
            "opreste",
            "oprește",
            "termina",
            "termină",
            "mod vocal",
            "mod discret",
            "descriere",
            "descrie",
            "text",
            "citire",
            "citeste",
            "citește",
            "ajutor",
            "da",
            "nu",
            "factura curent",
            "factură curent",
            "verifica becul",
            "verifică becul",
            "verifica bec",
            "verifică bec"
        };

        foreach (var b in blocked)
        {
            if (c == CleanForRouter(b))
                return -1000;
        }

        int tokenCount = c.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;

        if (tokenCount > 3)
            return -100;

        int score = 100;
        score += c.Length;

        if (tokenCount == 1) score += 20;
        if (tokenCount == 2) score += 10;

        return score;
    }

    private async Task StopRecognizerInternal()
    {
        if (recognizer == null) return;

        try
        {
            recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResultGenerated;
        }
        catch { }

        try
        {
            if (autoRestartSession)
                recognizer.ContinuousRecognitionSession.Completed -= OnSessionCompleted;
        }
        catch { }

        try
        {
            await recognizer.ContinuousRecognitionSession.StopAsync();
        }
        catch { }

        try
        {
            recognizer.Dispose();
        }
        catch { }

        recognizer = null;
    }

#endif

    private void SetUI(string msg)
    {
        if (debugOutputText != null)
            debugOutputText.text = msg;
    }

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

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    public void SetNameCaptureMode(bool enabled)
    {
#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        if (nameCaptureMode == enabled) return;

        nameCaptureMode = enabled;

        if (shouldRun)
        {
            runToken++;
            CancelInvoke(nameof(BeginInit));
            _ = StopRecognizerInternal();
            Invoke(nameof(BeginInit), Mathf.Max(0.01f, initDelaySeconds));
        }
#endif
    }
    private bool HasDoorIntent(string c)
    {
        c = CleanForRouter(c);

        return c == "usa" ||
               c == "usa din fata" ||
               c == "usa de la intrare" ||
               c == "usa intrare" ||
               c == "usa de iesire" ||
               c == "usa iesire" ||
               c == "usa iesirea" ||
               c.Contains("cauta usa") ||
               c.Contains("gaseste usa") ||
               c.Contains("unde este usa") ||
               c.Contains("unde e usa") ||
               c.Contains("usa de la intrare") ||
               c.Contains("usa din fata") ||
               c.Contains("usa de iesire") ||
               c.Contains("usa iesire");
    }


    private bool HasTableIntent(string c)
    {
        c = CleanForRouter(c);

        return c == "masa" ||
               c == "masa libera" ||
               c.Contains("cauta masa") ||
               c.Contains("gaseste masa") ||
               c.Contains("unde este masa") ||
               c.Contains("unde e masa") ||
               c.Contains("masa libera");
    }

    private bool HasObjectFamilyConflict(string currentCleaned, string alternativeCleaned)
    {
        currentCleaned = CleanForRouter(currentCleaned);
        alternativeCleaned = CleanForRouter(alternativeCleaned);

        // Dacă rezultatul curent pare ușă, nu lăsa alternativa „masă” să îl strice.
        if (HasDoorIntent(currentCleaned) && HasTableIntent(alternativeCleaned))
            return true;

        // IMPORTANT:
        // Nu blocăm inversul. Dacă UWP a auzit primar „masă”,
        // dar în alternative există „ușă”, vrem să putem corecta spre ușă.
        return false;
    }
    public void SetSosResponseMode(bool enabled)
    {
        if (sosResponseMode == enabled)
            return;

        sosResponseMode = enabled;

        // În mod SOS nu vrem captură nume.
        if (enabled)
            nameCaptureMode = false;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    if (!isActiveAndEnabled)
        return;

    shouldRun = true;
    runToken++;

    CancelInvoke(nameof(BeginInit));
    Invoke(nameof(BeginInit), 0.05f);
#endif

        SetUI(enabled ? "Speech: mod SOS activ." : "Speech: mod comenzi normal.");
    }
    private string[] BuildSosResponseGrammar()
    {
        return new[]
        {
        // OK
        "da",
        "bine",
        "ok",
        "okay",
        "sunt bine",
        "eu sunt bine",
        "sunt bine mulțumesc",
        "sunt bine multumesc",
        "sunt ok",
        "sunt în regulă",
        "sunt in regula",
        "sint bine",
        "sint ok",
        "sint in regula",
        "mă simt bine",
        "ma simt bine",
        "mă simt ok",
        "ma simt ok",
        "totul e bine",
        "totul este bine",
        "e bine",
        "este bine",
        "e ok",
        "nu am pățit nimic",
        "nu am patit nimic",
        "n-am pățit nimic",
        "n-am patit nimic",
        "nu am nimic",
        "n-am nimic",
        "n am nimic",
        "nu am nevoie de ajutor",
        "n-am nevoie de ajutor",
        "n am nevoie de ajutor",
        "fără ajutor",
        "fara ajutor",

        // HELP
        "nu",
        "nu sunt bine",
        "nu sint bine",
        "nu sunt ok",
        "nu mă simt bine",
        "nu ma simt bine",
        "nu sunt în regulă",
        "nu sunt in regula",
        "ajutor",
        "am nevoie de ajutor",
        "ajută-mă",
        "ajuta ma",
        "ajuta-ma",
        "ajutați-mă",
        "ajutati ma",
        "trimite ajutor",
        "cheamă ajutor",
        "cheama ajutor",
        "cheamă pe cineva",
        "cheama pe cineva",
        "ambulanță",
        "ambulanta",
        "urgență",
        "urgenta",
        "suna la 112",
        "sună la 112",
        "112",
        "m-am lovit",
        "m am lovit",
        "mă doare",
        "ma doare",
        "am căzut",
        "am cazut",
        "nu mă pot ridica",
        "nu ma pot ridica",
        "nu pot să mă ridic",
        "nu pot sa ma ridic",
        "îmi este rău",
        "imi este rau",
        "mi-e rău",
        "mi e rau"
    };
    }
}