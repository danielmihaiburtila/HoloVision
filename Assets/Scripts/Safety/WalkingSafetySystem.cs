using UnityEngine;

public class WalkingSafetySystem : MonoBehaviour
{
    [Header("References")]
    public Camera userCamera;
    public AzureTTS azureTTS;
    public SpeechUIAnimator speechUI;

    [Header("Detection")]
    [Tooltip("Distanța minimă de la care considerăm obstacolul relevant.")]
    public float minDistance = 0.6f;

    [Tooltip("Distanța maximă de verificare în față.")]
    public float maxDistance = 3.0f;

    [Tooltip("Raza spherecast-ului. Mai mare = mai robust, dar poate da false positive.")]
    public float sphereRadius = 0.20f;

    [Tooltip("La cât timp verificăm mediul.")]
    public float scanInterval = 0.30f;

    [Tooltip("Ce layere sunt considerate obstacole.")]
    public LayerMask obstacleLayers = ~0;

    [Tooltip("Ignoră trigger-ele.")]
    public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Header("Announcement anti-spam")]
    [Tooltip("Nu repeta mesajul mai des de atât.")]
    public float speakCooldown = 2.2f;

    [Tooltip("Reanunță doar dacă distanța s-a schimbat semnificativ.")]
    public float distanceChangeThreshold = 0.35f;

    [Tooltip("Dacă obstacolul este mai aproape decât atât, îl poți reanunța chiar dacă e același.")]
    public float urgentDistance = 0.9f;

    [Header("Direction thresholds")]
    [Tooltip("Prag pentru centru. Sub această valoare, anunțăm centru.")]
    public float centerThreshold = 0.18f;

    [Tooltip("Prag pentru ușor stânga/dreapta.")]
    public float slightThreshold = 0.45f;

    [Header("Debug")]
    public bool isRunning = false;
    public bool debugLogs = false;
    public bool drawDebug = true;

    private float _nextScanTime = 0f;
    private float _lastSpeakTime = -999f;

    private bool _hadObstacleLastTime = false;
    private float _lastAnnouncedDistance = -1f;
    private string _lastAnnouncedDirection = "";

    public bool IsRunning => isRunning;

    public void StartSafety()
    {
        isRunning = true;
        ResetState();

        if (speechUI != null)
            speechUI.ShowSuccess("Modul de siguranță la mers a fost pornit.");

        azureTTS?.Speak("Modul de siguranță la mers a fost pornit.");
    }

    public void StopSafety(bool speak = true)
    {
        isRunning = false;
        ResetState();

        if (speechUI != null)
            speechUI.ShowSuccess("Modul de siguranță a fost oprit.");

        if (speak)
            azureTTS?.Speak("Modul de siguranță a fost oprit.");
    }

    private void ResetState()
    {
        _nextScanTime = 0f;
        _lastSpeakTime = -999f;
        _hadObstacleLastTime = false;
        _lastAnnouncedDistance = -1f;
        _lastAnnouncedDirection = "";
    }

    private void Awake()
    {
        if (userCamera == null && Camera.main != null)
            userCamera = Camera.main;

        if (azureTTS == null)
            azureTTS = FindObjectOfType<AzureTTS>(true);

        if (speechUI == null)
            speechUI = FindObjectOfType<SpeechUIAnimator>(true);
    }

    private void Update()
    {
        if (!isRunning)
            return;

        if (userCamera == null)
            return;

        if (Time.time < _nextScanTime)
            return;

        _nextScanTime = Time.time + scanInterval;

        CheckObstacleAhead();
    }

    private void CheckObstacleAhead()
    {
        Vector3 origin = userCamera.transform.position;
        Vector3 forward = userCamera.transform.forward;

        // mic offset în față ca să evităm unele coliziuni ciudate chiar în cameră
        origin += forward * 0.05f;

        RaycastHit hit;
        bool hasHit = Physics.SphereCast(
            origin,
            sphereRadius,
            forward,
            out hit,
            maxDistance,
            obstacleLayers,
            triggerInteraction
        );

        if (drawDebug)
        {
            Debug.DrawRay(origin, forward * maxDistance, hasHit ? Color.red : Color.green, scanInterval);
        }

        if (!hasHit)
        {
            _hadObstacleLastTime = false;
            return;
        }

        float distance = hit.distance;

        if (distance < minDistance || distance > maxDistance)
        {
            _hadObstacleLastTime = false;
            return;
        }

        string direction = ComputeDirection(hit.point);

        if (debugLogs)
        {
            Debug.Log($"[WalkingSafety] Hit: {hit.collider.name}, dist={distance:F2}, dir={direction}");
        }

        bool shouldSpeak = ShouldAnnounce(distance, direction);

        _hadObstacleLastTime = true;

        if (!shouldSpeak)
            return;

        string msg = $"Obstacol la {distance.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} metri, {direction}.";

        if (speechUI != null)
            speechUI.ShowSuccess(msg);
        azureTTS?.Speak(msg);

        _lastSpeakTime = Time.time;
        _lastAnnouncedDistance = distance;
        _lastAnnouncedDirection = direction;
    }

    private bool ShouldAnnounce(float distance, string direction)
    {
        if (!_hadObstacleLastTime)
            return true;

        if (Time.time - _lastSpeakTime >= speakCooldown)
        {
            // Dacă e foarte aproape, reanunță
            if (distance <= urgentDistance)
                return true;

            // Dacă s-a schimbat direcția
            if (direction != _lastAnnouncedDirection)
                return true;

            // Dacă s-a schimbat mult distanța
            if (_lastAnnouncedDistance < 0f || Mathf.Abs(distance - _lastAnnouncedDistance) >= distanceChangeThreshold)
                return true;
        }

        return false;
    }

    private string ComputeDirection(Vector3 hitPoint)
    {
        Vector3 toHit = hitPoint - userCamera.transform.position;
        toHit.y = 0f;

        if (toHit.sqrMagnitude < 0.0001f)
            return "centru";

        toHit.Normalize();

        Vector3 camRight = userCamera.transform.right;
        camRight.y = 0f;
        camRight.Normalize();

        float side = Vector3.Dot(toHit, camRight);

        if (Mathf.Abs(side) < centerThreshold)
            return "centru";

        if (side <= -slightThreshold)
            return "stânga";

        if (side < -centerThreshold)
            return "ușor stânga";

        if (side >= slightThreshold)
            return "dreapta";

        return "ușor dreapta";
    }
}