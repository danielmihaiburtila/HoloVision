using UnityEngine;
using System.Globalization;

public class ObstacleDetectorMR : MonoBehaviour
{
    public enum Mode { Passive, Active }

    [Header("Mode")]
    public Mode mode = Mode.Passive;

    [Header("Ray origin")]
    public Transform headCamera;

    [Header("Detection")]
    public float maxDistance = 3.0f;
    public float sphereRadius = 0.18f;
    public float verticalOffset = -0.05f;
    public float checkInterval = 0.2f;

    [Header("Direction buckets")]
    public float sideAngleDeg = 18f;

    [Header("Layers")]
    public LayerMask obstacleLayers;

    [Header("Outputs")]
    public AzureTTS tts;

    // PASSIVE (always-on, very quiet)
    [Header("PASSIVE (always-on)")]
    [Tooltip("Vorbește doar dacă e mai aproape decât asta (m).")]
    public float passiveDangerDistance = 0.65f;

    [Tooltip("Dacă te apropii repede, avertizează puțin mai devreme (m/s).")]
    public float passiveMinClosingSpeed = 0.35f;

    [Tooltip("Cooldown între avertizări în PASSIVE.")]
    public float passiveSpeakCooldown = 2.2f;

    // ACTIVE (scan/explore)
    [Header("ACTIVE (scan)")]
    public float activeSafeDistance = 1.4f;
    public bool activeSpeakOnlyCenter = true;
    public float activeUrgentDistance = 1.0f;
    public float activeSpeakCooldown = 3.0f;
    public float activeMinDistanceDeltaToSpeak = 0.35f;

    [Header("Beep (ACTIVE only)")]
    public bool useBeepInActiveMode = true;
    public AudioSource beepSource;
    public AudioClip beepClip;
    [Range(0f, 1f)] public float beepVolume = 0.6f;
    public float beepCooldown = 0.45f;
    public bool spatialBeep = true;
    public float beepSideOffset = 0.35f;

    // Internal
    private float _nextCheckTime;
    private float _lastSpeakTime = -999f;
    private float _lastBeepTime = -999f;

    private float _lastSpokenDistance = -1f;
    private Dir _lastSpokenDir = Dir.None;

    private float _prevDistance = -1f;
    private float _prevTime = 0f;

    private readonly RaycastHit[] _hits = new RaycastHit[10];

    private enum Dir { None, Left, Center, Right }

    private void Awake()
    {
        if (headCamera == null && Camera.main != null) headCamera = Camera.main.transform;
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);

        // Default layer: Spatial Awareness
        if (obstacleLayers.value == 0)
        {
            int layer = LayerMask.NameToLayer("Spatial Awareness");
            if (layer >= 0) obstacleLayers = 1 << layer;
        }

        // Setup beep source (exists but only used in ACTIVE)
        if (beepSource == null)
        {
            var go = new GameObject("ObstacleBeepSource");
            go.transform.SetParent(transform, false);
            beepSource = go.AddComponent<AudioSource>();
        }

        beepSource.playOnAwake = false;
        beepSource.loop = false;
        beepSource.spatialBlend = spatialBeep ? 1f : 0f;
        beepSource.rolloffMode = AudioRolloffMode.Logarithmic;
        beepSource.minDistance = 0.2f;
        beepSource.maxDistance = 6f;
        beepSource.volume = beepVolume;

        if (beepClip == null)
            beepClip = GenerateBeepClip();
    }

    public void SetMode(Mode m)
    {
        mode = m;

        // reset hysteresis
        _lastSpokenDistance = -1f;
        _lastSpokenDir = Dir.None;
        _prevDistance = -1f;
    }

    private void Update()
    {
        if (!enabled) return;
        if (headCamera == null) return;
        if (Time.time < _nextCheckTime) return;
        _nextCheckTime = Time.time + checkInterval;

        Vector3 origin = headCamera.position + headCamera.up * verticalOffset;
        Vector3 dir = headCamera.forward;

        int count = Physics.SphereCastNonAlloc(
            origin, sphereRadius, dir, _hits, maxDistance,
            obstacleLayers, QueryTriggerInteraction.Ignore
        );

        if (count <= 0)
        {
            _prevDistance = -1f;
            return;
        }

        // nearest hit
        RaycastHit nearest = _hits[0];
        float best = nearest.distance;
        for (int i = 1; i < count; i++)
        {
            if (_hits[i].distance < best)
            {
                nearest = _hits[i];
                best = _hits[i].distance;
            }
        }

        float distance = Mathf.Clamp(best, 0.05f, maxDistance);
        Dir d = ComputeDirection(headCamera, nearest.point);

        // closing speed
        float closingSpeed = 0f;
        if (_prevDistance > 0f)
        {
            float dt = Mathf.Max(0.001f, Time.time - _prevTime);
            closingSpeed = (_prevDistance - distance) / dt; // >0 => getting closer
        }
        _prevDistance = distance;
        _prevTime = Time.time;

        if (mode == Mode.Passive) PassiveLogic(distance, d, closingSpeed);
        else ActiveLogic(distance, d, closingSpeed);
    }

    private void PassiveLogic(float distance, Dir d, float closingSpeed)
    {
        // PASSIVE = quiet, no beeps
        bool imminent = distance <= passiveDangerDistance;
        bool fastApproach = closingSpeed >= passiveMinClosingSpeed && distance <= (passiveDangerDistance + 0.25f);

        if (!imminent && !fastApproach) return;
        if ((Time.time - _lastSpeakTime) < passiveSpeakCooldown) return;

        Speak(distance, d, urgent: true);
        _lastSpeakTime = Time.time;
    }

    private void ActiveLogic(float distance, Dir d, float closingSpeed)
    {
        bool urgent = distance <= activeUrgentDistance;

        // Beep only in ACTIVE
        if (useBeepInActiveMode)
        {
            bool beepOk = (Time.time - _lastBeepTime) >= beepCooldown;
            if (beepOk && (urgent || distance <= activeSafeDistance))
            {
                PlayBeep(distance, d);
                _lastBeepTime = Time.time;
            }
        }

        // Speech filtering
        if (!urgent && distance > activeSafeDistance) return;
        if (!urgent && activeSpeakOnlyCenter && d != Dir.Center) return;

        bool cooldownOk = (Time.time - _lastSpeakTime) >= activeSpeakCooldown;
        bool distChanged = _lastSpokenDistance < 0f || Mathf.Abs(distance - _lastSpokenDistance) >= activeMinDistanceDeltaToSpeak;
        bool dirChanged = d != _lastSpokenDir;

        // key: nu vorbim dacă nu te apropii (previne spam când stai lângă scaun)
        bool approaching = closingSpeed > 0.10f;

        if (urgent)
        {
            if ((Time.time - _lastSpeakTime) >= 0.9f)
            {
                Speak(distance, d, urgent: true);
                _lastSpeakTime = Time.time;
                _lastSpokenDistance = distance;
                _lastSpokenDir = d;
            }
            return;
        }

        if (cooldownOk && approaching && (distChanged || dirChanged))
        {
            Speak(distance, d, urgent: false);
            _lastSpeakTime = Time.time;
            _lastSpokenDistance = distance;
            _lastSpokenDir = d;
        }
    }

    private Dir ComputeDirection(Transform cam, Vector3 hitPoint)
    {
        Vector3 to = hitPoint - cam.position; to.y = 0f;
        Vector3 fwd = cam.forward; fwd.y = 0f;

        if (to.sqrMagnitude < 0.0001f || fwd.sqrMagnitude < 0.0001f) return Dir.Center;

        float angle = Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        if (angle > sideAngleDeg) return Dir.Right;
        if (angle < -sideAngleDeg) return Dir.Left;
        return Dir.Center;
    }

    private void Speak(float distance, Dir dir, bool urgent)
    {
        float rounded = Mathf.Round(distance * 10f) / 10f;

        string side = dir == Dir.Left ? "ușor stânga"
                   : dir == Dir.Right ? "ușor dreapta"
                   : "în față";

        string msg = urgent
            ? $"Atenție! Obstacol la {rounded.ToString("0.0", CultureInfo.InvariantCulture)} metri, {side}."
            : $"Obstacol la {rounded.ToString("0.0", CultureInfo.InvariantCulture)} metri, {side}.";

        tts?.Speak(msg);
        Debug.Log("[ObstacleDetectorMR] " + msg + " mode=" + mode);
    }

    private void PlayBeep(float distance, Dir dir)
    {
        if (beepSource == null || beepClip == null || headCamera == null) return;

        beepSource.volume = beepVolume;
        beepSource.spatialBlend = spatialBeep ? 1f : 0f;

        if (spatialBeep)
        {
            Vector3 pos = headCamera.position + headCamera.forward * Mathf.Min(distance, 2.5f);
            if (dir == Dir.Left) pos += -headCamera.right * beepSideOffset;
            else if (dir == Dir.Right) pos += headCamera.right * beepSideOffset;
            beepSource.transform.position = pos;
        }
        else
        {
            beepSource.transform.position = headCamera.position;
        }

        beepSource.PlayOneShot(beepClip);
    }

    private AudioClip GenerateBeepClip()
    {
        const int sampleRate = 48000;
        float duration = 0.06f;
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

            data[i] = s * fade * 0.3f;
        }

        var clip = AudioClip.Create("GeneratedBeep", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}