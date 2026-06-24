using System;
using UnityEngine;
using UnityEngine.Events;

public enum WalkingSafetyMode
{
    Off,
    Passive,
    Reduced,
    Guidance
}

[Serializable]
public struct SafetySnapshot
{
    public bool HasObstacle;
    public float NearestDistance;     // cel mai apropiat obstacol din fan
    public float NearestAngle;        // unghiul obstacolului cel mai apropiat
    public float CenterDistance;      // distanța pe direcția înainte (0 grade)
    public float RecommendedAngle;    // direcția cea mai liberă
    public float Timestamp;
}

[Serializable]
public class StringUnityEvent : UnityEvent<string> { }