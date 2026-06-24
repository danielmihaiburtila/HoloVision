using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;

/// <summary>
/// Detectează gestul STOP (mână întinsă, orientată spre cameră, în fața camerei).
/// Întărit cu "guards" ca să NU se declanșeze accidental când utilizatorul ține o foaie/produs.
/// Nu afectează alte sisteme: emite doar OnStopGesture.
/// </summary>
public class StopGestureDetector : MonoBehaviour
{
    [Header("Gesture Tuning")]
    [Tooltip("Distanța maximă (m) dintre palmă și cap/cameră pentru a considera gestul.")]
    public float maxDistanceFromHead = 1.0f;

    [Range(0f, 1f)]
    [Tooltip("Prag vechi (folosit doar dacă dezactivezi orientarea robustă).")]
    public float minPalmFacing = 0.45f;

    public bool requireInFrontOfCamera = true;

    [Range(0.3f, 0.95f)]
    [Tooltip("Cât de mult trebuie să fie palma în fața camerei (dot(cam.forward, toPalm)).")]
    public float inFrontDotThreshold = 0.48f;

    [Header("Pose shape (open hand)")]
    public bool requireOpenHand = true;

    [Range(3, 5)]
    public int minExtendedFingers = 4;

    public float minExtensionDelta = 0.025f;
    public float minTipDistanceFromPalm = 0.06f;

    [Range(0.4f, 0.98f)]
    public float fingerStraightDot = 0.50f;

    [Header("Orientation like photo")]
    [Tooltip("Dacă e TRUE: așteptăm dosul mâinii spre cameră (cum ai zis). Dacă e FALSE: palma spre cameră.")]
    public bool requireBackOfHandFacingCamera = true;

    [Header("Stability (recommended)")]
    [Tooltip("Cât timp (sec) trebuie menținută poza ca să declanșeze.")]
    public float poseHoldSeconds = 0.06f;

    [Tooltip("Cât de repede 'se descarcă' acumularea când poza nu mai e detectată.")]
    public float poseDecaySeconds = 0.08f;

    [Header("One-shot")]
    public bool requireReleaseToRearm = true;
    public float rearmReleaseSeconds = 0.14f;

    [Header("Extra anti-false-positive guards")]
    [Tooltip("Cere ca palma să fie în zona centrală a ecranului (reduce trigger când ții foaia în lateral).")]
    public bool requirePalmNearScreenCenter = true;

    [Range(0.05f, 0.45f)]
    [Tooltip("Jumătate din lățimea/înălțimea box-ului central în viewport (0..1). 0.18 => box de 36% din ecran.")]
    public float centerBoxHalfSize = 0.22f;

    [Tooltip("Cere degete răsfirate (reduce trigger când prinzi/ții obiecte).")]
    public bool requireFingerSpread = true;

    [Range(0.03f, 0.20f)]
    [Tooltip("Distanța minimă (m) între IndexTip și LittleTip.")]
    public float minIndexToLittleTipDistance = 0.08f;

    [Range(0.3f, 0.95f)]
    [Tooltip("Prag strict pentru orientarea robustă (normală palmă/dos). Mai mare = mai greu de declanșat, mai puține false-positive.")]
    public float minFacingDotStrict = 0.62f;

    [Header("Debug")]
    public bool debugLogs = true;

    [Header("Event")]
    public UnityEvent OnStopGesture;

    private HandsAggregatorSubsystem _hands;
    private bool _loggedNoCam;
    private bool _loggedNoHandsSubsystem;

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
        if (cam == null)
        {
            if (debugLogs && !_loggedNoCam)
            {
                Debug.LogError("[StopGestureDetector] Camera.main este NULL. Verifică tag-ul MainCamera pe camera din MRTK XR Rig.");
                _loggedNoCam = true;
            }
            return;
        }

        if (_hands == null)
        {
            _hands = XRSubsystemHelpers.GetFirstRunningSubsystem<HandsAggregatorSubsystem>();
            if (_hands == null)
            {
                if (debugLogs && !_loggedNoHandsSubsystem)
                {
                    Debug.LogError("[StopGestureDetector] HandsAggregatorSubsystem NU rulează. Hand tracking e probabil dezactivat.");
                    _loggedNoHandsSubsystem = true;
                }
                return;
            }
        }

        bool poseNow =
            IsStopPose(cam, XRNode.RightHand) ||
            IsStopPose(cam, XRNode.LeftHand);

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
            if (!GestureGate.Instance.TryConsume(GestureType.Stop))
                return;
        }

        if (debugLogs) Debug.Log("[StopGestureDetector] STOP gesture detected.");
        OnStopGesture?.Invoke();
    }

    private bool IsStopPose(Camera cam, XRNode handNode)
    {
        if (!_hands.TryGetJoint(TrackedHandJoint.Palm, handNode, out HandJointPose palmPose))
            return false;

        if (requirePalmNearScreenCenter)
        {
            Vector3 vp = cam.WorldToViewportPoint(palmPose.Position);
            if (vp.z <= 0f) return false;

            float cx = 0.5f, cy = 0.5f;
            if (Mathf.Abs(vp.x - cx) > centerBoxHalfSize) return false;
            if (Mathf.Abs(vp.y - cy) > centerBoxHalfSize) return false;
        }

        float d = Vector3.Distance(cam.transform.position, palmPose.Position);
        if (d > maxDistanceFromHead) return false;

        if (requireInFrontOfCamera)
        {
            Vector3 toPalm = (palmPose.Position - cam.transform.position).normalized;
            float frontDot = Vector3.Dot(cam.transform.forward, toPalm);
            if (frontDot < inFrontDotThreshold) return false;
        }

        Vector3 toCamera = (cam.transform.position - palmPose.Position).normalized;

        if (!_hands.TryGetJoint(TrackedHandJoint.Wrist, handNode, out HandJointPose wristPose)) return false;
        if (!_hands.TryGetJoint(TrackedHandJoint.IndexProximal, handNode, out HandJointPose idxProx)) return false;
        if (!_hands.TryGetJoint(TrackedHandJoint.LittleProximal, handNode, out HandJointPose litProx)) return false;

        Vector3 a = (idxProx.Position - wristPose.Position);
        Vector3 b = (litProx.Position - wristPose.Position);
        if (a.sqrMagnitude < 1e-6f || b.sqrMagnitude < 1e-6f) return false;

        Vector3 palmNormal = Vector3.Cross(b, a).normalized;
        float dot = Vector3.Dot(palmNormal, toCamera);

        if (requireBackOfHandFacingCamera)
        {
            if (dot > -minFacingDotStrict) return false;
        }
        else
        {
            if (dot < minFacingDotStrict) return false;
        }

        if (requireOpenHand)
        {
            int extended = CountExtendedFingers(handNode, palmPose.Position);
            if (extended < minExtendedFingers) return false;
        }

        if (requireFingerSpread)
        {
            if (!_hands.TryGetJoint(TrackedHandJoint.IndexTip, handNode, out HandJointPose indexTip)) return false;
            if (!_hands.TryGetJoint(TrackedHandJoint.LittleTip, handNode, out HandJointPose littleTip)) return false;

            float spread = Vector3.Distance(indexTip.Position, littleTip.Position);
            if (spread < minIndexToLittleTipDistance) return false;
        }

        return true;
    }

    private int CountExtendedFingers(XRNode handNode, Vector3 palmPos)
    {
        int count = 0;

        if (IsFingerExtended(handNode, TrackedHandJoint.IndexProximal, TrackedHandJoint.IndexTip, palmPos)) count++;
        if (IsFingerExtended(handNode, TrackedHandJoint.MiddleProximal, TrackedHandJoint.MiddleTip, palmPos)) count++;
        if (IsFingerExtended(handNode, TrackedHandJoint.RingProximal, TrackedHandJoint.RingTip, palmPos)) count++;
        if (IsFingerExtended(handNode, TrackedHandJoint.LittleProximal, TrackedHandJoint.LittleTip, palmPos)) count++;
        if (IsFingerExtended(handNode, TrackedHandJoint.ThumbProximal, TrackedHandJoint.ThumbTip, palmPos)) count++;

        return count;
    }

    private bool IsFingerExtended(XRNode handNode, TrackedHandJoint proximal, TrackedHandJoint tip, Vector3 palmPos)
    {
        if (!_hands.TryGetJoint(proximal, handNode, out HandJointPose proxPose)) return false;
        if (!_hands.TryGetJoint(tip, handNode, out HandJointPose tipPose)) return false;

        float tipDist = Vector3.Distance(palmPos, tipPose.Position);
        float proxDist = Vector3.Distance(palmPos, proxPose.Position);

        if (tipDist < minTipDistanceFromPalm) return false;
        if ((tipDist - proxDist) < minExtensionDelta) return false;

        Vector3 v1 = (tipPose.Position - proxPose.Position).normalized;
        Vector3 v2 = (tipPose.Position - palmPos).normalized;
        float straight = Vector3.Dot(v1, v2);
        if (straight < fingerStraightDot) return false;

        return true;
    }
}