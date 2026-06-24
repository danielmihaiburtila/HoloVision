using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class HttpEmergencySender : MonoBehaviour, IEmergencySender
{
    [Tooltip("Ex: https://your-server.com/api/sos")]
    public string serverUrl = "https://YOUR_SERVER/api/sos";

    public int timeoutSeconds = 12;
    public bool debugLogs = true;

    public void SendEmergency(EmergencyPayload payload)
    {
        if (payload == null)
        {
            Debug.LogError("[HttpEmergencySender] payload NULL.");
            return;
        }

        if (string.IsNullOrWhiteSpace(serverUrl) || !serverUrl.StartsWith("http"))
        {
            Debug.LogError("[HttpEmergencySender] serverUrl invalid.");
            return;
        }

        string json = JsonUtility.ToJson(payload);
        if (debugLogs) Debug.Log("[HttpEmergencySender] POST " + serverUrl);

        StartCoroutine(Post(serverUrl, json));
    }

    private System.Collections.IEnumerator Post(string url, string json)
    {
        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("X-App", "HoloVision");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[HttpEmergencySender] FAIL code={req.responseCode} err={req.error} body={req.downloadHandler?.text}");
                yield break;
            }

            if (debugLogs) Debug.Log("[HttpEmergencySender] OK: " + req.downloadHandler.text);
        }
    }
}