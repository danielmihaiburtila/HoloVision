// SosApiModels.cs
using System;

[Serializable]
public class SosLatestWrapper
{
    public bool ok;
    public SosEvent latest;
}

[Serializable]
public class SosEvent
{
    public string deviceId;
    public string type;          // "fall_detected"
    public string reason;
    public string timestampUtc;  // ISO 8601
    public string eventId;
    public string status;        // "pending_hololens" / "decided"
    public string decision;      // "ok" / "help" / "no_response" (dupã ce e decis)
    public LocationPayload location;
}

[Serializable]
public class LocationPayload
{
    public double lat;
    public double lon;
    public float accuracyMeters;
}

[Serializable]
public class SosDecisionWrapper
{
    public bool ok;
    public SosDecision decision;
}

[Serializable]
public class SosDecision
{
    public string deviceId;
    public string eventId;
    public string decision;   // ok|help|no_response
    public string decidedUtc;
}