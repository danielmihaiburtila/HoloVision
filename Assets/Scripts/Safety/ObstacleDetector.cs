using System;
using UnityEngine;

public class ObstacleDetector : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Transform-ul camerei HoloLens (Main Camera din MRTK XR Rig).")]
    public Transform head;

    [Tooltip("Root de ignorat (ex: MRTK XR Rig) ca sa nu lovesti propriile obiecte/UI).")]
    public Transform rootToIgnore;

    [Header("Layers")]
    [Tooltip("Layer-urile pe care vrei sa detectezi obstacole (ideal spatial mesh / environment).")]
    public LayerMask obstacleLayers = ~0;

    [Header("Detection")]
    [Min(0.5f)] public float maxDistance = 3.0f;
    [Min(0.05f)] public float sphereRadius = 0.15f;
    [Range(0.02f, 0.5f)] public float scanInterval = 0.10f; // 10 Hz

    [Header("Fan Angles (degrees)")]
    public float[] fanAngles = new float[] { -40f, -20f, 0f, 20f, 40f };

    [Header("Vertical Offsets (meters relative to head)")]
    [Tooltip("Offset-uri relative la head.position. Ex: -0.35, -0.75, -1.10")]
    public float[] verticalOffsets = new float[] { -0.35f, -0.75f, -1.10f };

    [Header("Filtering")]
    public bool ignoreFloorByNormal = true;
    [Range(0.5f, 0.99f)] public float floorNormalYThreshold = 0.75f;

    [Tooltip("Ignora hit-uri mult deasupra capului (ex: tavan)")]
    public bool limitVerticalHitWindow = true;
    public float maxAboveHead = 0.25f;
    public float maxBelowHead = 1.35f;

    [Header("Debug")]
    public bool debugDraw = false;
    public bool debugLogs = false;

    public event Action<SafetySnapshot> OnSnapshot;

    private float _nextScanTime;
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    public bool IsRunning { get; private set; } = true;

    public void SetRunning(bool running)
    {
        IsRunning = running;
    }

    private void Update()
    {
        if (!IsRunning) return;
        if (head == null) return;
        if (Time.time < _nextScanTime) return;

        _nextScanTime = Time.time + scanInterval;
        Scan();
    }

    private void Scan()
    {
        if (fanAngles == null || fanAngles.Length == 0) return;
        if (verticalOffsets == null || verticalOffsets.Length == 0) return;

        float nearestDistance = maxDistance;
        float nearestAngle = 0f;
        bool hasObstacle = false;

        float centerDistance = maxDistance;

        float bestClearance = -1f;
        float bestAngle = 0f;

        Vector3 flatForward = head.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = head.forward;
        flatForward.Normalize();

        for (int i = 0; i < fanAngles.Length; i++)
        {
            float angle = fanAngles[i];
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * flatForward;
            dir.Normalize();

            float sectorMinDistance = maxDistance;

            for (int j = 0; j < verticalOffsets.Length; j++)
            {
                Vector3 origin = head.position + Vector3.up * verticalOffsets[j];

                int hitCount = Physics.SphereCastNonAlloc(
                    origin,
                    sphereRadius,
                    dir,
                    _hits,
                    maxDistance,
                    obstacleLayers,
                    QueryTriggerInteraction.Ignore
                );

                float localMin = maxDistance;

                for (int h = 0; h < hitCount; h++)
                {
                    RaycastHit hit = _hits[h];
                    if (!IsHitValid(hit)) continue;

                    if (hit.distance < localMin)
                        localMin = hit.distance;
                }

                if (localMin < sectorMinDistance)
                    sectorMinDistance = localMin;

                if (debugDraw)
                {
                    Color c = (localMin < maxDistance) ? Color.red : Color.green;
                    Debug.DrawRay(origin, dir * Mathf.Min(localMin, maxDistance), c, scanInterval);
                }
            }

            // "clearance" = cat de liber e pe directia asta
            float clearance = sectorMinDistance;

            if (clearance > bestClearance)
            {
                bestClearance = clearance;
                bestAngle = angle;
            }

            if (Mathf.Abs(angle) < 0.01f)
                centerDistance = sectorMinDistance;

            if (sectorMinDistance < nearestDistance)
            {
                nearestDistance = sectorMinDistance;
                nearestAngle = angle;
            }

            if (sectorMinDistance < maxDistance)
                hasObstacle = true;
        }

        SafetySnapshot snapshot = new SafetySnapshot
        {
            HasObstacle = hasObstacle,
            NearestDistance = nearestDistance,
            NearestAngle = nearestAngle,
            CenterDistance = centerDistance,
            RecommendedAngle = bestAngle,
            Timestamp = Time.time
        };

        if (debugLogs)
        {
            Debug.Log($"[ObstacleDetector] center={snapshot.CenterDistance:0.00}m, nearest={snapshot.NearestDistance:0.00}m @ {snapshot.NearestAngle:0}°, recommended={snapshot.RecommendedAngle:0}°");
        }

        OnSnapshot?.Invoke(snapshot);
    }

    private bool IsHitValid(RaycastHit hit)
    {
        if (hit.collider == null) return false;

        Transform t = hit.collider.transform;

        // ignora propriile obiecte (rig / ui / main canvas) daca sunt sub rootToIgnore
        if (rootToIgnore != null && t.IsChildOf(rootToIgnore))
            return false;

        // ignora podeaua (aproximativ) dupa normal
        if (ignoreFloorByNormal && hit.normal.y > floorNormalYThreshold)
            return false;

        // ignora tavan / hit-uri prea sus sau prea jos fata de cap
        if (limitVerticalHitWindow && head != null)
        {
            float dy = hit.point.y - head.position.y;
            if (dy > maxAboveHead) return false;
            if (dy < -maxBelowHead) return false;
        }

        return true;
    }
}