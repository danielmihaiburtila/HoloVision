using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class ObjectFinderSystem : MonoBehaviour
{
    public enum Provider { AzureVision, GoogleVision }

    [Header("Provider")]
    public Provider provider = Provider.AzureVision;

    // =========================
    // AZURE (existing)
    // =========================
    [Header("Azure Vision (v3.2 Analyze)")]
    public string subscriptionKey = "CHEIA_TA_VISION";
    public string endpoint = "https://nume.cognitiveservices.azure.com/";
    public string language = "en";

    // =========================
    // GOOGLE (new)
    // =========================
    [Header("Google Cloud Vision")]
    [Tooltip("API Key pentru Cloud Vision API (ATENȚIE: key în client = doar pentru prototip).")]
    public string googleApiKey = "PUT_GOOGLE_VISION_KEY_HERE";

    [Tooltip("Dacă nu se găsește bbox, folosim LABEL_DETECTION ca fallback (fără direcție).")]
    public bool useLabelFallback = true;

    // =========================
    // OPTIONAL: Google -> Azure fallback (bbox only)
    // =========================
    [Header("Google -> Azure fallback (ONLY when Google has no box)")]
    [Tooltip("Dacă Google returnează doar label (fără bbox), încercăm Azure Objects ca să obținem bbox (beep/direcție).")]
    public bool useAzureBoxFallbackWhenGoogleHasNoBox = true;

    [Tooltip("Nu încercăm fallback Azure la fiecare frame (secunde).")]
    public float azureBoxFallbackCooldown = 1.8f;

    private float nextAzureBoxFallbackTime = -999f;

    // =========================
    // Label-only feedback (ca să nu fie “tăcere”)
    // =========================
    [Header("No-box feedback (label-only)")]
    [Tooltip("Dacă e ON, când avem doar label (fără bbox), vorbim rar ca user-ul să știe că 'vede' obiectul.")]
    public bool speakWhenNoBox = true;

    [Tooltip("Cooldown între mesajele label-only.")]
    public float noBoxSpeakCooldown = 4.0f;

    private float lastNoBoxSpeakTime = -999f;

    // =========================
    // Translator (optional, for ANY object)
    // =========================
    [Header("Optional: RO->EN translator for unknown objects")]
    [Tooltip("Dacă e setat, pentru obiecte care NU sunt în listă, traduce RO->EN și poate găsi orice obiect recunoscut de Vision.")]
    public AzureTranslator translator;

    [Tooltip("Dacă e ON, pentru obiecte necunoscute, se face RO->EN înainte de matching.")]
    public bool autoTranslateUnknownTargets = true;

    // =========================
    // Deps
    // =========================
    [Header("Deps")]
    public PhotoCaptureManager captureManager;
    public AzureTTS tts;
    public SpeechUIAnimator uiAnimator;

    // =========================
    // Search Loop
    // =========================
    [Header("Search Loop")]
    public float analyzeInterval = 1.8f;
    public int timeoutSeconds = 8;

    [Tooltip("Prag default (obiecte mari).")]
    [Range(0f, 1f)] public float minConfidenceDefault = 0.50f;

    [Tooltip("Prag mai permisiv (ușă/TV/masă/pat/sticlă/cană).")]
    [Range(0f, 1f)] public float minConfidenceMedium = 0.40f;

    [Tooltip("Timeout doar până când obiectul e găsit. După 'găsit', NU mai spune 'nu am găsit'.")]
    public float maxSearchSeconds = 25f;

    // =========================
    // After-found behavior
    // =========================
    [Header("After-found behavior")]
    [Tooltip("După ce a fost găsit, dacă nu-l mai vede X secunde -> spune 'am pierdut obiectul'.")]
    public float lostAfterSeconds = 3.0f;

    [Tooltip("Cooldown pentru mesajul 'am pierdut'.")]
    public float lostAnnounceCooldown = 3.0f;

    [Tooltip("Când bbox devine mare => utilizatorul e aproape. Prag din [0..1] din aria imaginii.")]
    [Range(0.02f, 0.60f)] public float closeAreaThreshold01 = 0.18f;

    [Tooltip("Cooldown pentru mesajul 'foarte aproape / în față'.")]
    public float closeAnnounceCooldown = 2.0f;

    // =========================
    // Performance (temporary lower capture while searching)
    // =========================
    [Header("Performance (temporary lower capture while searching)")]
    public bool overrideCaptureResolutionWhileSearching = true;
    public int finderMaxWidth = 1280;
    public int finderMaxHeight = 720;

    public bool overrideJpgQualityWhileSearching = true;
    [Range(30, 95)] public int finderJpgQuality = 75;

    // =========================
    // Beep Guidance
    // =========================
    [Header("Beep Guidance")]
    public bool enableBeep = true;
    public AudioSource beepSource;
    public AudioClip beepClip;
    [Range(0f, 1f)] public float beepVolume = 0.75f;
    public float beepMaxInterval = 0.70f;
    public float beepMinInterval = 0.14f;
    public bool spatialBeep = true;
    public float beepDistanceFromHead = 0.8f;
    public float beepSideOffset = 0.35f;

    [Header("Camera")]
    public Transform headCamera;

    [Header("Behavior")]
    [Tooltip("Dacă e TRUE, anunț 'Am găsit' doar când există bbox (deci pot ghida cu beep).")]
    public bool announceFoundOnlyWhenHasBox = true;

    [Header("Debug (compact)")]
    public bool showCompactDebug = true;
    public float debugUiInterval = 1.2f;
    public bool logJson = false;
    public bool logMatches = false;
    [Header("Search voice feedback")]
    [Tooltip("Dacă nu găsește imediat obiectul, spune periodic utilizatorului ce să facă.")]
    public bool speakSearchProgress = true;

    [Tooltip("Cooldown pentru mesajele vocale în timpul căutării.")]
    public float searchProgressSpeakCooldown = 4.5f;

    [Tooltip("Dacă serviciul Vision dă eroare, spune vocal problema, nu doar în Console.")]
    public bool speakVisionErrors = true;

    [Tooltip("Cooldown pentru erori Vision, ca să nu repete obsesiv.")]
    public float visionErrorSpeakCooldown = 6.0f;

    private float lastSearchProgressSpeakTime = -999f;
    private float lastVisionErrorSpeakTime = -999f;
    private int consecutiveCaptureFails = 0;
    private int consecutiveVisionFails = 0;

    // =========================
    // Internal
    // =========================
    private bool running;
    private bool requestInFlight;

    private string targetKey;                 // normalized RO (canonical)
    private string targetRaw;                 // as spoken
    private string[] wantedLabelsEn;          // what we match in Vision results

    // Translation gating (so we don’t start analyze until we know wanted EN labels)
    private bool translationReady = true;
    private int findToken = 0;

    private float startTime;
    private float nextAnalyzeTime;
    private float nextBeepTime;

    private bool announcedFound;
    private bool announcedTagOnly;

    private float lastStrength01;
    private Side lastSide = Side.Center;
    private bool hasBoundingBox;

    private int prevMaxW, prevMaxH, prevJpgQ;
    private float nextDebugUiTime;

    private float lastSeenTime = -999f;
    private float lastLostAnnounceTime = -999f;
    private float lastCloseAnnounceTime = -999f;
    private bool lostAnnouncedForCurrentLoss = false;
    private bool searchProgressSpokenOnce = false;

    private float lastDirectionSpeakTime = -999f;
    private Side lastSpokenDirection = Side.Center;

    [Header("Direction voice guidance")]
    public bool speakDirectionGuidance = true;
    public float directionSpeakCooldown = 3.0f;

    private enum Side { Left, Center, Right }

    private string analyzeUrl;   // Azure
    private string googleUrl;    // Google

    [Header("Interaction instructions")]
    public bool preferPhoneControlInstructions = false;


    public bool IsRunning => running;

    // =========================
    // AZURE response models
    // =========================
    [Serializable] private class AzureVisionRoot { public AzureVisionObject[] objects; public AzureVisionTag[] tags; }
    [Serializable] private class AzureVisionObject { public string @object; public float confidence; public AzureRectangle rectangle; }
    [Serializable] private class AzureRectangle { public int x; public int y; public int w; public int h; }
    [Serializable] private class AzureVisionTag { public string name; public float confidence; }

    [Header("Free seat / free table secondary verification")]
    [Tooltip("Număr de cadre pentru confirmarea libertății.")]
    public int occupancyCheckFrames = 3;

    [Tooltip("Câte cadre din cele de mai sus trebuie să spună 'liber' ca să confirmăm.")]
    public int occupancyFreeVotesNeeded = 2;

    [Tooltip("Câte secunde ignorăm temporar un scaun / o masă respinsă ca ocupată.")]
    public float rejectedTargetMemorySeconds = 4f;

    [Tooltip("Overlap minim pe țintă pentru a considera că e ocupată.")]
    [Range(0.05f, 0.80f)] public float chairOccupiedOverlapThreshold = 0.18f;

    [Range(0.05f, 0.80f)] public float tableOccupiedOverlapThreshold = 0.12f;


    private enum OccupancyMode
    {
        None,
        ChairFree,
        TableFree
    }

    private OccupancyMode occupancyMode = OccupancyMode.None;
    private Queue<bool> occupancyVotes = new Queue<bool>();
    private bool occupancyIntroSpoken = false;
    private bool occupancyResolved = false;
    private Rect currentCandidateRect01;

    private class RejectedTargetMemory
    {
        public Rect rect;
        public float until;
    }

    private readonly List<RejectedTargetMemory> rejectedTargets = new List<RejectedTargetMemory>();

    // =========================
    // GOOGLE response models
    // =========================
    [Serializable] private class GoogleAnnotateRoot { public GoogleResponse[] responses; }
    [Serializable]
    private class GoogleResponse
    {
        public GoogleLocalizedObjectAnnotation[] localizedObjectAnnotations;
        public GoogleLabelAnnotation[] labelAnnotations;
        public GoogleTextAnnotation[] textAnnotations;
        public GoogleError error;
    }

    [Serializable] private class GoogleError { public int code; public string message; public string status; }

    [Serializable]
    private class GoogleLocalizedObjectAnnotation
    {
        public string name;
        public float score;
        public GoogleBoundingPoly boundingPoly;
    }

    [Serializable]
    private class GoogleBoundingPoly
    {
        public GoogleNormalizedVertex[] normalizedVertices;
        public GoogleVertex[] vertices;
    }

    [Serializable]
    private class GoogleNormalizedVertex
    {
        public float x;
        public float y;
    }

    [Serializable]
    private class GoogleLabelAnnotation
    {
        public string description;
        public float score;
    }


    private void Awake()
    {
        if (captureManager == null) captureManager = FindObjectOfType<PhotoCaptureManager>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        if (uiAnimator == null) uiAnimator = FindObjectOfType<SpeechUIAnimator>(true);
        if (translator == null) translator = FindObjectOfType<AzureTranslator>(true);

        if (headCamera == null && Camera.main != null) headCamera = Camera.main.transform;

        TryLoadSecretsConfigIfNeeded();
        BuildUrls();

        if (enableBeep)
        {
            if (beepSource == null)
            {
                var go = new GameObject("ObjectFinderBeepSource");
                go.transform.SetParent(transform, false);
                beepSource = go.AddComponent<AudioSource>();
            }

            beepSource.playOnAwake = false;
            beepSource.loop = false;
            beepSource.spatialBlend = spatialBeep ? 1f : 0f;
            beepSource.rolloffMode = AudioRolloffMode.Logarithmic;
            beepSource.minDistance = 0.15f;
            beepSource.maxDistance = 10f;
            beepSource.volume = beepVolume;

            if (beepClip == null)
                beepClip = GenerateBeepClip();
        }
    }

    private void BuildUrls()
    {
        // Azure
        analyzeUrl =
            $"{endpoint.TrimEnd('/')}/vision/v3.2/analyze" +
            $"?visualFeatures=Objects,Tags&language={language}&model-version=latest";

        // Google
        googleUrl = $"https://vision.googleapis.com/v1/images:annotate?key={googleApiKey}";
    }
    public void RebuildUrlsAfterSecretsInjected()
    {
        BuildUrls();

        Debug.Log(
            "[ObjectFinder] URLs rebuilt after secrets. " +
            "AzureConfigured=" + IsAzureConfigured() +
            ", GoogleKeySet=" + !string.IsNullOrWhiteSpace(googleApiKey)
        );
    }

    private bool IsAzureConfigured()
    {
        if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("CHEIA")) return false;
        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    public void StartFind(string targetRomanian)
    {
        if (string.IsNullOrWhiteSpace(targetRomanian)) return;

        StopFind(silent: true);

        if (captureManager == null || tts == null)
        {
            Debug.LogError("[ObjectFinder] Missing deps (captureManager/tts).");
            return;
        }

        TryLoadSecretsConfigIfNeeded();
        BuildUrls();

      
        if (provider == Provider.AzureVision)
        {
            if (!IsAzureConfigured())
            {
                uiAnimator?.ShowError("Eroare: Azure Vision nu este configurat (cheie/endpoint).");
                tts?.Speak("Eroare: Azure Vision nu este configurat.");
                return;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(googleApiKey) || googleApiKey.Contains("PUT_GOOGLE"))
            {
                uiAnimator?.ShowError("Eroare: Google Vision API Key nu este setat.");
                tts?.Speak("Eroare: Google Vision API Key nu este setat.");
                return;
            }
        }

        if (overrideCaptureResolutionWhileSearching)
        {
            prevMaxW = captureManager.maxWidth;
            prevMaxH = captureManager.maxHeight;
            captureManager.maxWidth = finderMaxWidth;
            captureManager.maxHeight = finderMaxHeight;
        }

        if (overrideJpgQualityWhileSearching)
        {
            prevJpgQ = captureManager.jpgQuality;
            captureManager.jpgQuality = finderJpgQuality;
        }
        targetRaw = targetRomanian;
        targetKey = NormalizeRo(targetRomanian);

      
        if (targetKey == "geam") targetKey = "fereastra";
        if (targetKey == "fereastră") targetKey = "fereastra";

        occupancyMode = OccupancyMode.None;

        if (targetKey.Contains("scaun") && (targetKey.Contains("liber") || targetKey.Contains("libera")))
        {
            occupancyMode = OccupancyMode.ChairFree;
            targetKey = "scaun";
        }
        else if ((targetKey.Contains("masa") || targetKey.Contains("birou")) &&
                 (targetKey.Contains("liber") || targetKey.Contains("libera")))
        {
            occupancyMode = OccupancyMode.TableFree;
            targetKey = "masa";
        }

        occupancyVotes.Clear();
        occupancyIntroSpoken = false;
        occupancyResolved = false;
        currentCandidateRect01 = new Rect();
        rejectedTargets.Clear();



        wantedLabelsEn = GetExpectedEnglishLabels(targetKey);

        // Translation for unknown targets (NEW, safe)
        translationReady = true;
        findToken++;
        int myToken = findToken;

        if (autoTranslateUnknownTargets && !IsKnownTarget(targetKey))
        {
            if (translator != null)
            {
                translationReady = false;
                StartCoroutine(TranslateTargetRoToEn(myToken));
            }
            else
            {
                wantedLabelsEn = BuildWantedEnglish(targetKey);
            }
        }

        running = true;
        requestInFlight = false;

        startTime = Time.time;
        nextAnalyzeTime = Time.time;
        nextBeepTime = Time.time;

        announcedFound = false;
        announcedTagOnly = false;

        lastStrength01 = 0f;
        lastSide = Side.Center;
        hasBoundingBox = false;

        nextDebugUiTime = Time.time;

        lastSeenTime = -999f;
        lastLostAnnounceTime = -999f;
        lastCloseAnnounceTime = -999f;
        lostAnnouncedForCurrentLoss = false;
        searchProgressSpokenOnce = false;

        lastDirectionSpeakTime = -999f;
        lastSpokenDirection = Side.Center;

        nextAzureBoxFallbackTime = -999f;
        lastNoBoxSpeakTime = -999f;
        lastSearchProgressSpeakTime = Time.time;
        lastVisionErrorSpeakTime = -999f;
        consecutiveCaptureFails = 0;
        consecutiveVisionFails = 0;

        string displayTarget = GetRomanianDisplayName(targetKey, targetRomanian);

        uiAnimator?.ShowProcessing(
            $"Caut: {displayTarget}. Mișcă încet capul stânga-dreapta. {WhenIFindObjectText(targetKey)}, urmează sunetul."
        );
        if (preferPhoneControlInstructions)
        {
            tts?.Speak(
                $"Caut {displayTarget}. Mișcă încet capul stânga-dreapta. " +
                "Când ajungi la obiect, apasă lung pe ecranul telefonului pentru a opri funcția."
            );
        }
        else
        {
            tts?.Speak(
             $"Caut {displayTarget}. Mișcă încet capul stânga-dreapta. " +
             $"{WhenIFindObjectText(targetKey)}, urmează sunetul. " +
             "Spune AM GĂSIT ca să opresc căutarea, sau spune STOP ori ridică palma."
            );
        }
    }

    public void StopFind(bool silent = false)
    {
        if (!running) return;

        // Oprește imediat orice mesaj vocal vechi din căutare.
        tts?.StopNow();

        running = false;
        requestInFlight = false;

        targetKey = null;
        targetRaw = null;
        wantedLabelsEn = null;

        translationReady = true;
        findToken++;

        lastStrength01 = 0f;
        hasBoundingBox = false;

        StopAllCoroutines();

        if (overrideCaptureResolutionWhileSearching)
        {
            if (prevMaxW > 0) captureManager.maxWidth = prevMaxW;
            if (prevMaxH > 0) captureManager.maxHeight = prevMaxH;
        }

        if (overrideJpgQualityWhileSearching)
        {
            if (prevJpgQ > 0) captureManager.jpgQuality = prevJpgQ;
        }

        if (!silent)
        {
            uiAnimator?.ShowSuccess("Am oprit căutarea.");
            tts?.Speak("Am oprit căutarea.");
        }
        occupancyMode = OccupancyMode.None;
        occupancyVotes.Clear();
        occupancyIntroSpoken = false;
        occupancyResolved = false;
        currentCandidateRect01 = new Rect();
        rejectedTargets.Clear();

    }

    private void Update()
    {
        if (!running) return;
        if (!translationReady) return;

        if (!announcedFound && maxSearchSeconds > 0f && Time.time - startTime > maxSearchSeconds)
        {
            if (announcedTagOnly)
            {
                uiAnimator?.ShowError("Nu am putut localiza precis. Încearcă mai aproape și mișcă încet capul.");
                tts?.Speak("Nu am putut localiza precis. Mișcă încet capul din nou.");
            }
            else
            {
                uiAnimator?.ShowError("Nu am găsit obiectul. Rotește încet capul din nou spre stânga și dreapta.");
                tts?.Speak("Nu am găsit obiectul. Rotește încet capul din nou spre stânga și dreapta.");
            }

            StopFind(silent: true);
            return;
        }
        if (!announcedFound && !requestInFlight)
        {
            SpeakSearchProgressIfNeeded();
        }

        if (announcedFound && lastSeenTime > 0f && Time.time - lastSeenTime > lostAfterSeconds && lastStrength01 < 0.05f)
        {
            if (!lostAnnouncedForCurrentLoss)
            {
                lostAnnouncedForCurrentLoss = true;
                lastLostAnnounceTime = Time.time;

                uiAnimator?.ShowProcessing("Am pierdut obiectul. Mișcă încet capul stânga-dreapta.");
                tts?.Speak("Am pierdut obiectul. Mișcă încet capul stânga-dreapta.");
            }
        }

        if (!requestInFlight && Time.time >= nextAnalyzeTime)
        {
            nextAnalyzeTime = Time.time + analyzeInterval;
            requestInFlight = true;

            captureManager.TakePhoto(bytes =>
            {
                if (!running) { requestInFlight = false; return; }
                if (!translationReady) { requestInFlight = false; return; }

                if (bytes == null || bytes.Length < 2000)
                {
                    requestInFlight = false;
                    consecutiveCaptureFails++;

                    if (consecutiveCaptureFails >= 2)
                    {
                        SpeakVisionErrorIfNeeded("Nu pot captura imaginea pentru căutare. Verifică permisiunea camerei și încearcă din nou.");
                    }

                    return;
                }

                consecutiveCaptureFails = 0;
                StartCoroutine(Analyze(bytes));
            });
        }

        if (enableBeep && hasBoundingBox && lastStrength01 > 0.01f && Time.time >= nextBeepTime)
        {
            float interval = Mathf.Lerp(beepMaxInterval, beepMinInterval, lastStrength01);
            nextBeepTime = Time.time + interval;
            PlayBeep(lastSide);
        }
    }

    private IEnumerator Analyze(byte[] bytes)
    {
        try
        {
            if (provider == Provider.AzureVision)
                yield return StartCoroutine(AnalyzeAzure(bytes));
            else
                yield return StartCoroutine(AnalyzeGoogle(bytes));
        }
        finally
        {
            requestInFlight = false;
        }
    }

    // =========================================================
    // AZURE ANALYZE
    // =========================================================
    private IEnumerator AnalyzeAzure(byte[] bytes)
    {
        using (UnityWebRequest req = new UnityWebRequest(analyzeUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            yield return req.SendWebRequest();
            if (!running) yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                consecutiveVisionFails++;

                Debug.LogWarning($"[ObjectFinder][Azure] FAIL {req.responseCode} {req.error}");

                if (req.responseCode == 400 || req.responseCode == 401 || req.responseCode == 403)
                {
                    SpeakVisionErrorIfNeeded("Azure Vision nu răspunde corect. Verifică cheia Azure Vision și endpoint-ul.");
                }
                else
                {
                    SpeakVisionErrorIfNeeded("Am probleme cu analiza imaginii prin Azure Vision.");
                }

                yield break;
            }

            string json = req.downloadHandler.text;
            if (logJson) Debug.Log("[ObjectFinder][Azure] JSON: " + json);
            if (string.IsNullOrWhiteSpace(json))
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                yield break;
            }

            if (!TryParseAzure(json, out var hit, out string compactDebug, out bool tagOnlyHit))
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                yield break;
            }

            ApplyHit(hit, compactDebug, tagOnlyHit);
        }
    }

    // =========================================================
    // GOOGLE ANALYZE
    // =========================================================
    private IEnumerator AnalyzeGoogle(byte[] bytes)
    {
        string base64 = Convert.ToBase64String(bytes);

        string features;

        if (targetKey == "semn iesire")
        {
            features =
                "["
                + "{\"type\":\"OBJECT_LOCALIZATION\",\"maxResults\":10},"
                + "{\"type\":\"TEXT_DETECTION\",\"maxResults\":10},"
                + "{\"type\":\"LABEL_DETECTION\",\"maxResults\":10}"
                + "]";
        }
        else
        {
            features =
                useLabelFallback
                    ? "[{\"type\":\"OBJECT_LOCALIZATION\",\"maxResults\":10},{\"type\":\"LABEL_DETECTION\",\"maxResults\":10}]"
                    : "[{\"type\":\"OBJECT_LOCALIZATION\",\"maxResults\":10}]";
        }

        string bodyJson =
            "{"
            + "\"requests\":[{"
            + "\"image\":{\"content\":\"" + base64 + "\"},"
            + "\"features\":" + features
            + "}]"
            + "}";

        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

        using (UnityWebRequest req = new UnityWebRequest(googleUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = Mathf.Max(10, timeoutSeconds);

            req.SetRequestHeader("Content-Type", "application/json; charset=utf-8");

            yield return req.SendWebRequest();
            if (!running) yield break;

            if (req.result != UnityWebRequest.Result.Success)
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                consecutiveVisionFails++;

                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.LogWarning($"[ObjectFinder][Google] FAIL {req.responseCode} {req.error} body={body}");

                if (req.responseCode == 400 || req.responseCode == 401 || req.responseCode == 403)
                {
                    SpeakVisionErrorIfNeeded("Google Vision nu răspunde corect. Verifică cheia Google Vision API și dacă API-ul este activ.");
                }
                else
                {
                    SpeakVisionErrorIfNeeded("Am probleme cu analiza imaginii. Verifică internetul și serviciul Google Vision.");
                }

                yield break;
            }

            string json = req.downloadHandler.text;
            if (logJson) Debug.Log("[ObjectFinder][Google] JSON: " + json);

            if (string.IsNullOrWhiteSpace(json))
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                yield break;
            }

            if (!TryParseGoogle(json, out var hit, out string compactDebug, out bool labelOnlyHit))
            {
                lastStrength01 = 0f;
                hasBoundingBox = false;
                yield break;
            }

            // 1) dacă e label-only (fără box), dă feedback vocal rar (nu spam)
            if (labelOnlyHit && speakWhenNoBox && !announcedFound)
            {
                if (Time.time - lastNoBoxSpeakTime > noBoxSpeakCooldown)
                {
                    lastNoBoxSpeakTime = Time.time;
                    tts?.Speak($"{PossibleDetectionText(targetKey)}, dar nu am direcție. Mișcă încet capul și apropie-te.");
                }
            }

            // 2) încearcă fallback Azure doar când nu există box din Google
            if (labelOnlyHit && useAzureBoxFallbackWhenGoogleHasNoBox && Time.time >= nextAzureBoxFallbackTime)
            {
                nextAzureBoxFallbackTime = Time.time + azureBoxFallbackCooldown;

                bool gotBox = false;
                yield return StartCoroutine(TryAzureBoxFallback(bytes, ok => gotBox = ok));

                if (gotBox)
                    yield break; // Azure a aplicat hit cu box => beep + ghidare completă
            }

            // 3) aplică rezultatul Google (box sau label-only)
            ApplyHit(hit, compactDebug, labelOnlyHit);
        }
    }

    // =========================================================
    // Azure fallback (bbox only) when Google has no box
    // =========================================================
    private IEnumerator TryAzureBoxFallback(byte[] bytes, Action<bool> onDone)
    {
        onDone?.Invoke(false);

        if (!IsAzureConfigured())
            yield break;

        using (UnityWebRequest req = new UnityWebRequest(analyzeUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            yield return req.SendWebRequest();
            if (!running) yield break;

            if (req.result != UnityWebRequest.Result.Success)
                yield break;

            string json = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            if (!TryParseAzure(json, out var hit, out string dbg, out bool tagOnly))
                yield break;

            // aplicăm doar dacă avem bbox -> beep / direcție
            if (hit.found && hit.hasBox)
            {
                ApplyHit(hit, "[Fallback Azure BOX] " + dbg, tagOnlyHit: false);
                onDone?.Invoke(true);
            }
        }
    }

 
    private void ApplyHit(HitInfo hit, string compactDebug, bool tagOnlyHit)
    {
        hasBoundingBox = hit.hasBox;
        lastStrength01 = Mathf.Clamp01(hit.strength01);
        lastSide = hit.side;

        if (showCompactDebug && Time.time >= nextDebugUiTime && !announcedFound)
        {
            nextDebugUiTime = Time.time + debugUiInterval;
            uiAnimator?.ShowProcessing(compactDebug);
        }

     
        if (occupancyMode != OccupancyMode.None && hit.found && hit.hasBox && !occupancyResolved)
        {
            currentCandidateRect01 = hit.targetRect01;

            if (!occupancyIntroSpoken)
            {
                occupancyIntroSpoken = true;
                uiAnimator?.ShowProcessing("Verific dacă locul este liber.");
                tts?.Speak("Verific dacă locul este liber.");
            }
            if (hit.occupancyEvaluated)
            {
                // Dacă într-un cadru vedem clar obiect/persoană PE scaun,
                // nu mai așteptăm voturi. Respinge imediat scaunul.
                if (!hit.looksFree)
                {
                    RememberRejectedTarget(currentCandidateRect01);
                    occupancyVotes.Clear();
                    currentCandidateRect01 = new Rect();

                    uiAnimator?.ShowProcessing("Acest scaun pare ocupat. Caut altul.");
                    tts?.Speak("Acest scaun pare ocupat. Caut altul.");

                    hasBoundingBox = false;
                    lastStrength01 = 0f;
                    return;
                }

                occupancyVotes.Enqueue(true);

                while (occupancyVotes.Count > occupancyCheckFrames)
                    occupancyVotes.Dequeue();

                if (occupancyVotes.Count >= occupancyCheckFrames)
                {
                    int freeVotes = occupancyVotes.Count(v => v);

                    if (freeVotes >= occupancyFreeVotesNeeded)
                    {
                        occupancyResolved = true;
                        announcedFound = true;
                        lastSeenTime = Time.time;
                        lostAnnouncedForCurrentLoss = false;

                        uiAnimator?.ShowSuccess("Am găsit un scaun liber. Urmează sunetul.");
                        tts?.Speak("Am găsit un scaun liber. Urmează sunetul.");

                        if (hit.area01 >= closeAreaThreshold01 &&
                            Time.time - lastCloseAnnounceTime > closeAnnounceCooldown)
                        {
                            lastCloseAnnounceTime = Time.time;

                            string where =
                                hit.side == Side.Left ? "ușor în stânga" :
                                hit.side == Side.Right ? "ușor în dreapta" :
                                "în față";

                            uiAnimator?.ShowSuccess("Scaunul liber este foarte aproape, " + where + ".");
                            tts?.Speak("Scaunul liber este foarte aproape, " + where + ".");
                        }

                        return;
                    }
                }
            }

            return;
        }


        if (hit.found && hit.hasBox)
        {
            lastSeenTime = Time.time;
            lostAnnouncedForCurrentLoss = false;

            if (!announcedFound)
            {
                announcedFound = true;

                if (targetKey == "semn iesire")
                {
                    uiAnimator?.ShowSuccess("Am detectat calea de ieșire. Urmează sunetul.");
                    tts?.Speak("Am detectat calea de ieșire. Urmează sunetul.");
                }
                else
                {
                    string obj = GetRomanianDisplayName(targetKey, targetRaw);
                    uiAnimator?.ShowSuccess($"Am găsit {obj}. Urmează sunetul.");
                    tts?.Speak($"Am găsit {obj}. Urmează sunetul.");
                }

                SpeakDirectionGuidanceIfNeeded(hit, force: true);
            }
            else
            {
                SpeakDirectionGuidanceIfNeeded(hit, force: false);
            }

            if (hit.area01 >= closeAreaThreshold01)
            {
                if (Time.time - lastCloseAnnounceTime > closeAnnounceCooldown)
                {
                    lastCloseAnnounceTime = Time.time;

                    string where =
                        hit.side == Side.Left ? "ușor în stânga" :
                        hit.side == Side.Right ? "ușor în dreapta" :
                        "în față";

                    uiAnimator?.ShowSuccess("Este foarte aproape, " + where + ".");
                    tts?.Speak("Este foarte aproape, " + where + ".");
                }
            }
        }

        if (tagOnlyHit && !announcedTagOnly && !announcedFound)
        {
            announcedTagOnly = true;

            if (!announceFoundOnlyWhenHasBox)
            {
                uiAnimator?.ShowSuccess("Cred că e în zonă, dar nu am direcție. Mișcă încet capul și apropie-te.");
                tts?.Speak("Cred că e în zonă, dar nu am direcție. Mișcă încet capul și apropie-te.");
            }
            else
            {
                uiAnimator?.ShowProcessing("Posibil detectat, dar nu am direcție.");
            }
        }
    }

    private struct HitInfo
    {
        public bool found;
        public bool hasBox;
        public float strength01;
        public Side side;
        public float area01;

        public Rect targetRect01;

        public bool occupancyEvaluated;
        public bool looksFree;
    }

    // =========================================================
    // AZURE PARSE
    // =========================================================
    private bool TryParseAzure(string json, out HitInfo hit, out string compactDebug, out bool tagOnlyHit)
    {
        hit = default;
        compactDebug = "Caut...";
        tagOnlyHit = false;

        AzureVisionRoot root;
        try { root = JsonUtility.FromJson<AzureVisionRoot>(json); }
        catch { return false; }

        string objDbg = "";
        if (root?.objects != null && root.objects.Length > 0)
        {
            var topO = root.objects
                .Where(o => o != null)
                .OrderByDescending(o => o.confidence)
                .Take(2)
                .Select(o => $"{(o.@object ?? "?")}({o.confidence:0.00})");
            objDbg = string.Join(", ", topO);
        }

        string tagDbg = "";
        if (root?.tags != null && root.tags.Length > 0)
        {
            var topT = root.tags
                .Where(t => t != null)
                .OrderByDescending(t => t.confidence)
                .Take(2)
                .Select(t => $"{(t.name ?? "?")}({t.confidence:0.00})");
            tagDbg = string.Join(", ", topT);
        }

        compactDebug = $"Caut {targetKey} | Obj: {objDbg} | Tag: {tagDbg}";
        // Mod special pentru semn de ieșire: dacă găsim și ușa asociată, ghidăm spre ușă
        if (targetKey == "semn iesire")
        {
            if (TryFindAzureDoorNearExitSign(
                root?.objects,
                captureManager != null ? captureManager.LastWidth : 0,
                captureManager != null ? captureManager.LastHeight : 0,
                out Rect exitDoorRect01,
                out Side exitDoorSide,
                out float exitDoorStrength01,
                out float exitDoorArea01))
            {
                hit.found = true;
                hit.hasBox = true;
                hit.side = exitDoorSide;
                hit.strength01 = exitDoorStrength01;
                hit.area01 = exitDoorArea01;
                hit.targetRect01 = exitDoorRect01;

                compactDebug = $"Caut ieșirea | Ușa asociată semnului EXIT este {(exitDoorSide == Side.Left ? "stânga" : exitDoorSide == Side.Right ? "dreapta" : "centru")}";
                return true;
            }
        }
        var objHit = FindBestAzureObject(
        root?.objects,
        captureManager != null ? captureManager.LastWidth : 0,
        captureManager != null ? captureManager.LastHeight : 0
 );

        if (objHit.found)
        {
            hit.found = true;
            hit.hasBox = true;
            hit.side = objHit.side;
            hit.strength01 = objHit.strength01;
            hit.area01 = objHit.area01;
            hit.targetRect01 = objHit.rect01;

            if (occupancyMode != OccupancyMode.None)
            {
                hit.occupancyEvaluated = true;
                hit.looksFree = EvaluateAzureOccupancy(root?.objects, objHit.rect01);
            }

            return true;
        }

        var tagHit = FindBestAzureTag(root?.tags);
        if (tagHit.found)
        {
            tagOnlyHit = true;
            hit.found = true;
            hit.hasBox = false;
            hit.side = Side.Center;
            hit.strength01 = Mathf.Clamp01(tagHit.confidence01 * 0.25f);
            hit.area01 = 0f;
            return true;
        }

        return false;
    }

    private (bool found, Side side, float strength01, float area01, Rect rect01) FindBestAzureObject(AzureVisionObject[] objs, int imgW, int imgH)
    {
        if (objs == null || objs.Length == 0)
            return (false, Side.Center, 0f, 0f, new Rect());

        if (string.IsNullOrWhiteSpace(targetKey))
            return (false, Side.Center, 0f, 0f, new Rect());

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float bestScore = 0f;
        AzureVisionObject bestObj = null;
        float bestArea01 = 0f;
        Rect bestRect01 = new Rect();

        foreach (var o in objs)
        {
            if (o == null || o.rectangle == null)
                continue;

            if (o.confidence < confMin)
                continue;

            string name = (o.@object ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w)))
                continue;

            float area01 = 0f;
            if (imgW > 0 && imgH > 0)
            {
                float area = o.rectangle.w * o.rectangle.h;
                area01 = Mathf.Clamp01(area / (imgW * imgH));
            }

            Rect rect01 = RectFromAzure(o.rectangle, imgW, imgH);

            // dacă folosești memoria temporară pentru scaune / mese respinse
            if (IsRejectedTargetRect(rect01))
                continue;

            float score = (o.confidence * 0.80f) + (area01 * 0.20f);

            if (logMatches)
            {
                Debug.Log($"[ObjectFinder][Azure] match obj={name} conf={o.confidence:0.00} area01={area01:0.00} score={score:0.00}");
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestObj = o;
                bestArea01 = area01;
                bestRect01 = rect01;
            }
        }

        if (bestObj == null)
            return (false, Side.Center, 0f, 0f, new Rect());

        float nx = Mathf.Clamp01(bestRect01.center.x);

        Side side =
            nx < 0.40f ? Side.Left :
            nx > 0.60f ? Side.Right :
            Side.Center;

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        float strength01 = Mathf.Clamp01(bestScore * 0.65f + center01 * 0.35f);

        return (true, side, strength01, bestArea01, bestRect01);
    }

    private (bool found, float confidence01) FindBestAzureTag(AzureVisionTag[] tags)
    {
        if (tags == null || tags.Length == 0) return (false, 0f);
        if (string.IsNullOrWhiteSpace(targetKey)) return (false, 0f);

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float best = 0f;
        foreach (var t in tags)
        {
            if (t == null) continue;
            if (t.confidence < confMin) continue;

            string name = (t.name ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w))) continue;

            best = Mathf.Max(best, t.confidence);
        }

        return best > 0f ? (true, Mathf.Clamp01(best)) : (false, 0f);
    }

    // =========================================================
    // GOOGLE PARSE
    // =========================================================
    private bool TryParseGoogle(string json, out HitInfo hit, out string compactDebug, out bool labelOnlyHit)
    {
        hit = default;
        compactDebug = "Caut...";
        labelOnlyHit = false;

        GoogleAnnotateRoot root;
        try { root = JsonUtility.FromJson<GoogleAnnotateRoot>(json); }
        catch { return false; }

        if (root == null || root.responses == null || root.responses.Length == 0)
            return false;

        var r = root.responses[0];
        if (r != null && r.error != null && !string.IsNullOrWhiteSpace(r.error.message))
        {
            Debug.LogWarning($"[ObjectFinder][Google] API error: {r.error.code} {r.error.status} {r.error.message}");
            return false;
        }

        string objDbg = "";
        if (r?.localizedObjectAnnotations != null && r.localizedObjectAnnotations.Length > 0)
        {
            var topO = r.localizedObjectAnnotations
                .Where(o => o != null)
                .OrderByDescending(o => o.score)
                .Take(2)
                .Select(o => $"{(o.name ?? "?")}({o.score:0.00})");
            objDbg = string.Join(", ", topO);
        }

        string labDbg = "";
        if (r?.labelAnnotations != null && r.labelAnnotations.Length > 0)
        {
            var topL = r.labelAnnotations
                .Where(l => l != null)
                .OrderByDescending(l => l.score)
                .Take(2)
                .Select(l => $"{(l.description ?? "?")}({l.score:0.00})");
            labDbg = string.Join(", ", topL);
        }

        compactDebug = $"Caut {targetKey} | Obj: {objDbg} | Label: {labDbg}";
        // Mod special pentru semn de ieșire: dacă găsim și ușa asociată, ghidăm spre ușă
        if (targetKey == "semn iesire")
        {
            int imgW = captureManager != null ? captureManager.LastWidth : 0;
            int imgH = captureManager != null ? captureManager.LastHeight : 0;

            // 1) Întâi: semnul EXIT / IESIRE din text
            if (TryFindGoogleExitText(
                r?.textAnnotations,
                imgW,
                imgH,
                out Rect signRect01,
                out Side signSide,
                out float signStrength01,
                out float signArea01))
            {
                // 2) Doar după ce avem semnul, căutăm ușa apropiată lui
                if (TryFindGoogleDoorNearExitText(
                    r?.localizedObjectAnnotations,
                    signRect01,
                    out Rect exitDoorRect01,
                    out Side exitDoorSide,
                    out float exitDoorStrength01,
                    out float exitDoorArea01))
                {
                    hit.found = true;
                    hit.hasBox = true;
                    hit.side = exitDoorSide;
                    hit.strength01 = exitDoorStrength01;
                    hit.area01 = exitDoorArea01;
                    hit.targetRect01 = exitDoorRect01;

                    compactDebug = $"Caut ieșirea | Am găsit semnul EXIT și ușa este {(exitDoorSide == Side.Left ? "stânga" : exitDoorSide == Side.Right ? "dreapta" : "centru")}";
                    return true;
                }

                // dacă nu avem ușa, ghidăm totuși către semn
                hit.found = true;
                hit.hasBox = true;
                hit.side = signSide;
                hit.strength01 = signStrength01;
                hit.area01 = signArea01;
                hit.targetRect01 = signRect01;

                compactDebug = $"Caut ieșirea | Am găsit semnul EXIT {(signSide == Side.Left ? "în stânga" : signSide == Side.Right ? "în dreapta" : "în centru")}";
                return true;
            }

            // IMPORTANT:
            // dacă nu există semn detectat, NU vrem să tratăm simpla ușă ca ieșire.
            // Așa evităm confuzia între "usa" și "exit".
            return false;
        }
        var objHit = FindBestGoogleObject(r?.localizedObjectAnnotations);
        if (objHit.found)
        {
            hit.found = true;
            hit.hasBox = true;
            hit.side = objHit.side;
            hit.strength01 = objHit.strength01;
            hit.area01 = objHit.area01;
            hit.targetRect01 = objHit.rect01;

            if (occupancyMode != OccupancyMode.None)
            {
                hit.occupancyEvaluated = true;
                hit.looksFree = EvaluateGoogleOccupancy(r?.localizedObjectAnnotations, objHit.rect01);
            }

            return true;
        }

        if (useLabelFallback)
        {
            var labelHit = FindBestGoogleLabel(r?.labelAnnotations);
            if (labelHit.found)
            {
                labelOnlyHit = true;
                hit.found = true;
                hit.hasBox = false;
                hit.side = Side.Center;
                hit.strength01 = Mathf.Clamp01(labelHit.confidence01 * 0.25f);
                hit.area01 = 0f;
                return true;
            }
        }

        return false;
    }

    private (bool found, Side side, float strength01, float area01, Rect rect01) FindBestGoogleObject(GoogleLocalizedObjectAnnotation[] objs)
    {
        if (objs == null || objs.Length == 0) return (false, Side.Center, 0f, 0f, new Rect());
        if (string.IsNullOrWhiteSpace(targetKey)) return (false, Side.Center, 0f, 0f, new Rect());

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float bestScore = 0f;
        GoogleLocalizedObjectAnnotation bestObj = null;
        float bestArea01 = 0f;
        float bestCenterX = 0.5f;
        Rect bestRect01 = new Rect();

        foreach (var o in objs)
        {
            if (o == null) continue;
            if (o.score < confMin) continue;

            string name = (o.name ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w))) continue;

            if (o.boundingPoly == null || o.boundingPoly.normalizedVertices == null || o.boundingPoly.normalizedVertices.Length == 0)
                continue;

            Rect rect01 = RectFromGoogle(o.boundingPoly);
            if (IsRejectedTargetRect(rect01))
                continue;

            float area01 = Mathf.Clamp01(rect01.width * rect01.height);
            float centerX = Mathf.Clamp01(rect01.center.x);

            float score = (o.score * 0.80f) + (area01 * 0.20f);

            if (logMatches)
                Debug.Log($"[ObjectFinder][Google] match obj={name} conf={o.score:0.00} area01={area01:0.00} score={score:0.00}");

            if (score > bestScore)
            {
                bestScore = score;
                bestObj = o;
                bestArea01 = area01;
                bestCenterX = centerX;
                bestRect01 = rect01;
            }
        }

        if (bestObj == null) return (false, Side.Center, 0f, 0f, new Rect());

        Side side = bestCenterX < 0.40f ? Side.Left : (bestCenterX > 0.60f ? Side.Right : Side.Center);

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(bestCenterX - 0.5f) / 0.5f);
        float strength01 = Mathf.Clamp01(bestScore * 0.65f + center01 * 0.35f);

        return (true, side, strength01, bestArea01, bestRect01);
    }

    private (bool found, float confidence01) FindBestGoogleLabel(GoogleLabelAnnotation[] labels)
    {
        if (labels == null || labels.Length == 0) return (false, 0f);
        if (string.IsNullOrWhiteSpace(targetKey)) return (false, 0f);

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float best = 0f;
        foreach (var l in labels)
        {
            if (l == null) continue;
            if (l.score < confMin) continue;

            string name = (l.description ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w))) continue;

            best = Mathf.Max(best, l.score);
        }

        return best > 0f ? (true, Mathf.Clamp01(best)) : (false, 0f);
    }

    // =========================================================
    // Translator coroutine (RO->EN for unknown targets)
    // =========================================================
    private IEnumerator TranslateTargetRoToEn(int myToken)
    {
        uiAnimator?.ShowProcessing($"Traduc obiectul: {targetRaw}...");

        string en = null;
        yield return StartCoroutine(translator.TranslateRomanianToEnglish(targetRaw, s => en = s));

        if (!running || myToken != findToken) yield break;

        if (string.IsNullOrWhiteSpace(en))
        {
            wantedLabelsEn = BuildWantedEnglish(targetKey);
            translationReady = true;
            yield break;
        }

        wantedLabelsEn = BuildWantedEnglish(en);
        translationReady = true;

        if (showCompactDebug && wantedLabelsEn != null && wantedLabelsEn.Length > 0)
            uiAnimator?.ShowProcessing($"Caut {targetRaw} (EN: {wantedLabelsEn[0]})");
    }

    private string[] GetWantedLabels()
    {
        if (wantedLabelsEn != null && wantedLabelsEn.Length > 0)
            return wantedLabelsEn;

        return GetExpectedEnglishLabels(targetKey);
    }

    // =========================================================
    // Helpers (same behavior)
    // =========================================================
    private float GetMinConfidenceForTarget(string target)
    {
        if (target == "usa")
            return 0.22f;

        if (target == "semn iesire")
            return 0.22f;

        if (target == "fereastra")
            return 0.22f;

        if (target == "cos gunoi")
            return 0.22f;

        if (target == "dulap")
            return 0.22f;

        if (target == "masa")
            return 0.26f;
        if (targetKey == "ochelari")
            return 0.24f;

        if (target == "scaun")
            return Mathf.Min(minConfidenceMedium, 0.28f);

        if (target == "telefon" || target == "laptop" || target == "sticla" || target == "cana" ||
            target == "televizor" || target == "pat" || target == "canapea")
            return minConfidenceMedium;

        return minConfidenceDefault;
    }

    private static bool MatchesWanted(string detectedLabel, string wanted)
    {
        if (string.IsNullOrWhiteSpace(detectedLabel) || string.IsNullOrWhiteSpace(wanted))
            return false;

        detectedLabel = detectedLabel.Trim().ToLowerInvariant();
        wanted = wanted.Trim().ToLowerInvariant();

        if (detectedLabel == wanted)
            return true;

        if (detectedLabel.Contains(wanted) || wanted.Contains(detectedLabel))
            return true;

        var detectedTokens = detectedLabel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var wantedTokens = wanted.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

        if (wantedTokens.All(w => detectedTokens.Contains(w)))
            return true;

        if (detectedTokens.Any(t => t == wanted))
            return true;

        return false;
    }
    private void PlayBeep(Side side)
    {
        if (beepSource == null || beepClip == null || headCamera == null) return;

        beepSource.volume = beepVolume;
        beepSource.spatialBlend = spatialBeep ? 1f : 0f;

        if (spatialBeep)
        {
            Vector3 pos = headCamera.position + headCamera.forward * beepDistanceFromHead;
            if (side == Side.Left) pos += -headCamera.right * beepSideOffset;
            else if (side == Side.Right) pos += headCamera.right * beepSideOffset;
            beepSource.transform.position = pos;
        }
        else
        {
            beepSource.transform.position = headCamera.position;
        }

        beepSource.PlayOneShot(beepClip);
    }

    private static AudioClip GenerateBeepClip()
    {
        const int sampleRate = 48000;
        float duration = 0.07f;
        int samples = Mathf.CeilToInt(sampleRate * duration);

        float freq = 1050f;
        float[] data = new float[samples];
        int fadeSamples = Mathf.CeilToInt(sampleRate * 0.01f);

        for (int i = 0; i < samples; i++)
        {
            float t = i / (float)sampleRate;
            float s = Mathf.Sin(2f * Mathf.PI * freq * t);

            float fade = 1f;
            if (i < fadeSamples) fade = i / (float)fadeSamples;
            else if (i > samples - fadeSamples) fade = (samples - i) / (float)fadeSamples;

            data[i] = s * fade * 0.55f;
        }

        var clip = AudioClip.Create("FinderBeep", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static string[] GetExpectedEnglishLabels(string targetRo)
    {
        switch (targetRo)
        {
            case "telefon":
                return new[] { "cell phone", "cellphone", "mobile phone", "smartphone", "iphone", "phone" };

            case "laptop":
                return new[] { "laptop", "notebook", "notebook computer", "computer" };

            case "sticla":
                return new[] { "bottle", "water bottle", "plastic bottle", "glass bottle" };

            case "cana":
                return new[] { "cup", "mug", "coffee cup", "teacup" };

            case "usa":
                return new[]
                {
                    "door",
                    "doorway",
                    "entrance",
                    "entryway",
                    "front door",
                    "interior door",
                    "exit door",
                    "room door",
                    "gate"
    };

            case "scaun":
                return new[] { "chair", "armchair", "seat" };

            case "scaun liber":
                return new[] { "chair", "armchair", "seat" };

            case "masa":
                return new[]
                {
                    "table",
                    "desk",
                    "dining table",
                    "coffee table",
                    "work table",
                    "office table",
                    "kitchen table",
                    "furniture"
                };

            case "masa libera":
                return new[]
                {
                    "table",
                    "desk",
                    "dining table",
                    "coffee table",
                    "work table",
                    "office table",
                    "kitchen table",
                    "furniture"
    };


            case "televizor":
                return new[] { "tv", "television", "television set", "monitor", "screen", "display" };

            case "pat":
                return new[] { "bed", "bunk bed" };

            case "canapea":
                return new[] { "couch", "sofa", "loveseat" };

            case "dulap":
                return new[]
                {
                    "wardrobe",
                    "closet",
                    "cabinet",
                    "cupboard",
                    "armoire",
                    "storage cabinet",
                    "kitchen cabinet",
                    "drawer",
                    "chest of drawers",
                    "dresser",
                    "shelf",
                    "bookcase",
                    "bookshelf",
                    "storage"
            };

            case "fereastra":
                return new[]
                {
                    "window",
                    "windowpane",
                    "glass window",
                    "pane",
                    "glass",
                    "casement window",
                    "bay window",
                    "window blind",
                    "curtain"
    };
            case "semn iesire":
                return new[]
                {
                "exit sign",
                "emergency exit sign",
                "emergency exit",
                "fire exit",
                "exit board",
                "exit",
                "sign",
                "exit door",
                "emergency door"
    };

            case "cos gunoi":
                return new[]
                {
                    "trash can",
                    "garbage can",
                    "waste bin",
                    "bin",
                    "trash bin",
                    "recycling bin",
                    "wastebasket",
                    "rubbish bin",
                    "litter bin",
                    "dustbin",
                    "waste container",
                    "container"
    };
            case "ochelari":
                return new[]
                {
                    "glasses",
                    "eyeglasses",
                    "spectacles",
                    "sunglasses",
                    "goggles",
                    "reading glasses",
                    "vision care",
                    "personal care"
                };

            default:
                return new[] { targetRo };
        }
    }

    private static bool IsKnownTarget(string targetRo)
    {
        switch (targetRo)
        {
            case "telefon":
            case "laptop":
            case "sticla":
            case "cana":
            case "usa":
            case "scaun":
            case "scaun liber":
            case "masa":
            case "masa libera":
            case "televizor":
            case "pat":
            case "canapea":
            case "dulap":
            case "fereastra":
            case "semn iesire":
            case "cos gunoi":
            case "ochelari":
                return true;
            default:
                return false;
        }
    }

    private static string[] BuildWantedEnglish(string en)
    {
        if (string.IsNullOrWhiteSpace(en)) return new string[0];

        en = en.ToLowerInvariant().Trim();
        en = en.Replace(".", "").Replace("!", "").Replace("?", "").Replace("\"", "").Replace("'", "");

        var list = new List<string>();

        void Add(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            s = s.Trim();
            if (!list.Contains(s)) list.Add(s);
        }

        foreach (var p in en.Split(new[] { ',', ';', '/', '|' }, StringSplitOptions.RemoveEmptyEntries))
            Add(p.Trim());

        var words = en.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 1) Add(words[words.Length - 1]);
        if (words.Length >= 2) Add(words[words.Length - 2] + " " + words[words.Length - 1]);

        if (list.Count > 0)
        {
            string w = list[0];
            if (w.EndsWith("s") && w.Length > 3) Add(w.TrimEnd('s'));
            else if (!w.EndsWith("s")) Add(w + "s");
        }

        return list.Distinct().ToArray();
    }

    private static string NormalizeRo(string input)
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
        return sb.ToString().Normalize(NormalizationForm.FormC).Trim();
    }
    private static Rect RectFromAzure(AzureRectangle r, int imgW, int imgH)
    {
        if (r == null || imgW <= 0 || imgH <= 0) return new Rect(0, 0, 0, 0);

        float x = Mathf.Clamp01(r.x / (float)imgW);
        float y = Mathf.Clamp01(r.y / (float)imgH);
        float w = Mathf.Clamp01(r.w / (float)imgW);
        float h = Mathf.Clamp01(r.h / (float)imgH);

        return new Rect(x, y, w, h);
    }

    private static Rect RectFromGoogle(GoogleBoundingPoly poly)
    {
        if (poly == null || poly.normalizedVertices == null || poly.normalizedVertices.Length == 0)
            return new Rect(0, 0, 0, 0);

        float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;

        foreach (var v in poly.normalizedVertices)
        {
            minX = Mathf.Min(minX, v.x);
            minY = Mathf.Min(minY, v.y);
            maxX = Mathf.Max(maxX, v.x);
            maxY = Mathf.Max(maxY, v.y);
        }

        return new Rect(
            Mathf.Clamp01(minX),
            Mathf.Clamp01(minY),
            Mathf.Clamp01(maxX - minX),
            Mathf.Clamp01(maxY - minY)
        );
    }

    private static float OverlapOnTarget(Rect target, Rect other)
    {
        float xMin = Mathf.Max(target.xMin, other.xMin);
        float yMin = Mathf.Max(target.yMin, other.yMin);
        float xMax = Mathf.Min(target.xMax, other.xMax);
        float yMax = Mathf.Min(target.yMax, other.yMax);

        float w = Mathf.Max(0f, xMax - xMin);
        float h = Mathf.Max(0f, yMax - yMin);
        float inter = w * h;
        float targetArea = Mathf.Max(0.0001f, target.width * target.height);

        return inter / targetArea;
    }
    private bool IsBlockingObjectForCurrentOccupancy(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;

        label = label.Trim().ToLowerInvariant();

        if (occupancyMode == OccupancyMode.ChairFree)
        {
            return label.Contains("person")
                || label.Contains("human")
                || label.Contains("man")
                || label.Contains("woman")
                || label.Contains("child")

                || label.Contains("backpack")
                || label.Contains("handbag")
                || label.Contains("bag")
                || label.Contains("suitcase")
                || label.Contains("purse")
                || label.Contains("tote")

                || label.Contains("laptop")
                || label.Contains("notebook")
                || label.Contains("computer")
                || label.Contains("tablet")
                || label.Contains("phone")
                || label.Contains("cell phone")
                || label.Contains("mobile phone")

                || label.Contains("book")
                || label.Contains("magazine")
                || label.Contains("paper")
                || label.Contains("box")
                || label.Contains("package")
                || label.Contains("container")

                || label.Contains("bottle")
                || label.Contains("cup")
                || label.Contains("plate")
                || label.Contains("food")

                || label.Contains("clothing")
                || label.Contains("coat")
                || label.Contains("jacket")
                || label.Contains("hat")
                || label.Contains("scarf")
                || label.Contains("blanket")
                || label.Contains("pillow")

                || label.Contains("remote")
                || label.Contains("keyboard")
                || label.Contains("mouse")
                || label.Contains("toy")
                || label.Contains("object");
        }

        if (occupancyMode == OccupancyMode.TableFree)
        {
            return label.Contains("person")
                || label.Contains("human")
                || label.Contains("man")
                || label.Contains("woman")
                || label.Contains("child")
                || label.Contains("backpack")
                || label.Contains("handbag")
                || label.Contains("bag")
                || label.Contains("laptop")
                || label.Contains("book")
                || label.Contains("phone")
                || label.Contains("cell phone")
                || label.Contains("mobile phone")
                || label.Contains("bottle")
                || label.Contains("cup")
                || label.Contains("plate")
                || label.Contains("box")
                || label.Contains("package")
                || label.Contains("suitcase")
                || label.Contains("food")
                || label.Contains("container");
        }

        return false;
    }
    private void CleanupRejectedTargets()
    {
        rejectedTargets.RemoveAll(r => Time.time > r.until);
    }

    private bool IsRejectedTargetRect(Rect rect01)
    {
        CleanupRejectedTargets();

        foreach (var r in rejectedTargets)
        {
            float overlap = OverlapOnTarget(rect01, r.rect);
            if (overlap >= 0.45f)
                return true;
        }

        return false;
    }

    private void RememberRejectedTarget(Rect rect01)
    {
        rejectedTargets.Add(new RejectedTargetMemory
        {
            rect = rect01,
            until = Time.time + rejectedTargetMemorySeconds
        });
    }
    private bool EvaluateAzureOccupancy(AzureVisionObject[] objs, Rect targetRect01)
    {
        if (occupancyMode == OccupancyMode.None) return true;

        if (objs == null || objs.Length == 0)
            return true;

        foreach (var o in objs)
        {
            if (o == null || o.rectangle == null) continue;

            string name = (o.@object ?? "").Trim().ToLowerInvariant();

            // Ignorăm scaunul însuși și mobilierul apropiat.
            if (IsSeatOrNearbyFurnitureLabel(name))
                continue;

            Rect other = RectFromAzure(o.rectangle, captureManager.LastWidth, captureManager.LastHeight);

            if (occupancyMode == OccupancyMode.ChairFree)
            {
                if (IsPersonLabel(name))
                {
                    if (PersonActuallyOnChair(targetRect01, other))
                        return false;

                    continue;
                }

                // Pentru scaun: dacă Azure a localizat orice obiect real pe scaun,
                // îl considerăm ocupat. Nu ne bazăm doar pe nume.
                if (IsBlockingObjectForCurrentOccupancy(name) || IsGenericPhysicalObjectLabel(name))
                {
                    if (ObjectActuallyOnSeat(targetRect01, other))
                        return false;
                }
            }
            else if (occupancyMode == OccupancyMode.TableFree)
            {
                if (!IsBlockingObjectForCurrentOccupancy(name))
                    continue;

                float overlap = OverlapOnTarget(targetRect01, other);

                if (overlap >= tableOccupiedOverlapThreshold)
                    return false;
            }
        }

        return true;
    }
    private bool EvaluateGoogleOccupancy(GoogleLocalizedObjectAnnotation[] objs, Rect targetRect01)
    {
        if (occupancyMode == OccupancyMode.None) return true;

        if (objs == null || objs.Length == 0)
            return true;

        foreach (var o in objs)
        {
            if (o == null || o.boundingPoly == null) continue;

            string name = (o.name ?? "").Trim().ToLowerInvariant();

            if (IsSeatOrNearbyFurnitureLabel(name))
                continue;

            Rect other = RectFromGoogle(o.boundingPoly);

            if (occupancyMode == OccupancyMode.ChairFree)
            {
                if (IsPersonLabel(name))
                {
                    if (PersonActuallyOnChair(targetRect01, other))
                        return false;

                    continue;
                }

                if (IsBlockingObjectForCurrentOccupancy(name) || IsGenericPhysicalObjectLabel(name))
                {
                    if (ObjectActuallyOnSeat(targetRect01, other))
                        return false;
                }
            }
            else if (occupancyMode == OccupancyMode.TableFree)
            {
                if (!IsBlockingObjectForCurrentOccupancy(name))
                    continue;

                float overlap = OverlapOnTarget(targetRect01, other);

                if (overlap >= tableOccupiedOverlapThreshold)
                    return false;
            }
        }

        return true;
    }
    private static bool LabelMatchesAny(string detectedLabel, params string[] wanted)
    {
        if (string.IsNullOrWhiteSpace(detectedLabel)) return false;

        string d = detectedLabel.Trim().ToLowerInvariant();

        foreach (var w in wanted)
        {
            if (string.IsNullOrWhiteSpace(w)) continue;

            string ww = w.Trim().ToLowerInvariant();

            if (d == ww) return true;
            if (d.Contains(ww) || ww.Contains(d)) return true;

            var dt = d.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var wt = ww.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (wt.All(x => dt.Contains(x)))
                return true;
        }

        return false;
    }
    private static float RectCenterDistance(Rect a, Rect b)
    {
        return Vector2.Distance(a.center, b.center);
    }
    private bool TryFindAzureDoorNearExitSign(
    AzureVisionObject[] objs,
    int imgW,
    int imgH,
    out Rect bestDoorRect01,
    out Side bestDoorSide,
    out float bestDoorStrength01,
    out float bestDoorArea01)
    {
        bestDoorRect01 = new Rect();
        bestDoorSide = Side.Center;
        bestDoorStrength01 = 0f;
        bestDoorArea01 = 0f;

        if (objs == null || objs.Length == 0 || imgW <= 0 || imgH <= 0)
            return false;

        float signConfMin = GetMinConfidenceForTarget("semn iesire");
        float doorConfMin = GetMinConfidenceForTarget("usa");

        var exitCandidates = new List<(AzureVisionObject obj, Rect rect01, float area01)>();
        var doorCandidates = new List<(AzureVisionObject obj, Rect rect01, float area01)>();

        foreach (var o in objs)
        {
            if (o == null || o.rectangle == null) continue;

            string name = (o.@object ?? "").Trim().ToLowerInvariant();
            Rect rect01 = RectFromAzure(o.rectangle, imgW, imgH);
            float area01 = Mathf.Clamp01(rect01.width * rect01.height);

            if (o.confidence >= signConfMin &&
                LabelMatchesAny(name, "exit sign", "emergency exit", "fire exit", "exit"))
            {
                exitCandidates.Add((o, rect01, area01));
            }

            if (o.confidence >= doorConfMin &&
                LabelMatchesAny(name, "door", "doorway", "entrance", "interior door", "front door"))
            {
                doorCandidates.Add((o, rect01, area01));
            }
        }

        if (exitCandidates.Count == 0 || doorCandidates.Count == 0)
            return false;

        float bestScore = -999f;
        Rect chosenDoor = new Rect();
        float chosenArea01 = 0f;

        foreach (var sign in exitCandidates)
        {
            foreach (var door in doorCandidates)
            {
                float dist = RectCenterDistance(sign.rect01, door.rect01);

                // bonus dacă ușa e sub semn sau aproape sub el
                float verticalBonus = 0f;
                if (door.rect01.center.y > sign.rect01.center.y)
                    verticalBonus = 0.12f;

                float overlapX = Mathf.Max(0f,
                    Mathf.Min(sign.rect01.xMax, door.rect01.xMax) - Mathf.Max(sign.rect01.xMin, door.rect01.xMin));

                float alignBonus = overlapX > 0.05f ? 0.12f : 0f;

                float score =
                    (door.obj.confidence * 0.50f) +
                    (sign.obj.confidence * 0.20f) +
                    (door.area01 * 0.15f) +
                    ((1f - Mathf.Clamp01(dist / 0.8f)) * 0.15f) +
                    verticalBonus +
                    alignBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    chosenDoor = door.rect01;
                    chosenArea01 = door.area01;
                }
            }
        }

        if (bestScore < 0f)
            return false;

        float nx = Mathf.Clamp01(chosenDoor.center.x);

        bestDoorSide =
            nx < 0.40f ? Side.Left :
            nx > 0.60f ? Side.Right :
            Side.Center;

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        bestDoorStrength01 = Mathf.Clamp01(0.65f + center01 * 0.35f);
        bestDoorArea01 = chosenArea01;
        bestDoorRect01 = chosenDoor;

        return true;
    }

    private bool TryFindGoogleDoorNearExitSign(
    GoogleLocalizedObjectAnnotation[] objs,
    out Rect bestDoorRect01,
    out Side bestDoorSide,
    out float bestDoorStrength01,
    out float bestDoorArea01)
    {
        bestDoorRect01 = new Rect();
        bestDoorSide = Side.Center;
        bestDoorStrength01 = 0f;
        bestDoorArea01 = 0f;

        if (objs == null || objs.Length == 0)
            return false;

        float signConfMin = GetMinConfidenceForTarget("semn iesire");
        float doorConfMin = GetMinConfidenceForTarget("usa");

        var exitCandidates = new List<(GoogleLocalizedObjectAnnotation obj, Rect rect01, float area01)>();
        var doorCandidates = new List<(GoogleLocalizedObjectAnnotation obj, Rect rect01, float area01)>();

        foreach (var o in objs)
        {
            if (o == null || o.boundingPoly == null) continue;

            string name = (o.name ?? "").Trim().ToLowerInvariant();
            Rect rect01 = RectFromGoogle(o.boundingPoly);
            float area01 = Mathf.Clamp01(rect01.width * rect01.height);

            if (o.score >= signConfMin &&
                LabelMatchesAny(name, "exit sign", "emergency exit", "fire exit", "exit"))
            {
                exitCandidates.Add((o, rect01, area01));
            }

            if (o.score >= doorConfMin &&
                LabelMatchesAny(name, "door", "doorway", "entrance", "interior door", "front door"))
            {
                doorCandidates.Add((o, rect01, area01));
            }
        }

        if (exitCandidates.Count == 0 || doorCandidates.Count == 0)
            return false;

        float bestScore = -999f;
        Rect chosenDoor = new Rect();
        float chosenArea01 = 0f;

        foreach (var sign in exitCandidates)
        {
            foreach (var door in doorCandidates)
            {
                float dist = RectCenterDistance(sign.rect01, door.rect01);

                float verticalBonus = 0f;
                if (door.rect01.center.y > sign.rect01.center.y)
                    verticalBonus = 0.12f;

                float overlapX = Mathf.Max(0f,
                    Mathf.Min(sign.rect01.xMax, door.rect01.xMax) - Mathf.Max(sign.rect01.xMin, door.rect01.xMin));

                float alignBonus = overlapX > 0.05f ? 0.12f : 0f;

                float score =
                    (door.obj.score * 0.50f) +
                    (sign.obj.score * 0.20f) +
                    (door.area01 * 0.15f) +
                    ((1f - Mathf.Clamp01(dist / 0.8f)) * 0.15f) +
                    verticalBonus +
                    alignBonus;

                if (score > bestScore)
                {
                    bestScore = score;
                    chosenDoor = door.rect01;
                    chosenArea01 = door.area01;
                }
            }
        }

        if (bestScore < 0f)
            return false;

        float nx = Mathf.Clamp01(chosenDoor.center.x);

        bestDoorSide =
            nx < 0.40f ? Side.Left :
            nx > 0.60f ? Side.Right :
            Side.Center;

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        bestDoorStrength01 = Mathf.Clamp01(0.65f + center01 * 0.35f);
        bestDoorArea01 = chosenArea01;
        bestDoorRect01 = chosenDoor;

        return true;
    }
    [Serializable]
    private class GoogleTextAnnotation
    {
        public string description;
        public GoogleBoundingPoly boundingPoly;
    }

    [Serializable]
    private class GoogleVertex
    {
        public int x;
        public int y;
    }
 
    private bool IsExitText(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;

        string n = " " + NormalizeRo(s) + " ";

        return n.Contains(" exit ")
            || n.Contains(" iesire ")
            || n.Contains(" emergency exit ")
            || n.Contains(" fire exit ");
    }
    private bool TryFindGoogleExitText(
    GoogleTextAnnotation[] texts,
    int imgW,
    int imgH,
    out Rect signRect01,
    out Side signSide,
    out float signStrength01,
    out float signArea01)
    {
        signRect01 = new Rect();
        signSide = Side.Center;
        signStrength01 = 0f;
        signArea01 = 0f;

        if (texts == null || texts.Length == 0)
            return false;

        float bestScore = -999f;
        Rect bestRect = new Rect();

        for (int i = 0; i < texts.Length; i++)
        {
            var t = texts[i];
            if (t == null || string.IsNullOrWhiteSpace(t.description) || t.boundingPoly == null)
                continue;

            // textAnnotations[0] este de obicei tot textul din imagine
            // pentru semn vrem cuvintele / grupurile individuale
            if (i == 0 && texts.Length > 1)
                continue;

            if (!IsExitText(t.description))
                continue;

            Rect rect01 = RectFromGoogleTextPoly(t.boundingPoly, imgW, imgH);
            if (rect01.width <= 0.001f || rect01.height <= 0.001f)
                continue;

            float area01 = Mathf.Clamp01(rect01.width * rect01.height);
            float centerX = Mathf.Clamp01(rect01.center.x);
            float center01 = 1f - Mathf.Clamp01(Mathf.Abs(centerX - 0.5f) / 0.5f);

            string textNorm = NormalizeRo(t.description);

            float keywordBonus = 0f;
            if (textNorm == "exit" || textNorm == "iesire")
                keywordBonus = 0.20f;
            else if (textNorm.Contains("emergency exit") || textNorm.Contains("fire exit"))
                keywordBonus = 0.16f;
            else if (textNorm.Contains("exit") || textNorm.Contains("iesire"))
                keywordBonus = 0.12f;

            // penalizăm bbox-uri uriașe care probabil vin din text prost agregat
            float hugePenalty = area01 > 0.35f ? 0.20f : 0f;

            float score =
                (area01 * 0.35f) +
                (center01 * 0.25f) +
                keywordBonus -
                hugePenalty;

            if (score > bestScore)
            {
                bestScore = score;
                bestRect = rect01;
                signArea01 = area01;
            }
        }

        if (bestScore < 0f)
            return false;

        float nx = Mathf.Clamp01(bestRect.center.x);

        signSide =
            nx < 0.40f ? Side.Left :
            nx > 0.60f ? Side.Right :
            Side.Center;

        float centerFactor = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        signStrength01 = Mathf.Clamp01(0.58f + centerFactor * 0.27f + signArea01 * 0.15f);
        signRect01 = bestRect;

        return true;
    }
    private bool TryFindGoogleDoorNearExitText(
    GoogleLocalizedObjectAnnotation[] objs,
    Rect signRect01,
    out Rect bestDoorRect01,
    out Side bestDoorSide,
    out float bestDoorStrength01,
    out float bestDoorArea01)
    {
        bestDoorRect01 = new Rect();
        bestDoorSide = Side.Center;
        bestDoorStrength01 = 0f;
        bestDoorArea01 = 0f;

        if (objs == null || objs.Length == 0)
            return false;

        float doorConfMin = GetMinConfidenceForTarget("usa");

        float bestScore = -999f;
        Rect chosenDoor = new Rect();
        float chosenArea01 = 0f;

        foreach (var o in objs)
        {
            if (o == null || o.boundingPoly == null) continue;
            if (o.score < doorConfMin) continue;

            string name = (o.name ?? "").Trim().ToLowerInvariant();
            if (!LabelMatchesAny(name, "door", "doorway", "entrance", "interior door", "front door"))
                continue;

            Rect doorRect01 = RectFromGoogle(o.boundingPoly);
            float area01 = Mathf.Clamp01(doorRect01.width * doorRect01.height);

            float dist = RectCenterDistance(signRect01, doorRect01);

            float verticalBonus = 0f;
            if (doorRect01.center.y > signRect01.center.y)
                verticalBonus = 0.12f;

            float overlapX = Mathf.Max(0f,
                Mathf.Min(signRect01.xMax, doorRect01.xMax) - Mathf.Max(signRect01.xMin, doorRect01.xMin));

            float alignBonus = overlapX > 0.03f ? 0.12f : 0f;

            float score =
                (o.score * 0.50f) +
                (area01 * 0.20f) +
                ((1f - Mathf.Clamp01(dist / 0.85f)) * 0.18f) +
                verticalBonus +
                alignBonus;

            if (score > bestScore)
            {
                bestScore = score;
                chosenDoor = doorRect01;
                chosenArea01 = area01;
            }
        }

        if (bestScore < 0f)
            return false;

        float nx = Mathf.Clamp01(chosenDoor.center.x);

        bestDoorSide =
            nx < 0.40f ? Side.Left :
            nx > 0.60f ? Side.Right :
            Side.Center;

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        bestDoorStrength01 = Mathf.Clamp01(0.65f + center01 * 0.35f);
        bestDoorArea01 = chosenArea01;
        bestDoorRect01 = chosenDoor;

        return true;
    }
    private static Rect RectFromGoogleTextPoly(GoogleBoundingPoly poly, int imgW, int imgH)
    {
        if (poly == null)
            return new Rect(0, 0, 0, 0);

        // Preferăm normalizedVertices dacă există
        if (poly.normalizedVertices != null && poly.normalizedVertices.Length > 0)
        {
            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;

            foreach (var v in poly.normalizedVertices)
            {
                minX = Mathf.Min(minX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxX = Mathf.Max(maxX, v.x);
                maxY = Mathf.Max(maxY, v.y);
            }

            return new Rect(
                Mathf.Clamp01(minX),
                Mathf.Clamp01(minY),
                Mathf.Clamp01(maxX - minX),
                Mathf.Clamp01(maxY - minY)
            );
        }

        // Fallback pentru TEXT_DETECTION clasic: vertices în pixeli
        if (poly.vertices != null && poly.vertices.Length > 0 && imgW > 0 && imgH > 0)
        {
            float minX = imgW, minY = imgH, maxX = 0f, maxY = 0f;

            foreach (var v in poly.vertices)
            {
                minX = Mathf.Min(minX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxX = Mathf.Max(maxX, v.x);
                maxY = Mathf.Max(maxY, v.y);
            }

            return new Rect(
                Mathf.Clamp01(minX / imgW),
                Mathf.Clamp01(minY / imgH),
                Mathf.Clamp01((maxX - minX) / imgW),
                Mathf.Clamp01((maxY - minY) / imgH)
            );
        }

        return new Rect(0, 0, 0, 0);
    }
    private void TryLoadSecretsConfigIfNeeded()
    {
        try
        {
            var cfg = Resources.Load<SecretsConfig>("SecretsConfig");
            if (cfg == null)
            {
                Debug.LogWarning("[ObjectFinder] SecretsConfig nu a fost găsit în Resources.");
                return;
            }

            // Azure Vision
            if ((string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("CHEIA")) &&
                !string.IsNullOrWhiteSpace(cfg.visionKey))
            {
                subscriptionKey = cfg.visionKey;
            }

            if ((string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(cfg.visionEndpoint))
            {
                endpoint = cfg.visionEndpoint;
            }

            // Google Vision
            if ((string.IsNullOrWhiteSpace(googleApiKey) || googleApiKey.Contains("PUT_GOOGLE")) &&
                !string.IsNullOrWhiteSpace(cfg.googleVisionApiKey))
            {
                googleApiKey = cfg.googleVisionApiKey;
            }

            // Translator, dacă nu e legat în Inspector
            if (translator == null)
                translator = FindObjectOfType<AzureTranslator>(true);

            Debug.Log("[ObjectFinder] SecretsConfig verificat/injectat pentru ObjectFinder.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ObjectFinder] TryLoadSecretsConfigIfNeeded exception: " + e.Message);
        }
    }
    private void SpeakSearchProgressIfNeeded()
    {
        if (!speakSearchProgress) return;
        if (!running) return;
        if (announcedFound) return;
        if (searchProgressSpokenOnce) return;

        if (Time.time - startTime < 4.0f)
            return;

        searchProgressSpokenOnce = true;

        string obj = GetRomanianDisplayName(targetKey, targetRaw);
        string msg = $"Încă nu văd clar {obj}. Mișcă încet capul stânga-dreapta.";

        uiAnimator?.ShowProcessing(msg);

        if (tts != null && !tts.IsSpeaking())
            tts.Speak(msg);
    }

    private void SpeakVisionErrorIfNeeded(string msg)
    {
        if (!speakVisionErrors) return;
        if (!running) return;

        if (Time.time - lastVisionErrorSpeakTime < visionErrorSpeakCooldown)
            return;

        lastVisionErrorSpeakTime = Time.time;

        uiAnimator?.ShowError(msg);

        if (tts != null && !tts.IsSpeaking())
            tts.Speak(msg);
    }
    private string DirectionText(Side side)
    {
        if (side == Side.Left) return "în stânga";
        if (side == Side.Right) return "în dreapta";
        return "în față";
    }

    private void SpeakDirectionGuidanceIfNeeded(HitInfo hit, bool force = false)
    {
        if (!speakDirectionGuidance) return;
        if (!running) return;
        if (!hit.hasBox) return;

        bool sideChanged = hit.side != lastSpokenDirection;

        if (!force && !sideChanged && Time.time - lastDirectionSpeakTime < directionSpeakCooldown)
            return;

        lastDirectionSpeakTime = Time.time;
        lastSpokenDirection = hit.side;

        string msg = $"Obiectul căutat este {DirectionText(hit.side)}. Urmează sunetul.";

        uiAnimator?.ShowSuccess(msg);

        if (tts != null && !tts.IsSpeaking())
            tts.Speak(msg);
    }
    private string GetRomanianDisplayName(string key, string fallback)
    {
        key = NormalizeRo(key ?? "");
        fallback = fallback ?? "";

        switch (key)
        {
            case "scaun": return "scaun";
            case "masa": return "masă";
            case "masa libera": return "masă liberă";
            case "dulap": return "dulap";
            case "fereastra": return "fereastră";
            case "usa": return "ușă";
            case "cos gunoi": return "coș de gunoi";
            case "semn iesire": return "semn de ieșire";
            case "pat": return "pat";
            case "canapea": return "canapea";
            case "telefon": return "telefon";
            case "laptop": return "laptop";
            case "televizor": return "televizor";
            case "sticla": return "sticlă";
            case "cana": return "cană";
            case "ochelari": return "ochelari";
        }

        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        return key;
    }
    private bool IsNearOrOverlapsTarget(Rect targetRect01, Rect otherRect01, float overlapThreshold, bool isPerson)
    {
        float overlap = OverlapOnTarget(targetRect01, otherRect01);
        if (overlap >= overlapThreshold)
            return true;

        Rect expanded = ExpandRect01(
            targetRect01,
            isPerson ? 0.18f : 0.10f,
            isPerson ? 0.18f : 0.10f
        );

        if (expanded.Contains(otherRect01.center))
            return true;

        float dx = Mathf.Abs(targetRect01.center.x - otherRect01.center.x);
        float dy = Mathf.Abs(targetRect01.center.y - otherRect01.center.y);

        if (isPerson)
            return dx < 0.42f && dy < 0.48f;

        return dx < 0.25f && dy < 0.30f;
    }

    private Rect ExpandRect01(Rect r, float xPad, float yPad)
    {
        float xMin = Mathf.Clamp01(r.xMin - xPad);
        float yMin = Mathf.Clamp01(r.yMin - yPad);
        float xMax = Mathf.Clamp01(r.xMax + xPad);
        float yMax = Mathf.Clamp01(r.yMax + yPad);

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }
    private bool IsPersonLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;

        label = label.Trim().ToLowerInvariant();

        return label.Contains("person")
            || label.Contains("human")
            || label.Contains("man")
            || label.Contains("woman")
            || label.Contains("child");
    }

    private bool IsSeatOrNearbyFurnitureLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;

        label = label.Trim().ToLowerInvariant();

        // Acestea pot fi lângă scaun și NU trebuie să facă scaunul ocupat.
        return label == "chair"
            || label.Contains("chair")
            || label.Contains("seat")
            || label.Contains("table")
            || label.Contains("desk")
            || label.Contains("dining table")
            || label.Contains("coffee table")
            || label.Contains("furniture");
    }

    private Rect GetSeatZone(Rect chairRect01)
    {
        // Zona reală de interes: partea centrală a scaunului, unde ar sta o persoană/obiect.
        // Nu folosim tot bounding box-ul scaunului, ca să nu confundăm biroul de lângă el.
        float xPad = chairRect01.width * 0.12f;
        float topCut = chairRect01.height * 0.25f;
        float bottomCut = chairRect01.height * 0.05f;

        float xMin = Mathf.Clamp01(chairRect01.xMin + xPad);
        float xMax = Mathf.Clamp01(chairRect01.xMax - xPad);
        float yMin = Mathf.Clamp01(chairRect01.yMin + topCut);
        float yMax = Mathf.Clamp01(chairRect01.yMax - bottomCut);

        if (xMax <= xMin || yMax <= yMin)
            return chairRect01;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private bool ObjectActuallyOnSeat(Rect chairRect01, Rect objectRect01)
    {
        Rect seatZone = GetSeatZone(chairRect01);
        Rect chairCore = GetChairCoreZone(chairRect01);

        // 1) Dacă centrul obiectului este în scaun, este ocupat.
        if (seatZone.Contains(objectRect01.center) || chairCore.Contains(objectRect01.center))
            return true;

        // 2) Verificăm puncte utile ale obiectului.
        Vector2 bottomCenter = new Vector2(objectRect01.center.x, objectRect01.yMax);
        Vector2 topCenter = new Vector2(objectRect01.center.x, objectRect01.yMin);

        if (seatZone.Contains(bottomCenter) || seatZone.Contains(topCenter))
            return true;

        if (chairCore.Contains(bottomCenter) || chairCore.Contains(topCenter))
            return true;

        // 3) IMPORTANT pentru obiecte mici:
        // nu raportăm doar la aria scaunului, ci și la aria obiectului.
        float objectInsideSeat = OverlapOfObjectInsideTarget(seatZone, objectRect01);
        float objectInsideChair = OverlapOfObjectInsideTarget(chairCore, objectRect01);

        if (objectInsideSeat >= 0.20f)
            return true;

        if (objectInsideChair >= 0.30f)
            return true;

        // 4) Pentru obiecte mari, verificăm și cât ocupă din zona scaunului.
        float seatCovered = OverlapOnTarget(seatZone, objectRect01);
        if (seatCovered >= 0.04f)
            return true;

        return false;
    }

    private bool PersonActuallyOnChair(Rect chairRect01, Rect personRect01)
    {
        // Pentru persoană cerem suprapunere reală cu scaunul.
        // Nu e suficient să fie doar aproape, pentru că poate sta lângă birou.
        float overlapOnChair = OverlapOnTarget(chairRect01, personRect01);

        if (overlapOnChair >= 0.10f)
            return true;

        Rect seatZone = GetSeatZone(chairRect01);

        // Dacă centrul persoanei cade în zona scaunului, probabil este așezată.
        if (seatZone.Contains(personRect01.center))
            return true;

        // Dacă partea de jos a persoanei este peste zona scaunului, probabil este așezată.
        Vector2 lowerPoint = new Vector2(personRect01.center.x, personRect01.yMax);
        if (seatZone.Contains(lowerPoint))
            return true;

        return false;
    }
    private Rect GetChairCoreZone(Rect chairRect01)
    {
        // Zonă mai largă decât șezutul strict.
        // Ajută pentru obiecte mici detectate puțin deplasat pe scaun.
        float xPad = chairRect01.width * 0.04f;
        float yPad = chairRect01.height * 0.08f;

        float xMin = Mathf.Clamp01(chairRect01.xMin + xPad);
        float xMax = Mathf.Clamp01(chairRect01.xMax - xPad);
        float yMin = Mathf.Clamp01(chairRect01.yMin + yPad);
        float yMax = Mathf.Clamp01(chairRect01.yMax - yPad);

        if (xMax <= xMin || yMax <= yMin)
            return chairRect01;

        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    private float OverlapOfObjectInsideTarget(Rect targetRect01, Rect objectRect01)
    {
        float xMin = Mathf.Max(targetRect01.xMin, objectRect01.xMin);
        float yMin = Mathf.Max(targetRect01.yMin, objectRect01.yMin);
        float xMax = Mathf.Min(targetRect01.xMax, objectRect01.xMax);
        float yMax = Mathf.Min(targetRect01.yMax, objectRect01.yMax);

        float w = Mathf.Max(0f, xMax - xMin);
        float h = Mathf.Max(0f, yMax - yMin);

        float inter = w * h;
        float objectArea = Mathf.Max(0.0001f, objectRect01.width * objectRect01.height);

        return inter / objectArea;
    }
    private bool IsGenericPhysicalObjectLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;

        label = label.Trim().ToLowerInvariant();

        // Lucruri pe care NU vrem să le tratăm ca obiecte puse pe scaun.
        if (label.Contains("chair") ||
            label.Contains("seat") ||
            label.Contains("table") ||
            label.Contains("desk") ||
            label.Contains("furniture") ||
            label.Contains("wall") ||
            label.Contains("floor") ||
            label.Contains("ceiling") ||
            label.Contains("door") ||
            label.Contains("window"))
        {
            return false;
        }

        // Dacă Vision a localizat un obiect cu bounding box și nu este mobilier/fundal,
        // îl tratăm ca potențial obiect fizic pe scaun.
        return true;
    }
    private enum RoObjectGrammar
    {
        MasculineSingular,
        FeminineSingular,
        MasculinePlural,
        FemininePlural
    }

    private RoObjectGrammar GetRomanianGrammar(string key)
    {
        key = NormalizeRo(key ?? "");

        switch (key)
        {
            // Feminin singular: o găsesc
            case "sticla":
            case "cana":
            case "usa":
            case "fereastra":
            case "masa":
            case "masa libera":
            case "canapea":
                return RoObjectGrammar.FeminineSingular;

            // Plural masculin: îi găsesc
            case "ochelari":
                return RoObjectGrammar.MasculinePlural;

            // Masculin singular: îl găsesc
            case "scaun":
            case "pat":
            case "dulap":
            case "telefon":
            case "laptop":
            case "televizor":
            case "cos gunoi":
            case "semn iesire":
            default:
                return RoObjectGrammar.MasculineSingular;
        }
    }

    private string FindObjectPronounAccusative(string key)
    {
        switch (GetRomanianGrammar(key))
        {
            case RoObjectGrammar.FeminineSingular:
                return "o";

            case RoObjectGrammar.MasculinePlural:
                return "îi";

            case RoObjectGrammar.FemininePlural:
                return "le";

            case RoObjectGrammar.MasculineSingular:
            default:
                return "îl";
        }
    }

    private string WhenIFindObjectText(string key)
    {
        return $"Când {FindObjectPronounAccusative(key)} găsesc";
    }

    private string PossibleDetectionText(string key)
    {
        return $"{FirstUpper(FindObjectPronounAccusative(key))} detectez posibil în zonă";
    }

    private string FirstUpper(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        if (s.Length == 1)
            return s.ToUpperInvariant();

        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }






}