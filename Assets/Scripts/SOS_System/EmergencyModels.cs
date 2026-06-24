using System;
using UnityEngine;

public interface IEmergencySender
{
    void SendEmergency(EmergencyPayload payload);
}

[Serializable]
public class EmergencyPayload
{
    public string reason;         // ex: "no_response" / "user_requested_help"
    public string timestampUtc;   // ISO 8601
    public string app;            // "HoloVision"
    public string device;         // "HoloLens2"
    public string sessionId;      // optional

    // optional (dacă implementezi locația / server side)
    public double? latitude;
    public double? longitude;
    public float? accuracyMeters;
}