using UnityEngine;

public enum GestureType
{
    Stop,
    Watch
}

/// <summary>
/// Gate global ca să prevenim:
/// - declanșări multiple în același timp
/// - conflicte între gesturi (ex: Watch și Stop în același moment)
/// - spam la frame-uri consecutive
/// </summary>
public class GestureGate : MonoBehaviour
{
    public static GestureGate Instance { get; private set; }

    [Header("Global lock (recommended)")]
    [Tooltip("După ce un gest a declanșat, blochează orice alt gest pentru X secunde.")]
    public float globalLockSeconds = 0.28f;

    private float _lockedUntil;
    private GestureType _lastGesture;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public bool TryConsume(GestureType gesture)
    {
        if (Time.time < _lockedUntil) return false;

        _lastGesture = gesture;
        _lockedUntil = Time.time + globalLockSeconds;
        return true;
    }

    public GestureType LastGesture => _lastGesture;
}