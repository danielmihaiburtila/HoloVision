using UnityEngine;

public class SafetyFeedbackController : MonoBehaviour
{
    [Header("Mode")]
    public WalkingSafetyMode currentMode = WalkingSafetyMode.Passive;

    [Header("Distance Thresholds (meters)")]
    [Min(0.2f)] public float criticalStopDistance = 0.8f;
    [Min(0.4f)] public float dangerDistance = 1.2f;
    [Min(0.6f)] public float warningDistance = 2.0f;
    [Min(1.0f)] public float maxDistance = 3.0f;

    [Header("Beeps")]
    public bool enableBeeps = true;
    public AudioSource beepAudioSource;
    public AudioClip beepClip;
    [Range(0f, 1f)] public float beepVolume = 0.8f;

    [Tooltip("Daca e true, panStereo urmareste directia recomandata (stanga/dreapta).")]
    public bool panBeepsByRecommendedDirection = true;

    [Header("TTS / Speech")]
    public bool enableSpeech = true;
    [Tooltip("Leaga aici metoda din TTS_System care primeste string (Dynamic string).")]
    public StringUnityEvent OnSpeakRequested;

    [Header("Anti-spam TTS")]
    [Min(0.1f)] public float ttsCooldown = 1.5f;
    [Min(0.05f)] public float significantDistanceDelta = 0.30f;
    [Range(5f, 45f)] public float significantAngleDelta = 15f;

    [Header("Debug")]
    public bool debugLogs = true;

    private SafetySnapshot _lastSnapshot;
    private bool _hasSnapshot;

    private float _nextBeepTime;
    private float _lastSpeechTime = -999f;
    private float _lastSpokenCenterDistance = -1f;
    private float _lastSpokenRecommendedAngle = 999f;
    private string _lastSpeechMessage = "";

    private enum RiskBand
    {
        Clear,
        Warning,
        Danger,
        Critical
    }

    public void SetMode(WalkingSafetyMode mode)
    {
        currentMode = mode;

        if (debugLogs)
            Debug.Log($"[SafetyFeedback] Mode = {mode}");
    }

    public void ResetState()
    {
        _hasSnapshot = false;
        _nextBeepTime = 0f;
        _lastSpeechTime = -999f;
        _lastSpokenCenterDistance = -1f;
        _lastSpokenRecommendedAngle = 999f;
        _lastSpeechMessage = "";
    }

    public void HandleSnapshot(SafetySnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        _hasSnapshot = true;

        if (currentMode == WalkingSafetyMode.Off) return;

        // Vorbim doar cand merita (anti-spam + prioritate)
        TrySpeak(snapshot);
    }

    private void Update()
    {
        if (!_hasSnapshot) return;
        if (currentMode == WalkingSafetyMode.Off) return;

        if (enableBeeps)
        {
            HandleBeeps(_lastSnapshot);
        }
    }

    private void HandleBeeps(SafetySnapshot s)
    {
        if (beepAudioSource == null || beepClip == null) return;

        float interval = GetBeepInterval(s, currentMode);

        if (interval <= 0f) return;
        if (Time.time < _nextBeepTime) return;

        _nextBeepTime = Time.time + interval;

        if (panBeepsByRecommendedDirection)
        {
            // -1 = stanga, +1 = dreapta
            float pan = Mathf.Clamp(s.RecommendedAngle / 40f, -1f, 1f);
            beepAudioSource.panStereo = pan;
        }
        else
        {
            beepAudioSource.panStereo = 0f;
        }

        beepAudioSource.PlayOneShot(beepClip, beepVolume);
    }

    private float GetBeepInterval(SafetySnapshot s, WalkingSafetyMode mode)
    {
        float d = s.CenterDistance;

        // In Reduced, pastram doar alerta critica/foarte aproape
        if (mode == WalkingSafetyMode.Reduced)
        {
            if (d < criticalStopDistance) return 0.15f;
            if (d < dangerDistance) return 0.35f;
            return 0f;
        }

        // In Passive / Guidance: beep-uri adaptive
        if (d < criticalStopDistance) return 0.12f;
        if (d < dangerDistance) return 0.25f;
        if (d < warningDistance) return 0.50f;
        if (d < 2.5f) return 1.00f;

        // prea departe => silentios (mai relaxant)
        return 0f;
    }

    private void TrySpeak(SafetySnapshot s)
    {
        if (!enableSpeech) return;

        RiskBand centerBand = GetRiskBand(s.CenterDistance);

        // Prioritate maxima: critic
        if (centerBand == RiskBand.Critical)
        {
            SpeakIfAllowed("Stop. Obstacol în față.", forceIfDifferent: true);
            return;
        }

        // In mod Reduced, nu mai vorbim informativ (doar critic mai sus)
        if (currentMode == WalkingSafetyMode.Reduced)
            return;

        // In Passive: vorbim rar si doar daca e relevant
        if (currentMode == WalkingSafetyMode.Passive)
        {
            if (centerBand == RiskBand.Clear) return;

            bool distChanged = Mathf.Abs(s.CenterDistance - _lastSpokenCenterDistance) >= significantDistanceDelta;
            bool angleChanged = Mathf.Abs(Mathf.DeltaAngle(s.RecommendedAngle, _lastSpokenRecommendedAngle)) >= significantAngleDelta;

            if (!distChanged && !angleChanged) return;

            string msg = BuildPassiveMessage(s);
            SpeakIfAllowed(msg, forceIfDifferent: false);

            _lastSpokenCenterDistance = s.CenterDistance;
            _lastSpokenRecommendedAngle = s.RecommendedAngle;
            return;
        }

        // In Guidance: mai explicit
        if (currentMode == WalkingSafetyMode.Guidance)
        {
            bool distChanged = Mathf.Abs(s.CenterDistance - _lastSpokenCenterDistance) >= 0.20f;
            bool angleChanged = Mathf.Abs(Mathf.DeltaAngle(s.RecommendedAngle, _lastSpokenRecommendedAngle)) >= 10f;

            if (!distChanged && !angleChanged) return;

            string msg = BuildGuidanceMessage(s);
            SpeakIfAllowed(msg, forceIfDifferent: false);

            _lastSpokenCenterDistance = s.CenterDistance;
            _lastSpokenRecommendedAngle = s.RecommendedAngle;
        }
    }

    private void SpeakIfAllowed(string message, bool forceIfDifferent)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        bool cooldownPassed = (Time.time - _lastSpeechTime) >= ttsCooldown;
        bool isDifferent = !string.Equals(message, _lastSpeechMessage);

        if (!cooldownPassed && !(forceIfDifferent && isDifferent))
            return;

        _lastSpeechTime = Time.time;
        _lastSpeechMessage = message;

        if (debugLogs)
            Debug.Log("[SafetyFeedback][TTS] " + message);

        if (OnSpeakRequested != null)
            OnSpeakRequested.Invoke(message);
    }

    private RiskBand GetRiskBand(float d)
    {
        if (d < criticalStopDistance) return RiskBand.Critical;
        if (d < dangerDistance) return RiskBand.Danger;
        if (d < warningDistance) return RiskBand.Warning;
        return RiskBand.Clear;
    }

    private string BuildPassiveMessage(SafetySnapshot s)
    {
        // Mesaj scurt, neintruziv
        string obstacleDir = AngleToDirectionLabel(s.NearestAngle, forObstacle: true);
        return $"Obstacol la {s.CenterDistance:0.0} metri. {obstacleDir}.";
    }

    private string BuildGuidanceMessage(SafetySnapshot s)
    {
        // Mesaj mai util pentru deplasare
        string pathDir = AngleToDirectionLabel(s.RecommendedAngle, forObstacle: false);

        if (s.CenterDistance < warningDistance)
        {
            return $"Obstacol la {s.CenterDistance:0.0} metri. Mergi {pathDir}.";
        }

        return $"Cale mai liberă {pathDir}.";
    }

    private string AngleToDirectionLabel(float angle, bool forObstacle)
    {
        // angle < 0 => stanga, angle > 0 => dreapta
        float a = angle;

        if (Mathf.Abs(a) <= 10f)
            return forObstacle ? "în față" : "înainte";

        if (a < 0f)
        {
            if (Mathf.Abs(a) <= 25f) return "ușor stânga";
            return "stânga";
        }
        else
        {
            if (Mathf.Abs(a) <= 25f) return "ușor dreapta";
            return "dreapta";
        }
    }
}