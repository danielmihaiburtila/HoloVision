using System.Globalization;
using UnityEngine;

/// <summary>
/// HoloLens2 / MRTK3:
/// - Always-on PASSIVE safety: speaks (Alina) ONLY when collision risk is imminent.
/// - Optional ACTIVE guidance: spatial beeps that "pull" you toward the safest direction.
/// - Uses Spatial Awareness mesh colliders (Physics).
/// </summary>
public class SafeWalkGuidanceMR : MonoBehaviour
{
    public enum Mode { PassiveSafety, ActiveGuidance }

    [Header("Mode")]
    public Mode mode = Mode.PassiveSafety;

    [Header("References")]
    public Transform headCamera;          // Main Camera (MRTK XR Rig)
    public LayerMask obstacleLayers;      // Spatial Awareness
    public AzureTTS tts;                  // Alina (Azure)
    public AudioSource spatialBeepSource; // 3D beep source

    [Header("Detection")]
    [Tooltip("How far we check forward (m).")]
    public float maxDistance = 2.5f;

    [Tooltip("Sphere radius (m). Increase if misses thin obstacles.")]
    public float sphereRadius = 0.22f;

    [Tooltip("Ray origin vertical offset relative to head (m). Small negative is ok.")]
    public float verticalOffset = -0.05f;

    [Tooltip("Check interval (sec). 0.1-0.2 ok.")]
    public float checkInterval = 0.12f;

    [Header("PASSIVE safety thresholds (quiet)")]
    [Tooltip("Speak ONLY if obstacle is closer than this (m).")]
    public float dangerDistance = 0.85f;

    [Tooltip("If approaching fast, warn slightly earlier (m/s).")]
    public float minClosingSpeed = 0.30f;

    [Tooltip("Min seconds between voice warnings.")]
    public float voiceCooldown = 2.2f;

    [Header("ACTIVE guidance (optional)")]
    [Tooltip("Enable spatial beeps in Active mode.")]
    public bool enableSpatialBeepInActive = true;

    [Tooltip("How often beeps can happen (sec).")]
    public float beepCooldown = 0.35f;

    [Tooltip("Beep volume.")]
    [Range(0f, 1f)] public float beepVolume = 0.55f;

    [Tooltip("Guidance scan angles (+/- degrees).")]
    public float guidanceAngleRange = 55f;

    [Tooltip("Angle step in degrees. Lower = smoother but more CPU. 10-15 ok.")]
    public float guidanceAngleStep = 12f;

    [Tooltip("If best direction distance is under this, we're basically boxed in (m).")]
    public float boxedInDistance = 0.75f;

    [Header("Target guidance (MVP)")]
    public bool targetGuidanceEnabled = false;
    public Transform target; // set by SetTargetFromGaze()

    // internal
    private float _nextCheck;
    private float _lastVoice = -999f;
    private float _lastBeep = -999f;

    private float _prevDist = -1f;
    private float _prevTime = 0f;

    private readonly RaycastHit[] _hits = new RaycastHit[12];

    void Awake()
    {
        if (headCamera == null && Camera.main != null) headCamera = Camera.main.transform;
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);

        if (obstacleLayers.value == 0)
        {
            int layer = LayerMask.NameToLayer("Spatial Awareness");
            if (layer >= 0) obstacleLayers = 1 << layer;
        }

        if (spatialBeepSource == null)
        {
            var go = new GameObject("SpatialBeepSource");
            go.transform.SetParent(transform, false);
            spatialBeepSource = go.AddComponent<AudioSource>();
        }

        spatialBeepSource.playOnAwake = false;
        spatialBeepSource.loop = false;
        spatialBeepSource.spatialBlend = 1f; // 3D
        spatialBeepSource.rolloffMode = AudioRolloffMode.Logarithmic;
        spatialBeepSource.minDistance = 0.2f;
        spatialBeepSource.maxDistance = 6f;
        spatialBeepSource.volume = beepVolume;
    }

    void Update()
    {
        if (!enabled) return;
        if (headCamera == null) return;

        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + checkInterval;

        // If target guidance is enabled, guide to target (separately from obstacle warnings).
        if (targetGuidanceEnabled && target != null)
        {
            GuideToTarget();
        }

        // 1) Forward safety check
        bool hitSomething = TryForwardHit(out float dist, out Vector3 hitPoint);

        if (!hitSomething)
        {
            _prevDist = -1f;
            return;
        }

        // closing speed
        float closingSpeed = 0f;
        if (_prevDist > 0f)
        {
            float dt = Mathf.Max(0.001f, Time.time - _prevTime);
            closingSpeed = (_prevDist - dist) / dt; // >0 getting closer
        }
        _prevDist = dist;
        _prevTime = Time.time;

        bool imminent = dist <= dangerDistance;
        bool fastApproach = (closingSpeed >= minClosingSpeed) && (dist <= dangerDistance + 0.35f);

        if (!imminent && !fastApproach)
        {
            // Active mode can still beep softly if desired, but we keep it calm:
            if (mode == Mode.ActiveGuidance && enableSpatialBeepInActive)
            {
                // only if within ~1.2m (not for everything in room)
                if (dist <= 1.2f) BeepAt(hitPoint);
            }
            return;
        }

        // 2) Voice warning (Alina) ONLY when dangerous + cooldown
        if ((Time.time - _lastVoice) >= voiceCooldown)
        {
            SpeakDanger(dist);
            _lastVoice = Time.time;
        }

        // 3) If ActiveGuidance, also guide toward safest direction (beep "from safety")
        if (mode == Mode.ActiveGuidance && enableSpatialBeepInActive)
        {
            Vector3 safeDir = ComputeSafestDirection(out float safeDist);
            if (safeDist > 0.1f)
            {
                // Place beep "in the safe direction" (like an audio arrow)
                Vector3 pos = headCamera.position + safeDir * Mathf.Min(2.0f, Mathf.Max(0.8f, safeDist));
                BeepAt(pos);

                // Optional: if boxed in, a short voice hint (rare)
                if (safeDist <= boxedInDistance && (Time.time - _lastVoice) >= (voiceCooldown + 1.0f))
                {
                    tts?.Speak("Spațiu îngust. Încearcă să încetinești și să te întorci ușor.");
                    _lastVoice = Time.time;
                }
            }
        }
    }

    private bool TryForwardHit(out float dist, out Vector3 hitPoint)
    {
        dist = 0f;
        hitPoint = Vector3.zero;

        Vector3 origin = headCamera.position + headCamera.up * verticalOffset;
        Vector3 dir = headCamera.forward;

        int count = Physics.SphereCastNonAlloc(
            origin, sphereRadius, dir, _hits, maxDistance,
            obstacleLayers, QueryTriggerInteraction.Ignore
        );

        if (count <= 0) return false;

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

        dist = Mathf.Clamp(best, 0.05f, maxDistance);
        hitPoint = nearest.point;
        return true;
    }

    private void SpeakDanger(float distance)
    {
        float rounded = Mathf.Round(distance * 10f) / 10f;
        string msg = $"Atenție! Obstacol la {rounded.ToString("0.0", CultureInfo.InvariantCulture)} metri, în față.";
        tts?.Speak(msg);
        Debug.Log("[SafeWalkGuidanceMR] " + msg);
    }

    private void BeepAt(Vector3 worldPos)
    {
        if ((Time.time - _lastBeep) < beepCooldown) return;
        _lastBeep = Time.time;

        if (spatialBeepSource == null) return;
        spatialBeepSource.volume = beepVolume;
        spatialBeepSource.transform.position = worldPos;

        // Generate a quick beep if no clip assigned
        if (spatialBeepSource.clip == null)
            spatialBeepSource.clip = GenerateBeepClip();

        spatialBeepSource.Stop();
        spatialBeepSource.Play();
    }

    private Vector3 ComputeSafestDirection(out float bestDistance)
    {
        bestDistance = 0f;

        Vector3 origin = headCamera.position + headCamera.up * verticalOffset;

        float best = -1f;
        Vector3 bestDir = headCamera.forward;

        // sample angles left->right
        for (float ang = -guidanceAngleRange; ang <= guidanceAngleRange; ang += guidanceAngleStep)
        {
            Vector3 dir = Quaternion.AngleAxis(ang, Vector3.up) * headCamera.forward;

            float d = RayDistance(origin, dir);
            if (d > best)
            {
                best = d;
                bestDir = dir;
            }
        }

        bestDistance = Mathf.Max(0f, best);
        bestDir.y = 0f;
        return bestDir.normalized;
    }

    private float RayDistance(Vector3 origin, Vector3 dir)
    {
        int count = Physics.SphereCastNonAlloc(
            origin, sphereRadius, dir, _hits, maxDistance,
            obstacleLayers, QueryTriggerInteraction.Ignore
        );

        if (count <= 0) return maxDistance; // no hit = clear

        // nearest hit distance
        float best = _hits[0].distance;
        for (int i = 1; i < count; i++)
            if (_hits[i].distance < best) best = _hits[i].distance;

        return Mathf.Clamp(best, 0.05f, maxDistance);
    }

    // ======== TARGET GUIDANCE (MVP) ========
    // Set a target at the point you are looking at (on Spatial Mesh).
    public void SetTargetFromGaze()
    {
        if (headCamera == null) return;

        Vector3 origin = headCamera.position;
        Vector3 dir = headCamera.forward;

        if (Physics.Raycast(origin, dir, out RaycastHit hit, 6f, obstacleLayers, QueryTriggerInteraction.Ignore))
        {
            if (target == null)
            {
                var go = new GameObject("UserTarget");
                target = go.transform;
            }

            target.position = hit.point;
            targetGuidanceEnabled = true;

            tts?.Speak("Țintă setată. Te ghidez prin sunet.");
            Debug.Log("[SafeWalkGuidanceMR] Target set at " + hit.point);
        }
        else
        {
            tts?.Speak("Nu pot seta ținta. Uită-te spre o suprafață apropiată.");
        }
    }

    public void StopTargetGuidance()
    {
        targetGuidanceEnabled = false;
        tts?.Speak("Am oprit ghidarea către țintă.");
    }

    private void GuideToTarget()
    {
        if (target == null) return;

        Vector3 to = target.position - headCamera.position;
        float dist = to.magnitude;

        if (dist < 0.6f)
        {
            // arrived
            targetGuidanceEnabled = false;
            tts?.Speak("Ai ajuns la țintă.");
            return;
        }

        // place a beep "in the direction of target"
        Vector3 dir = to.normalized;
        Vector3 pos = headCamera.position + dir * Mathf.Min(2.0f, dist);
        BeepAt(pos);
    }

    // Simple generated beep clip
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