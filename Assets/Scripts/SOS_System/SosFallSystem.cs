// SosFallSystem.cs
using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.XR;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
using Windows.Devices.Sensors;
#endif

public class SosFallSystem : MonoBehaviour
{
    [Header("Refs")]
    public VoiceCommandRouter router;
    public AzureTTS tts;
    private UWPSpeechRecognizer sosSpeech;

    [Header("Server (FLOW complet: event + decision => alertă aparținător)")]
    [Tooltip("Ex: http://192.168.0.105:8080  (FĂRĂ /api)")]
    public string serverBaseUrl = "http://192.168.0.103:8080";

    [Tooltip("Trebuie să fie același ca în caregiver app și WatchEventReceiver")]
    public string deviceId = "user1";

    [Header("Voice confirmation")]
    public float askTimeoutSeconds = 8f;
    [TextArea] public string askText = "Ești bine? Spune SUNT BINE sau AJUTOR.";
    [TextArea] public string confirmedOkText = "Bine. Mulțumesc.";
    [TextArea] public string sendingHelpText = "Trimit o alertă de urgență.";

    [Header("Auto detection (head height)")]
    public float calibrationSeconds = 2.0f;

    [Tooltip("Cât trebuie să scadă capul în fereastra de analiză ca să suspectăm cădere.")]
    public float fallHeightDropMeters = 0.40f;

    [Header("Robust drop window (prinde și căderi mai lente)")]
    public float dropWindowSeconds = 1.2f;
    [Range(20, 200)] public int maxSamples = 90;

    [Header("Low-hold confirm (după drop)")]
    public float lowHoldSeconds = 2.0f;
    public float lowMotionEpsilonMeters = 0.06f;

    [Header("IMPORTANT: Înălțimea irelevantă => NU folosim prag absolut Y")]
    public bool useAbsoluteLowY = false;
    public float absoluteLowHeadY = 1.10f;

    [Header("Optional IMU (dacă există pe platformă)")]
    public float sampleInterval = 0.05f;
    public float impactAccelThreshold = 16f; // m/s^2
    public float postImpactGraceSeconds = 0.6f;
    public float immobileSecondsRequired = 5f;
    public float immobileAccelEpsilon = 0.6f;
    public float immobileMaxWindowExtra = 6f;

    [Header("Debug")]
    public bool debugLogs = true;

    private bool sosActive = false;
    private bool awaitingResponse = false;
    private string sessionId;

    [Header("SOS speech timing")]
    [Tooltip("Mică pauză ca UWP Speech să treacă sigur în gramatica SOS înainte să vorbim.")]
    public float sosSpeechWarmupSeconds = 0.45f;

    private Coroutine askSpeakRoutine;

    // head baseline
    private float baselineHeadY = 0f;
    private bool baselineReady = false;
    private float calibrateEndTime = 0f;
    private float baselineAccum = 0f;
    private int baselineCount = 0;

    private Vector3 lastHeadPos = Vector3.zero;

    // XR
    private InputDevice headDevice;

    // pending event context (pentru a posta decision)
    private string pendingEventId = null;
    private string pendingDeviceId = null;
    private string pendingBaseUrl = null;

    [Header("Anti false positives")]
    [Tooltip("Declanșează doar dacă drop-ul s-a produs suficient de rapid (m/s).")]
    public float minVerticalSpeedMetersPerSec = 0.9f; // încearcă 0.8..1.2

    [Tooltip("Câte secunde verificăm viteza verticală după ce suspectăm drop.")]
    public float speedCheckWindowSeconds = 0.35f;

    private struct YSample { public float t; public float y; }
    private readonly System.Collections.Generic.List<YSample> _y =
        new System.Collections.Generic.List<YSample>(128);

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private Accelerometer uwpAccel;
    private bool accelSupported = false;
    private Vector3 lastAccel = Vector3.zero;
    private Vector3 currentAccel = Vector3.zero;
#endif

    [Serializable] private class EventResp { public bool ok; public string eventId; }

    private void Awake()
    {
        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        sessionId = Guid.NewGuid().ToString("N");
    }

    private void OnEnable()
    {
        headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        StartCalibration();

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        TryInitUwpAccelerometer();
#endif

        StartCoroutine(HeadFallLoop());

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        if (accelSupported) StartCoroutine(AccelLoop());
#endif
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        sosActive = false;
        awaitingResponse = false;
        pendingEventId = null;
        pendingDeviceId = null;
        pendingBaseUrl = null;
        _y.Clear();
    }

    private void StartCalibration()
    {
        baselineReady = false;
        baselineAccum = 0f;
        baselineCount = 0;
        calibrateEndTime = Time.time + Mathf.Max(0.5f, calibrationSeconds);

        var pos = GetHeadLocalPos();
        lastHeadPos = pos;

        _y.Clear();

        if (debugLogs) Debug.Log("[SOS] Calibration started.");
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private void TryInitUwpAccelerometer()
    {
        accelSupported = false;
        try
        {
            uwpAccel = Accelerometer.GetDefault();
            if (uwpAccel != null)
            {
                uwpAccel.ReportInterval = (uint)Mathf.Max(16, (int)(sampleInterval * 1000f));
                accelSupported = uwpAccel.GetCurrentReading() != null;

                if (debugLogs) Debug.Log("[SOS] UWP Accelerometer present=" + accelSupported);
                if (accelSupported)
                {
                    var r = uwpAccel.GetCurrentReading();
                    if (r != null)
                        lastAccel = new Vector3((float)r.AccelerationX, (float)r.AccelerationY, (float)r.AccelerationZ);
                }
            }
        }
        catch (Exception e)
        {
            if (debugLogs) Debug.LogWarning("[SOS] UWP accelerometer init exception: " + e);
        }
    }

    private bool TryReadAccelerationMs2(out Vector3 accelMs2)
    {
        accelMs2 = Vector3.zero;
        if (uwpAccel == null) return false;

        var r = uwpAccel.GetCurrentReading();
        if (r == null) return false;

        const float g = 9.80665f;
        accelMs2 = new Vector3((float)r.AccelerationX * g, (float)r.AccelerationY * g, (float)r.AccelerationZ * g);
        return true;
    }
#endif

    private Vector3 GetHeadLocalPos()
    {
        if (!headDevice.isValid) headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);

        if (headDevice.isValid && headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out var pos))
            return pos;

        return Camera.main != null ? Camera.main.transform.localPosition : Vector3.zero;
    }

    private IEnumerator HeadFallLoop()
    {
        while (enabled)
        {
            yield return null;
            if (sosActive) continue;

            var headPos = GetHeadLocalPos();
            float y = headPos.y;

            // baseline calibration
            if (!baselineReady)
            {
                if (Time.time <= calibrateEndTime)
                {
                    baselineAccum += y;
                    baselineCount++;
                }
                else
                {
                    baselineHeadY = (baselineCount > 0) ? (baselineAccum / baselineCount) : y;
                    baselineReady = true;
                    if (debugLogs) Debug.Log($"[SOS] Baseline ready: {baselineHeadY:0.00}m");
                }

                lastHeadPos = headPos;
                continue;
            }

            // robust sample window
            _y.Add(new YSample { t = Time.time, y = y });
            float minT = Time.time - Mathf.Max(0.2f, dropWindowSeconds);
            while (_y.Count > 0 && (_y[0].t < minT || _y.Count > maxSamples))
                _y.RemoveAt(0);

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int i = 0; i < _y.Count; i++)
            {
                float yy = _y[i].y;
                if (yy < minY) minY = yy;
                if (yy > maxY) maxY = yy;
            }

            float drop = (maxY - minY);
            // dacă maxY e mult sub baseline, probabil era deja aplecat/jos -> ignorăm
            if (maxY < baselineHeadY - 0.20f)
                continue;
            if (drop >= fallHeightDropMeters)
            {
                // verifică dacă a fost rapid
                float dt = _y[_y.Count - 1].t - _y[0].t;
                if (dt <= 0.001f) dt = 0.001f;

                // viteza aproximată pe interval (m/s)
                float verticalSpeed = drop / dt;

                if (verticalSpeed < minVerticalSpeedMetersPerSec)
                {
                    if (debugLogs) Debug.Log($"[SOS] Drop ignored (too slow). drop={drop:0.00} dt={dt:0.00}s v={verticalSpeed:0.00}m/s");
                    continue;
                }

                if (debugLogs) Debug.Log($"[SOS] Head drop detected drop={drop:0.00} v={verticalSpeed:0.00}m/s -> confirm low hold");
                sosActive = true;
                StartCoroutine(ConfirmLowHoldAndAsk(reason: "hololens_fall_local"));
                _y.Clear();
                continue;
            }

            lastHeadPos = headPos;
        }
    }

    private IEnumerator ConfirmLowHoldAndAsk(string reason)
    {
        float lowHoldTimer = 0f;
        yield return new WaitForSeconds(0.10f);

        while (lowHoldTimer < lowHoldSeconds)
        {
            var headPos = GetHeadLocalPos();
            float y = headPos.y;

            bool isLowRelative = y <= (baselineHeadY - fallHeightDropMeters * 0.75f);
            bool isLow = isLowRelative;

            if (useAbsoluteLowY)
                isLow = isLow && (y <= absoluteLowHeadY);

            float move = (headPos - lastHeadPos).magnitude;
            lastHeadPos = headPos;

            if (isLow && move <= lowMotionEpsilonMeters)
                lowHoldTimer += Time.deltaTime;
            else
                lowHoldTimer = 0f;

            yield return null;
        }

        if (debugLogs) Debug.Log("[SOS] Low hold confirmed -> AskUser()");
        AskUser_CreateEventIfNeeded(reason);
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private IEnumerator AccelLoop()
    {
        while (enabled)
        {
            yield return new WaitForSeconds(sampleInterval);

            if (!accelSupported) continue;
            if (sosActive) continue;

            if (!TryReadAccelerationMs2(out var accel)) continue;

            currentAccel = accel;
            float aMag = accel.magnitude;

            if (aMag >= impactAccelThreshold)
            {
                sosActive = true;
                if (debugLogs) Debug.Log($"[SOS] Accel impact aMag={aMag:0.00} -> after impact check");
                StartCoroutine(AfterImpactCheckImmobile());
            }
        }
    }

    private IEnumerator AfterImpactCheckImmobile()
    {
        yield return new WaitForSeconds(postImpactGraceSeconds);

        float immobileTimer = 0f;
        float maxWindow = immobileSecondsRequired + Mathf.Max(0f, immobileMaxWindowExtra);
        float t0 = Time.time;

        lastAccel = currentAccel;

        while (Time.time - t0 < maxWindow)
        {
            if (!TryReadAccelerationMs2(out var accel))
            {
                yield return null;
                continue;
            }

            float delta = (accel - lastAccel).magnitude;
            lastAccel = accel;

            if (delta <= immobileAccelEpsilon) immobileTimer += Time.deltaTime;
            else immobileTimer = 0f;

            if (immobileTimer >= immobileSecondsRequired) break;

            yield return null;
        }

        if (immobileTimer < immobileSecondsRequired)
        {
            if (debugLogs) Debug.Log("[SOS] Accel immobile NOT confirmed -> cancel");
            sosActive = false;
            yield break;
        }

        if (debugLogs) Debug.Log("[SOS] Accel immobile confirmed -> AskUser()");
        AskUser_CreateEventIfNeeded("hololens_impact_local");
    }
#endif

    // ========== FLOW: Ask user + ensure event exists on server ==========
    private void AskUser_CreateEventIfNeeded(string reason)
    {
        awaitingResponse = true;

        router?.azureTTS?.StopNow();
        tts?.StopNow();

        // Intrăm în gramatica SOS înainte să vorbim.
        // Important pentru HoloLens: recognizer-ul are nevoie de puțin timp să recompileze gramatica.
        SetSosSpeechMode(true);

        if (string.IsNullOrEmpty(pendingEventId))
        {
            StartCoroutine(CreateEventOnServer(reason));
        }

        if (askSpeakRoutine != null)
        {
            StopCoroutine(askSpeakRoutine);
            askSpeakRoutine = null;
        }

        askSpeakRoutine = StartCoroutine(SpeakAskTextAfterSosRecognizerReady());

        StartCoroutine(AskTimeout());
    }

    private IEnumerator SpeakAskTextAfterSosRecognizerReady()
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, sosSpeechWarmupSeconds));

        if (!awaitingResponse)
            yield break;

        tts?.Speak(askText);
        askSpeakRoutine = null;
    }

    private IEnumerator AskTimeout()
    {
        float t0 = Time.time;

        while (awaitingResponse && Time.time - t0 < askTimeoutSeconds)
            yield return null;

        if (!awaitingResponse) yield break;

        awaitingResponse = false;
        SetSosSpeechMode(false);
        if (debugLogs) Debug.Log("[SOS] No response -> decision=no_response");

        // IMPORTANT: asteapta putin daca event-ul e inca in curs de creare
        float waitUntil = Time.time + 3.0f;
        while (string.IsNullOrEmpty(pendingEventId) && Time.time < waitUntil)
            yield return null;

        // decision => alertă caregiver
        ConfirmToServerDecision("no_response");

        tts?.StopNow();
        tts?.Speak(sendingHelpText);

        StartCoroutine(ResetAfterSeconds(6f));
    }

    public bool TryHandleVoiceResponse(string cmdNorm, string raw)
    {
        if (!awaitingResponse)
            return false;

        string c = NormalizeSosText((cmdNorm ?? "") + " " + (raw ?? ""));

        if (debugLogs)
            Debug.Log("[SOS] Voice response heard: " + c);

        bool help = IsSosHelpResponse(c);
        bool ok = IsSosOkResponse(c);

        // IMPORTANT:
        // Verificăm întâi HELP, ca "NU SUNT BINE" să nu fie confundat cu "SUNT BINE".
        if (help)
        {
            awaitingResponse = false;
            SetSosSpeechMode(false);

            router?.azureTTS?.StopNow();
            tts?.StopNow();

            tts?.Speak(sendingHelpText);

            StartCoroutine(ConfirmDecisionThenReset("help", 6f));
            return true;
        }

        if (ok)
        {
            awaitingResponse = false;
            SetSosSpeechMode(false);

            router?.azureTTS?.StopNow();
            tts?.StopNow();

            tts?.Speak(confirmedOkText);

            StartCoroutine(ConfirmDecisionThenReset("ok", 2f));
            return true;
        }

        // Dacă a auzit ceva, dar nu e clar, nu lăsăm comanda să intre în restul aplicației.
        // Îi cerem din nou răspuns clar, iar timeout-ul continuă.
        tts?.StopNow();
        tts?.Speak("Nu am înțeles răspunsul. Spune SUNT BINE dacă ești bine sau NU dacă ai nevoie de ajutor.");

        return true;
    }
    private static string NormalizeSosText(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "";

        string s = input.ToLowerInvariant();

        s = s.Replace("ă", "a")
             .Replace("â", "a")
             .Replace("î", "i")
             .Replace("ș", "s")
             .Replace("ş", "s")
             .Replace("ț", "t")
             .Replace("ţ", "t");

        StringBuilder sb = new StringBuilder(s.Length);

        foreach (char ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        s = sb.ToString();

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    private static bool IsSosOkResponse(string c)
    {
        if (string.IsNullOrWhiteSpace(c))
            return false;

        // Răspunsuri scurte clare
        if (c == "da") return true;
        if (c == "bine") return true;
        if (c == "ok") return true;
        if (c == "okay") return true;

        // Sunt bine / sunt ok
        if (c.Contains("sunt bine")) return true;
        if (c.Contains("sint bine")) return true;
        if (c.Contains("sant bine")) return true;
        if (c.Contains("eu sunt bine")) return true;
        if (c.Contains("sunt ok")) return true;
        if (c.Contains("sint ok")) return true;

        // Mă simt bine
        if (c.Contains("ma simt bine")) return true;
        if (c.Contains("ma simt ok")) return true;

        // Sunt în regulă
        if (c.Contains("sunt in regula")) return true;
        if (c.Contains("sint in regula")) return true;
        if (c.Contains("sant in regula")) return true;
        if (c.Contains("sunt regula")) return true;

        // Totul e bine
        if (c.Contains("totul e bine")) return true;
        if (c.Contains("totul este bine")) return true;
        if (c.Contains("este bine")) return true;
        if (c.Contains("e bine")) return true;
        if (c.Contains("e ok")) return true;

        // Fraze naturale după cădere
        if (c.Contains("nu am patit nimic")) return true;
        if (c.Contains("n am patit nimic")) return true;
        if (c.Contains("nu am nimic")) return true;
        if (c.Contains("n am nimic")) return true;

        // Important: acestea înseamnă OK, nu alertă
        if (c.Contains("nu am nevoie de ajutor")) return true;
        if (c.Contains("n am nevoie de ajutor")) return true;
        if (c.Contains("nu trebuie ajutor")) return true;
        if (c.Contains("fara ajutor")) return true;

        return false;
    }
    private static bool IsSosHelpResponse(string c)
    {
        if (string.IsNullOrWhiteSpace(c))
            return false;

        c = NormalizeSosText(c);

        string compact = c.Replace(" ", "");

        // ==========================================================
        // PROTECȚII: acestea înseamnă că utilizatorul este OK,
        // deci NU trimitem alertă.
        // ==========================================================
        bool saysNoHelpNeeded =
            c.Contains("nu am nevoie de ajutor") ||
            c.Contains("n am nevoie de ajutor") ||
            compact.Contains("nuamnevoiedeajutor") ||
            compact.Contains("namnevoiedeajutor") ||

            c.Contains("nu trebuie ajutor") ||
            compact.Contains("nutrebuieajutor") ||

            c.Contains("fara ajutor") ||
            compact.Contains("faraajutor") ||

            c.Contains("nu am patit nimic") ||
            c.Contains("n am patit nimic") ||
            compact.Contains("nuampatitnimic") ||
            compact.Contains("nampatitnimic") ||

            c.Contains("nu am nimic") ||
            c.Contains("n am nimic") ||
            compact.Contains("nuamnimic") ||
            compact.Contains("namnimic");

        if (saysNoHelpNeeded)
            return false;

        // ==========================================================
        // RĂSPUNS SCURT: NU
        // În contextul întrebării "Ești bine?", "NU" înseamnă ajutor.
        // ==========================================================
        if (c == "nu")
            return true;

        // ==========================================================
        // Utilizatorul spune clar că NU este bine.
        // ==========================================================
        if (c.Contains("nu sunt bine")) return true;
        if (c.Contains("nu sint bine")) return true;
        if (c.Contains("nu sant bine")) return true;
        if (compact.Contains("nusuntbine")) return true;
        if (compact.Contains("nusintbine")) return true;
        if (compact.Contains("nusantbine")) return true;

        if (c.Contains("nu ma simt bine")) return true;
        if (compact.Contains("numasimtbine")) return true;

        if (c.Contains("nu sunt ok")) return true;
        if (c.Contains("nu sint ok")) return true;
        if (compact.Contains("nusuntok")) return true;
        if (compact.Contains("nusintok")) return true;

        if (c.Contains("nu e bine")) return true;
        if (c.Contains("nu este bine")) return true;
        if (compact.Contains("nuebune")) return false; // protecție pentru recunoaștere greșită rară
        if (compact.Contains("nuebien")) return true;
        if (compact.Contains("nuebines")) return true;
        if (compact.Contains("nuebinen")) return true;
        if (compact.Contains("nuebne")) return true;
        if (compact.Contains("nuebine")) return true;
        if (compact.Contains("nuestebine")) return true;

        if (c.Contains("nu sunt in regula")) return true;
        if (c.Contains("nu sint in regula")) return true;
        if (compact.Contains("nusuntinregula")) return true;
        if (compact.Contains("nusintinregula")) return true;

        // ==========================================================
        // CERERE EXPLICITĂ DE AJUTOR
        // Foarte tolerant pentru recunoaștere vocală:
        // "ajutor", "a jutor", "ajutorul", "ajuta-ma", "help", "sos".
        // ==========================================================
        if (c == "ajutor") return true;
        if (c.Contains("ajutor")) return true;
        if (compact.Contains("ajutor")) return true;

        if (c.Contains("am nevoie de ajutor")) return true;
        if (compact.Contains("amnevoiedeajutor")) return true;

        if (c.Contains("ajuta ma")) return true;
        if (c.Contains("ajutama")) return true;
        if (compact.Contains("ajutama")) return true;

        if (c.Contains("ajutati ma")) return true;
        if (c.Contains("ajutatima")) return true;
        if (compact.Contains("ajutatima")) return true;

        if (c.Contains("trimite ajutor")) return true;
        if (compact.Contains("trimiteajutor")) return true;

        if (c.Contains("cheama ajutor")) return true;
        if (compact.Contains("cheamaajutor")) return true;

        if (c.Contains("cheama pe cineva")) return true;
        if (compact.Contains("cheamapecineva")) return true;

        if (c.Contains("help")) return true;
        if (c.Contains("sos")) return true;

        // ==========================================================
        // URGENȚĂ / 112
        // ==========================================================
        if (c.Contains("ambulanta")) return true;
        if (c.Contains("ambulan")) return true;

        if (c.Contains("urgenta")) return true;
        if (compact.Contains("urgenta")) return true;

        if (c.Contains("112")) return true;

        if (c.Contains("suna la")) return true;
        if (compact.Contains("sunala")) return true;

        if (c.Contains("sunati")) return true;
        if (c.Contains("chemati")) return true;

        // Atenție: "cheama" singur poate fi vag, dar în context SOS îl tratăm ca urgență.
        if (c.Contains("cheama")) return true;

        // ==========================================================
        // SIMPTOME / ACCIDENT
        // ==========================================================
        if (c.Contains("m am lovit")) return true;
        if (compact.Contains("mamlovit")) return true;

        if (c.Contains("ma doare")) return true;
        if (compact.Contains("madoare")) return true;

        if (c.Contains("am cazut")) return true;
        if (compact.Contains("amcazut")) return true;

        if (c.Contains("sunt ranit")) return true;
        if (c.Contains("sunt ranita")) return true;
        if (compact.Contains("suntrant")) return true;
        if (compact.Contains("suntranit")) return true;
        if (compact.Contains("suntranita")) return true;

        if (c.Contains("imi este rau")) return true;
        if (compact.Contains("imiesterau")) return true;

        if (c.Contains("mi e rau")) return true;
        if (compact.Contains("mierau")) return true;

        if (c.Contains("nu pot sa ma ridic")) return true;
        if (compact.Contains("nupotsamaridic")) return true;

        if (c.Contains("nu ma pot ridica")) return true;
        if (compact.Contains("numapotridica")) return true;

        return false;
    }

    private IEnumerator ConfirmDecisionThenReset(string decision, float resetAfterSeconds)
    {
        // Dacă evenimentul local încă se creează pe server,
        // așteptăm puțin ca să avem eventId înainte de decision.
        float waitUntil = Time.time + 3.0f;

        while (string.IsNullOrEmpty(pendingEventId) && Time.time < waitUntil)
            yield return null;

        ConfirmToServerDecision(decision);

        yield return new WaitForSeconds(resetAfterSeconds);

        sosActive = false;
        awaitingResponse = false;
        pendingEventId = null;
        pendingDeviceId = null;
        pendingBaseUrl = null;

        StartCalibration();
    }


    public void TriggerImmediateHelp(string reason = "voice_help")
    {
        if (debugLogs) Debug.Log("[SOS] TriggerImmediateHelp reason=" + reason);

       
        if (awaitingResponse || sosActive)
        {
            awaitingResponse = false;
            SetSosSpeechMode(false);

            router?.azureTTS?.StopNow();
            tts?.StopNow();

            tts?.Speak(sendingHelpText);

            StartCoroutine(ConfirmDecisionThenReset("help", 6f));
            return;
        }

        // Caz normal: utilizatorul spune AJUTOR fără cădere detectată înainte.
        sosActive = true;
        awaitingResponse = false;

        StartCoroutine(CreateEventThenDecide(reason, "help"));

        tts?.StopNow();
        tts?.Speak(sendingHelpText);

        StartCoroutine(ResetAfterSeconds(6f));
    }

    private IEnumerator ResetAfterSeconds(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        sosActive = false;
        awaitingResponse = false;
        pendingEventId = null;
        pendingDeviceId = null;
        pendingBaseUrl = null;
        StartCalibration();
    }

    [ContextMenu("TEST SOS (AskUser)")]
    public void TestAskNow()
    {
        if (sosActive || awaitingResponse) return;
        sosActive = true;
        AskUser_CreateEventIfNeeded("test_manual");
    }

    // ===== External event entrypoint (de la WatchEventReceiver) =====
    public void AskNowFromExternalEvent(string reason = "external_fall")
    {
        if (sosActive || awaitingResponse) return;

        if (debugLogs)
            Debug.Log("[SOS] AskNowFromExternalEvent reason=" + reason);

        sosActive = true;

        // Folosim aceeași cale ca la căderea locală:
        // setează awaitingResponse, activează gramatica SOS, așteaptă warmup,
        // apoi spune întrebarea.
        AskUser_CreateEventIfNeeded(reason);
    }

    /// <summary>
    /// Setează contextul evenimentului extern (eventId/deviceId/baseUrl).
    /// baseUrl: http://ip:8080
    /// </summary>
    public void SetExternalPending(string eventId, string devId, string baseUrl)
    {
        pendingEventId = eventId;
        pendingDeviceId = devId;
        pendingBaseUrl = baseUrl;
    }

    private void ConfirmToServerDecision(string decision)
    {
        if (string.IsNullOrEmpty(pendingEventId) || string.IsNullOrEmpty(pendingBaseUrl))
        {
            if (debugLogs) Debug.LogWarning("[SOS] No pending eventId/baseUrl -> cannot post decision.");
            return;
        }

        StartCoroutine(PostDecision(pendingBaseUrl, pendingEventId, pendingDeviceId, decision));
    }

    private IEnumerator PostDecision(string baseUrl, string eventId, string devId, string decision)
    {
        string url = baseUrl.TrimEnd('/') + "/api/sos/decision";
        string safeDevice = string.IsNullOrEmpty(devId) ? deviceId : devId;

        string json =
            "{\"deviceId\":\"" + safeDevice +
            "\",\"eventId\":\"" + eventId +
            "\",\"decision\":\"" + decision + "\"}";

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;

            yield return req.SendWebRequest();

            if (debugLogs)
            {
                if (req.result != UnityWebRequest.Result.Success)
                    Debug.LogWarning("[SOS] PostDecision FAIL: " + req.error + " body=" + req.downloadHandler?.text);
                else
                    Debug.Log("[SOS] PostDecision OK");
            }
        }

        // clear pending after decision
        pendingEventId = null;
        pendingDeviceId = null;
        pendingBaseUrl = null;
    }

    // ===== Create local event on server =====
    private IEnumerator CreateEventOnServer(string reason)
    {
        if (string.IsNullOrWhiteSpace(serverBaseUrl))
            yield break;

        string baseUrl = serverBaseUrl.TrimEnd('/');
        string url = baseUrl + "/api/sos/event";

        // payload minimal (locatia poate veni de la telefon/ceas in alt event)
        string json =
            "{"
            + "\"deviceId\":\"" + deviceId + "\","
            + "\"type\":\"fall_detected\","
            + "\"reason\":\"" + reason + "\","
            + "\"timestampUtc\":\"" + DateTime.UtcNow.ToString("o") + "\""
            + "}";

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 8;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (debugLogs) Debug.LogWarning("[SOS] CreateEvent FAIL: " + req.error + " body=" + req.downloadHandler?.text);
                yield break;
            }

            EventResp resp = null;
            try { resp = JsonUtility.FromJson<EventResp>(req.downloadHandler.text); } catch { }

            if (resp == null || !resp.ok || string.IsNullOrWhiteSpace(resp.eventId))
            {
                if (debugLogs) Debug.LogWarning("[SOS] CreateEvent invalid response: " + req.downloadHandler.text);
                yield break;
            }

            pendingEventId = resp.eventId;
            pendingDeviceId = deviceId;
            pendingBaseUrl = baseUrl;

            if (debugLogs) Debug.Log("[SOS] CreateEvent OK eventId=" + pendingEventId);
        }
    }

    private IEnumerator CreateEventThenDecide(string reason, string decision)
    {
        yield return CreateEventOnServer(reason);

        float waitUntil = Time.time + 3.0f;
        while (string.IsNullOrEmpty(pendingEventId) && Time.time < waitUntil)
            yield return null;

        ConfirmToServerDecision(decision);
    }
    public void AskNowManualHelp(string reason = "voice_help")
    {
        if (debugLogs) Debug.Log("[SOS] AskNowManualHelp reason=" + reason);

        if (sosActive || awaitingResponse)
            return;

        sosActive = true;
        awaitingResponse = false;

        pendingEventId = null;
        pendingDeviceId = deviceId;
        pendingBaseUrl = serverBaseUrl;

        AskUser_CreateEventIfNeeded(reason);
    }
    private UWPSpeechRecognizer GetSpeechRecognizer()
    {
        if (sosSpeech == null)
            sosSpeech = FindObjectOfType<UWPSpeechRecognizer>(true);

        return sosSpeech;
    }

    private void SetSosSpeechMode(bool active)
    {
        var speech = GetSpeechRecognizer();
        if (speech != null)
            speech.SetSosResponseMode(active);
    }
}