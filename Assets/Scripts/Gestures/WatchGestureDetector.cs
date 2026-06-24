using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;

public class WatchGestureDetector : MonoBehaviour
{
    [Header("Tuning")]
    public float maxDistanceFromHead = 0.85f;
    public bool requireInFrontOfCamera = true;
    [Range(0.2f, 0.95f)] public float inFrontDotThreshold = 0.42f;

    [Tooltip("Cât de aproape de centrul privirii trebuie să fie încheietura (0..1 din viewport).")]
    [Range(0.05f, 0.6f)] public float maxViewportRadius = 0.26f;

    [Header("Orientation (robust)")]
    [Tooltip("Prag strict: cât de mult trebuie să fie DOSUL mâinii spre cameră. Mai mare = mai greu de declanșat.")]
    [Range(0.3f, 0.98f)] public float minBackOfHandFacingDot = 0.60f;

    [Tooltip("Dacă pe device gestul se declanșează doar când întorci mâna invers, bifează asta.")]
    public bool invertBackOfHandTest = false;

    [Header("Stability (recommended)")]
    public float poseHoldSeconds = 0.07f;
    public float poseDecaySeconds = 0.08f;

    [Header("One-shot")]
    public bool requireReleaseToRearm = true;
    public float rearmReleaseSeconds = 0.18f;

    [Header("Debug")]
    public bool debugLogs = true;

    [Header("Event")]
    public UnityEvent OnWatchGesture;

    private HandsAggregatorSubsystem _hands;

    private bool _latched;
    private float _lastNotPoseTime;
    private float _poseAccum;

    private void Start()
    {
        _hands = XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
        _latched = false;
        _lastNotPoseTime = 0f;
        _poseAccum = 0f;
    }

    private void Update()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (_hands == null)
        {
            _hands = XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
            if (_hands == null) return;
        }

        bool poseNow = IsWatchPose(cam, XRNode.LeftHand);

        if (poseNow)
            _poseAccum = Mathf.Min(poseHoldSeconds, _poseAccum + Time.deltaTime);
        else
            _poseAccum = Mathf.Max(
                0f,
                _poseAccum - (Time.deltaTime * (poseHoldSeconds / Mathf.Max(0.01f, poseDecaySeconds)))
            );

        bool poseStable = _poseAccum >= poseHoldSeconds;

        if (!poseStable)
        {
            if (_latched)
            {
                if (_lastNotPoseTime <= 0f) _lastNotPoseTime = Time.time;

                if (Time.time - _lastNotPoseTime >= rearmReleaseSeconds)
                {
                    _latched = false;
                    _lastNotPoseTime = 0f;
                }
            }
            return;
        }
        else
        {
            _lastNotPoseTime = 0f;
        }

        if (requireReleaseToRearm)
        {
            if (_latched) return;
            _latched = true;
        }

        if (GestureGate.Instance != null)
        {
            if (!GestureGate.Instance.TryConsume(GestureType.Watch))
                return;
        }

        if (debugLogs) Debug.Log("[WatchGestureDetector] WATCH gesture detected.");
        OnWatchGesture?.Invoke();
    }

    private bool IsWatchPose(Camera cam, XRNode handNode)
    {
        if (!_hands.TryGetJoint(TrackedHandJoint.Wrist, handNode, out HandJointPose wristPose))
            return false;

        if (!_hands.TryGetJoint(TrackedHandJoint.Palm, handNode, out HandJointPose palmPose))
            return false;

        float d = Vector3.Distance(cam.transform.position, wristPose.Position);
        if (d > maxDistanceFromHead) return false;

        if (requireInFrontOfCamera)
        {
            Vector3 toWrist = (wristPose.Position - cam.transform.position).normalized;
            float frontDot = Vector3.Dot(cam.transform.forward, toWrist);
            if (frontDot < inFrontDotThreshold) return false;
        }

        Vector3 vp = cam.WorldToViewportPoint(wristPose.Position);
        if (vp.z <= 0.05f) return false;

        float dx = vp.x - 0.5f;
        float dy = vp.y - 0.5f;
        if ((dx * dx + dy * dy) > (maxViewportRadius * maxViewportRadius)) return false;

        if (!_hands.TryGetJoint(TrackedHandJoint.IndexProximal, handNode, out HandJointPose idxProx)) return false;
        if (!_hands.TryGetJoint(TrackedHandJoint.LittleProximal, handNode, out HandJointPose litProx)) return false;

        Vector3 toCamera = (cam.transform.position - palmPose.Position).normalized;

        Vector3 a = (idxProx.Position - wristPose.Position);
        Vector3 b = (litProx.Position - wristPose.Position);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return false;

        Vector3 palmNormal = Vector3.Cross(b, a).normalized;
        float dot = Vector3.Dot(palmNormal, toCamera);

        bool backFacing = invertBackOfHandTest ? (dot > minBackOfHandFacingDot)
                                               : (dot < -minBackOfHandFacingDot);

        return backFacing;
    }
}