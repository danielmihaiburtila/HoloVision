// WatchEventReceiver.cs
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class WatchEventReceiver : MonoBehaviour
{
    [Header("Server")]
    public string latestUrl = "http://127.0.0.1:8080/api/sos/latest";
    public float pollIntervalSeconds = 1.0f;

    [Tooltip("Ignoră evenimente mai vechi de X sec (ca să nu declanșeze la start).")]
    public float maxEventAgeSeconds = 20f;

    [Header("Device")]
    public string deviceId = "user1";

    [Header("Refs")]
    public SosFallSystem sos;

    [Header("Debug")]
    public bool debugLogs = true;

    private string _lastEventId = "";
    private bool _running;

    // ✅ NEW: la pornire “primim” ultimul event și îl marcăm văzut fără să întrebăm
    private bool _primed = false;

    private void Awake()
    {
        if (sos == null) sos = FindObjectOfType<SosFallSystem>(true);
    }

    private void OnEnable()
    {
        _running = true;
        StartCoroutine(PollLoop());
    }

    private void OnDisable()
    {
        _running = false;
        StopAllCoroutines();
    }

    private IEnumerator PollLoop()
    {
        yield return new WaitForSeconds(0.5f);

        while (_running)
        {
            yield return FetchLatest();
            yield return new WaitForSeconds(Mathf.Max(0.2f, pollIntervalSeconds));
        }
    }

    private IEnumerator FetchLatest()
    {
        if (string.IsNullOrWhiteSpace(latestUrl))
            yield break;

        using (var req = UnityWebRequest.Get(latestUrl))
        {
            req.timeout = 6;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (debugLogs) Debug.LogWarning("[WatchEventReceiver] GET fail: " + req.error);
                yield break;
            }

            var json = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
                yield break;

            SosLatestWrapper wrapper = null;
            try { wrapper = JsonUtility.FromJson<SosLatestWrapper>(json); } catch { }

            if (wrapper == null || !wrapper.ok || wrapper.latest == null)
                yield break;

            var e = wrapper.latest;

            // Filtru deviceId
            if (!string.IsNullOrEmpty(deviceId) && e.deviceId != deviceId)
                yield break;

            if (string.IsNullOrWhiteSpace(e.eventId))
                yield break;

            // ✅ ignoră event-uri deja decise
            if (!string.IsNullOrWhiteSpace(e.decision) || string.Equals(e.status, "decided", StringComparison.OrdinalIgnoreCase))
            {
                // îl marcăm văzut ca să nu tot încercăm
                _lastEventId = e.eventId;
                _primed = true;
                yield break;
            }

            // ✅ PRIMING: la primul fetch după start, NU declanșăm întrebarea
            if (!_primed)
            {
                _primed = true;
                _lastEventId = e.eventId;
                if (debugLogs) Debug.Log("[WatchEventReceiver] Primed with last eventId=" + _lastEventId + " (no ask on start)");
                yield break;
            }

            // dacă e același event, nu facem nimic
            if (e.eventId == _lastEventId)
                yield break;

            // Freshness
            if (!IsFreshEnough(e.timestampUtc))
            {
                if (debugLogs) Debug.Log("[WatchEventReceiver] Ignored old event: " + e.timestampUtc);
                _lastEventId = e.eventId;
                yield break;
            }

            _lastEventId = e.eventId;

            if (debugLogs) Debug.Log($"[WatchEventReceiver] NEW Event id={e.eventId} deviceId={e.deviceId}");

            if (sos != null)
            {
                var baseUrl = GuessBaseUrlFromLatestUrl(latestUrl);
                sos.SetExternalPending(e.eventId, e.deviceId, baseUrl);
                sos.AskNowFromExternalEvent("watch_fall_detected");
            }
        }
    }

    private string GuessBaseUrlFromLatestUrl(string url)
    {
        try
        {
            var u = new Uri(url);
            return u.Scheme + "://" + u.Host + (u.IsDefaultPort ? "" : (":" + u.Port));
        }
        catch
        {
            return "http://127.0.0.1:8080";
        }
    }

    private bool IsFreshEnough(string isoUtc)
    {
        if (string.IsNullOrWhiteSpace(isoUtc)) return false; // ✅ dacă nu e timp, mai bine ignorăm

        if (!DateTime.TryParse(isoUtc, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var t))
            return false;

        var age = DateTime.UtcNow - t.ToUniversalTime();
        return age.TotalSeconds <= maxEventAgeSeconds;
    }
}