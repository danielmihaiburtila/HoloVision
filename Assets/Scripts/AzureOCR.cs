using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AzureOCR : MonoBehaviour
{
    [Header("Azure Settings (Computer Vision)")]
    [Tooltip("Cheia din resursa Azure Computer Vision (sau Azure AI Services care include Vision).")]
    public string subscriptionKey = "PUT_VISION_KEY_HERE";

    [Tooltip("Ex: https://<resource-name>.cognitiveservices.azure.com/")]
    public string endpoint = "https://nume.cognitiveservices.azure.com/";

    [Header("Dependencies")]
    public PhotoCaptureManager captureManager;
    public AzureTTS tts;
    public SpeechUIAnimator uiAnimator;

    [Header("Behavior")]
    [Tooltip("Timeout request OCR (secunde).")]
    public int timeoutSeconds = 20;

    [Tooltip("Dacă true, loghează response JSON complet (atenție: e lung).")]
    public bool logResponses = false;

    private string ocrUrl;
    private bool busy;

    // ====== MODELE JSON pentru /vision/v3.2/ocr ======
    [Serializable] private class OcrRoot { public OcrRegion[] regions; }
    [Serializable] private class OcrRegion { public OcrLine[] lines; }
    [Serializable] private class OcrLine { public OcrWord[] words; }
    [Serializable] private class OcrWord { public string text; }

    private void Start()
    {
        // Endpoint-ul clasic OCR (v3.2). language nu e necesar; detectOrientation ajută.
        ocrUrl = $"{endpoint.TrimEnd('/')}/vision/v3.2/ocr?detectOrientation=true";
        Debug.Log("[AzureOCR] OCR URL: " + ocrUrl);
    }

    public void ReadText()
    {
        Debug.Log("[AzureOCR] ReadText() CALLED");

        // 1) Anti-spam / anti-double-call
        if (busy)
        {
            ExecuteFeedback("Sunt deja în procesul de citire. Te rog așteaptă.", processing: true);
            Debug.LogWarning("[AzureOCR] Busy - ignoring new request.");
            return;
        }

        // 2) Validări
        if (captureManager == null)
        {
            ExecuteFeedback("Eroare: CameraManager nu este legat la OCR_System.", error: true);
            Debug.LogError("[AzureOCR] captureManager is NULL. Drag CameraManager (PhotoCaptureManager) in Inspector!");
            return;
        }

        if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("PUT_") || subscriptionKey.Contains("CHEIA_TA"))
        {
            ExecuteFeedback("Eroare: cheia de Computer Vision (OCR) nu este setată.", error: true);
            Debug.LogError("[AzureOCR] subscriptionKey is missing/placeholder.");
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            ExecuteFeedback("Eroare: endpoint-ul de Computer Vision nu este setat corect.", error: true);
            Debug.LogError("[AzureOCR] endpoint invalid: " + endpoint);
            return;
        }

        // 3) Feedback imediat (important pentru nevăzător)
        ExecuteFeedback("Comandă recepționată: CITIRE. Citesc textul, te rog nu te mișca.", processing: true);

        busy = true;

        // 4) Capture photo
        captureManager.TakePhoto(bytes =>
        {
            if (bytes == null || bytes.Length < 1000)
            {
                busy = false;
                ExecuteFeedback("Eroare: nu am primit o imagine validă de la cameră.", error: true);
                Debug.LogError($"[AzureOCR] Invalid photo bytes. bytes={(bytes == null ? 0 : bytes.Length)}");
                return;
            }

            StartCoroutine(UploadAndParse(bytes));
        });
    }

    private IEnumerator UploadAndParse(byte[] bytes)
    {
        using (UnityWebRequest req = new UnityWebRequest(ocrUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            Debug.Log($"[AzureOCR] Uploading image bytes={bytes.Length}");

            yield return req.SendWebRequest();

            busy = false;

            if (req.result != UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.LogError($"[AzureOCR] FAIL code={req.responseCode} err={req.error} body={body}");
                ExecuteFeedback($"Eroare la citirea textului. Cod: {req.responseCode}", error: true);
                yield break;
            }

            string json = req.downloadHandler.text;
            if (logResponses) Debug.Log("[AzureOCR] OCR response: " + json);

            // 1) Parse robust (JsonUtility) + fallback string-search
            string text = ParseOcrJson(json);
            if (string.IsNullOrWhiteSpace(text))
                text = ParseOCR_Fallback(json);

            if (string.IsNullOrWhiteSpace(text))
            {
                ExecuteFeedback("Nu am găsit text clar. Încearcă mai multă lumină și apropie textul.", success: true, processing: false);
                yield break;
            }

            // Curățare minimă (spații duble)
            text = CleanupText(text);

            ExecuteFeedback("Am citit: " + text, success: true, processing: false);
        }
    }

    /// <summary>
    /// Parse OCR JSON cu JsonUtility (mai sigur decât string search).
    /// </summary>
    private string ParseOcrJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var root = JsonUtility.FromJson<OcrRoot>(json);
            if (root == null || root.regions == null || root.regions.Length == 0)
                return null;

            StringBuilder sb = new StringBuilder();

            foreach (var region in root.regions)
            {
                if (region?.lines == null) continue;

                foreach (var line in region.lines)
                {
                    if (line?.words == null) continue;

                    foreach (var w in line.words)
                    {
                        if (!string.IsNullOrWhiteSpace(w?.text))
                            sb.Append(w.text).Append(" ");
                    }

                    // separăm liniile cu pauză
                    sb.Append(". ");
                }
            }

            string result = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureOCR] ParseOcrJson exception: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// Fallback: caută toate "text":"..." în JSON (merge pe multe răspunsuri).
    /// </summary>
    private string ParseOCR_Fallback(string json)
    {
        StringBuilder sb = new StringBuilder();
        const string key = "\"text\":\"";
        int pos = 0;

        while ((pos = json.IndexOf(key, pos, StringComparison.Ordinal)) != -1)
        {
            pos += key.Length;
            int end = json.IndexOf("\"", pos, StringComparison.Ordinal);
            if (end != -1)
            {
                sb.Append(json.Substring(pos, end - pos)).Append(" ");
                pos = end;
            }
            else break;
        }

        string result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }

    private string CleanupText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        // înlocuiește spații multiple
        while (input.Contains("  "))
            input = input.Replace("  ", " ");
        return input.Trim();
    }

    private void ExecuteFeedback(string message, bool success = false, bool error = false, bool processing = true)
    {
        if (uiAnimator != null)
        {
            if (error) uiAnimator.ShowError(message);
            else if (success) uiAnimator.ShowSuccess(message);
            else if (processing) uiAnimator.ShowProcessing(message);
            else uiAnimator.ShowUI(message);
        }

        if (tts != null)
            tts.Speak(message);
    }
}