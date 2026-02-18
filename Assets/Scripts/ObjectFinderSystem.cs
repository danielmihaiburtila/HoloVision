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

    // =========================
    // Internal
    // =========================
    private bool running;
    private bool requestInFlight;

    private string targetKey;                 // normalized RO
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

    private enum Side { Left, Center, Right }

    private string analyzeUrl;   // Azure
    private string googleUrl;    // Google

    // =========================
    // AZURE response models
    // =========================
    [Serializable] private class AzureVisionRoot { public AzureVisionObject[] objects; public AzureVisionTag[] tags; }
    [Serializable] private class AzureVisionObject { public string @object; public float confidence; public AzureRectangle rectangle; }
    [Serializable] private class AzureRectangle { public int x; public int y; public int w; public int h; }
    [Serializable] private class AzureVisionTag { public string name; public float confidence; }

    // =========================
    // GOOGLE response models
    // =========================
    [Serializable] private class GoogleAnnotateRoot { public GoogleResponse[] responses; }

    [Serializable]
    private class GoogleResponse
    {
        public GoogleLocalizedObjectAnnotation[] localizedObjectAnnotations;
        public GoogleLabelAnnotation[] labelAnnotations;
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

        BuildUrls();

        // Provider config checks
        if (provider == Provider.AzureVision)
        {
            if (!IsAzureConfigured())
            {
                uiAnimator?.ShowError("Eroare: Azure Vision nu este configurat (cheie/endpoint).");
                tts?.Speak("Eroare: Azure Vision nu este configurat.");
                return;
            }
        }
        else // Google
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

        // Target init
        targetRaw = targetRomanian;
        targetKey = NormalizeRo(targetRomanian);

        // Default behavior for known targets (unchanged)
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

        nextAzureBoxFallbackTime = -999f;
        lastNoBoxSpeakTime = -999f;

        uiAnimator?.ShowProcessing($"Caut: {targetRomanian}. Îndreaptă privirea spre obiect și mișcă încet capul.");
        tts?.Speak($"Caut {targetRomanian}. Îndreaptă privirea spre obiect și mișcă încet capul.");
    }

    public void StopFind(bool silent = false)
    {
        if (!running) return;

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
    }

    private void Update()
    {
        if (!running) return;

        // Wait until translation finishes for unknown targets
        if (!translationReady) return;

        // timeout doar dacă NU am găsit încă
        if (!announcedFound && maxSearchSeconds > 0f && Time.time - startTime > maxSearchSeconds)
        {
            if (announcedTagOnly)
            {
                uiAnimator?.ShowError("Nu am putut localiza precis. Încearcă mai aproape și mișcă încet capul.");
                tts?.Speak("Nu am putut localiza precis. Încearcă mai aproape și mișcă încet capul.");
            }
            else
            {
                uiAnimator?.ShowError("Nu am găsit. Încearcă mai aproape și cu lumină mai bună.");
                tts?.Speak("Nu am găsit. Încearcă mai aproape și cu lumină mai bună.");
            }

            StopFind(silent: true);
            return;
        }

        // după ce a fost găsit: dacă îl pierdem din vedere
        if (announcedFound && lastSeenTime > 0f && Time.time - lastSeenTime > lostAfterSeconds && lastStrength01 < 0.05f)
        {
            if (Time.time - lastLostAnnounceTime > lostAnnounceCooldown)
            {
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

                if (bytes == null || bytes.Length < 2000) { requestInFlight = false; return; }
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
                Debug.LogWarning($"[ObjectFinder][Azure] FAIL {req.responseCode} {req.error}");
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

        string features =
            useLabelFallback
                ? "[{\"type\":\"OBJECT_LOCALIZATION\",\"maxResults\":10},{\"type\":\"LABEL_DETECTION\",\"maxResults\":10}]"
                : "[{\"type\":\"OBJECT_LOCALIZATION\",\"maxResults\":10}]";

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

                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.LogWarning($"[ObjectFinder][Google] FAIL {req.responseCode} {req.error} body={body}");
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
                    tts?.Speak("Îl detectez posibil în zonă, dar nu am direcție. Mișcă încet capul și apropie-te.");
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

    // =========================================================
    // Shared apply hit
    // =========================================================
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

        if (hit.found && hit.hasBox)
        {
            lastSeenTime = Time.time;

            if (!announcedFound)
            {
                announcedFound = true;
                uiAnimator?.ShowSuccess("Am găsit. Urmează sunetul.");
                tts?.Speak("Am găsit. Urmează sunetul.");
            }

            // ✅ “Foarte aproape” (cu direcție)
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
                // Dacă announceFoundOnlyWhenHasBox e ON, nu “stricăm” regula; UI rămâne informativ.
                uiAnimator?.ShowProcessing("Posibil detectat, dar nu am direcție. Îndreaptă camera mai direct spre obiect.");
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

        var objHit = FindBestAzureObject(root?.objects, captureManager != null ? captureManager.LastWidth : 0, captureManager != null ? captureManager.LastHeight : 0);
        if (objHit.found)
        {
            hit.found = true;
            hit.hasBox = true;
            hit.side = objHit.side;
            hit.strength01 = objHit.strength01;
            hit.area01 = objHit.area01;
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

    private (bool found, Side side, float strength01, float area01) FindBestAzureObject(AzureVisionObject[] objs, int imgW, int imgH)
    {
        if (objs == null || objs.Length == 0) return (false, Side.Center, 0f, 0f);
        if (string.IsNullOrWhiteSpace(targetKey)) return (false, Side.Center, 0f, 0f);

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float bestScore = 0f;
        AzureVisionObject bestObj = null;
        float bestArea01 = 0f;

        foreach (var o in objs)
        {
            if (o == null || o.rectangle == null) continue;
            if (o.confidence < confMin) continue;

            string name = (o.@object ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w))) continue;

            float area01 = 0f;
            if (imgW > 0 && imgH > 0)
            {
                float area = o.rectangle.w * o.rectangle.h;
                area01 = Mathf.Clamp01(area / (imgW * imgH));
            }

            float score = (o.confidence * 0.80f) + (area01 * 0.20f);

            if (logMatches)
                Debug.Log($"[ObjectFinder][Azure] match obj={name} conf={o.confidence:0.00} area01={area01:0.00} score={score:0.00}");

            if (score > bestScore)
            {
                bestScore = score;
                bestObj = o;
                bestArea01 = area01;
            }
        }

        if (bestObj == null) return (false, Side.Center, 0f, 0f);

        float cx = bestObj.rectangle.x + bestObj.rectangle.w * 0.5f;
        float nx = (imgW > 0) ? Mathf.Clamp01(cx / imgW) : 0.5f;

        Side side = nx < 0.40f ? Side.Left : (nx > 0.60f ? Side.Right : Side.Center);

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(nx - 0.5f) / 0.5f);
        float strength01 = Mathf.Clamp01(bestScore * 0.65f + center01 * 0.35f);

        return (true, side, strength01, bestArea01);
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

        var objHit = FindBestGoogleObject(r?.localizedObjectAnnotations);
        if (objHit.found)
        {
            hit.found = true;
            hit.hasBox = true;
            hit.side = objHit.side;
            hit.strength01 = objHit.strength01;
            hit.area01 = objHit.area01;
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

    private (bool found, Side side, float strength01, float area01) FindBestGoogleObject(GoogleLocalizedObjectAnnotation[] objs)
    {
        if (objs == null || objs.Length == 0) return (false, Side.Center, 0f, 0f);
        if (string.IsNullOrWhiteSpace(targetKey)) return (false, Side.Center, 0f, 0f);

        var wanted = GetWantedLabels();
        float confMin = GetMinConfidenceForTarget(targetKey);

        float bestScore = 0f;
        GoogleLocalizedObjectAnnotation bestObj = null;
        float bestArea01 = 0f;
        float bestCenterX = 0.5f;

        foreach (var o in objs)
        {
            if (o == null) continue;
            if (o.score < confMin) continue;

            string name = (o.name ?? "").Trim().ToLowerInvariant();
            if (!wanted.Any(w => MatchesWanted(name, w))) continue;

            if (o.boundingPoly == null || o.boundingPoly.normalizedVertices == null || o.boundingPoly.normalizedVertices.Length == 0)
                continue;

            float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
            foreach (var v in o.boundingPoly.normalizedVertices)
            {
                minX = Mathf.Min(minX, v.x);
                maxX = Mathf.Max(maxX, v.x);
                minY = Mathf.Min(minY, v.y);
                maxY = Mathf.Max(maxY, v.y);
            }

            float w = Mathf.Clamp01(maxX - minX);
            float h = Mathf.Clamp01(maxY - minY);
            float area01 = Mathf.Clamp01(w * h);
            float centerX = Mathf.Clamp01((minX + maxX) * 0.5f);

            float score = (o.score * 0.80f) + (area01 * 0.20f);

            if (logMatches)
                Debug.Log($"[ObjectFinder][Google] match obj={name} conf={o.score:0.00} area01={area01:0.00} score={score:0.00}");

            if (score > bestScore)
            {
                bestScore = score;
                bestObj = o;
                bestArea01 = area01;
                bestCenterX = centerX;
            }
        }

        if (bestObj == null) return (false, Side.Center, 0f, 0f);

        Side side = bestCenterX < 0.40f ? Side.Left : (bestCenterX > 0.60f ? Side.Right : Side.Center);

        float center01 = 1f - Mathf.Clamp01(Mathf.Abs(bestCenterX - 0.5f) / 0.5f);
        float strength01 = Mathf.Clamp01(bestScore * 0.65f + center01 * 0.35f);

        return (true, side, strength01, bestArea01);
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
        if (target == "telefon" || target == "laptop" || target == "sticla" || target == "cana"
            || target == "usa" || target == "televizor" || target == "pat" || target == "masa" || target == "canapea"
            || target == "dulap")
            return minConfidenceMedium;

        return minConfidenceDefault;
    }

    private static bool MatchesWanted(string detectedLabel, string wanted)
    {
        if (string.IsNullOrWhiteSpace(detectedLabel) || string.IsNullOrWhiteSpace(wanted)) return false;

        detectedLabel = detectedLabel.Trim().ToLowerInvariant();
        wanted = wanted.Trim().ToLowerInvariant();

        if (wanted.Contains(" "))
            return detectedLabel.Contains(wanted);

        if (detectedLabel == wanted) return true;

        var tokens = detectedLabel.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(t => t == wanted);
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
                return new[] { "door", "doorway", "entrance", "interior door", "front door" };
            case "scaun":
                return new[] { "chair", "armchair", "seat" };
            case "masa":
                return new[] { "table", "desk", "dining table", "coffee table", "work table" };
            case "televizor":
                return new[] { "tv", "television", "television set", "monitor", "screen", "display" };
            case "pat":
                return new[] { "bed", "bunk bed" };
            case "canapea":
                return new[] { "couch", "sofa", "loveseat" };
            case "dulap":
                return new[] { "wardrobe", "closet", "cabinet", "cupboard" };
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
            case "masa":
            case "televizor":
            case "pat":
            case "canapea":
            case "dulap":
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
}