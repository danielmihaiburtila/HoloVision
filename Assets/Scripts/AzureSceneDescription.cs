using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class AzureSceneDescription : MonoBehaviour
{
    [Header("Azure Vision Settings")]
    public string subscriptionKey = "CHEIA_TA_VISION";
    public string endpoint = "https://nume.cognitiveservices.azure.com/";

    [Header("Translation")]
    public AzureTranslator translator;

    [Header("Dependencies")]
    public PhotoCaptureManager captureManager;
    public AzureTTS tts;
    public SpeechUIAnimator uiAnimator;

    [Header("Behavior")]
    [Range(0, 10)] public int topTagsToSpeak = 5;
    [Range(0f, 1f)] public float lowConfidenceThreshold = 0.55f;

    [Header("Debug")]
    public bool logResponses = false;

    private string visionUrl;

    [Serializable] private class VisionAnalyzeRoot { public VisionDescription description; public VisionTag[] tags; }
    [Serializable] private class VisionDescription { public VisionCaption[] captions; }
    [Serializable] private class VisionCaption { public string text; public float confidence; }
    [Serializable] private class VisionTag { public string name; public float confidence; }

    private void Awake()
    {
        AutoWireIfMissing();
        BuildUrl();
    }

    private void OnEnable()
    {
        AutoWireIfMissing();
        BuildUrl();
    }

    private void AutoWireIfMissing()
    {
        if (captureManager == null) captureManager = FindObjectOfType<PhotoCaptureManager>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        if (uiAnimator == null) uiAnimator = FindObjectOfType<SpeechUIAnimator>(true);
        if (translator == null) translator = FindObjectOfType<AzureTranslator>(true);
    }

    private void BuildUrl()
    {
        if (string.IsNullOrWhiteSpace(endpoint)) { visionUrl = null; return; }
        visionUrl = $"{endpoint.TrimEnd('/')}/vision/v3.2/analyze?visualFeatures=Description,Tags&language=en";
    }

    public void AnalyzeScene()
    {
        AutoWireIfMissing();
        BuildUrl();

        if (captureManager == null)
        {
            Feedback("Eroare: PhotoCaptureManager nu este disponibil.", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("CHEIA") ||
            string.IsNullOrWhiteSpace(visionUrl) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Feedback("Eroare: Azure Vision nu este configurat (cheie/endpoint).", error: true);
            return;
        }

        Feedback("Comandă recepționată: DESCRIERE. Analizez mediul, te rog nu te mișca.", processing: true);

        captureManager.TakePhoto(bytes =>
        {
            if (bytes == null || bytes.Length < 1000)
            {
                Feedback("Nu am putut captura o imagine (camera ocupată sau fără permisiune WebCam).", error: true);
                return;
            }
            StartCoroutine(UploadAnalyzeTranslateAndSpeak(bytes));
        });
    }

    private IEnumerator UploadAnalyzeTranslateAndSpeak(byte[] bytes)
    {
        using (UnityWebRequest request = new UnityWebRequest(visionUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            request.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);
            request.timeout = 10;

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogError($"[AzureSceneDescription] FAIL code={request.responseCode} err={request.error} body={body}");
                Feedback($"Nu am putut analiza imaginea. Cod: {request.responseCode}", error: true);
                yield break;
            }

            string json = request.downloadHandler.text;
            if (logResponses) Debug.Log("[AzureSceneDescription] Vision response: " + json);

            if (!TryParseAnalyze(json, out string captionEn, out float captionConf, out string[] tagsEn))
            {
                Feedback("Nu pot descrie clar mediul. Încearcă mai multă lumină.", error: true);
                yield break;
            }

            string messageEn = BuildNaturalMessageEnglish(captionEn, captionConf, tagsEn);

            // dacă nu ai translator, vorbește în engleză (fallback)
            if (translator == null)
            {
                Feedback("Văd următoarele: " + messageEn, success: true);
                yield break;
            }

            Feedback("Am detectat scena. Traduc în română.", processing: true);

            string ro = null;
            yield return StartCoroutine(translator.TranslateToRomanian(messageEn, result => ro = result));

            string finalText = string.IsNullOrWhiteSpace(ro) ? messageEn : ro;
            Feedback("Văd următoarele: " + finalText, success: true);
        }
    }

    private bool TryParseAnalyze(string json, out string caption, out float captionConf, out string[] topTags)
    {
        caption = null;
        captionConf = 0f;
        topTags = null;

        if (string.IsNullOrWhiteSpace(json)) return false;

        try
        {
            var root = JsonUtility.FromJson<VisionAnalyzeRoot>(json);
            if (root == null) return false;

            if (root.description != null && root.description.captions != null && root.description.captions.Length > 0)
            {
                caption = root.description.captions[0].text;
                captionConf = root.description.captions[0].confidence;
            }

            if (root.tags != null && root.tags.Length > 0 && topTagsToSpeak > 0)
            {
                Array.Sort(root.tags, (a, b) => b.confidence.CompareTo(a.confidence));
                int n = Mathf.Min(topTagsToSpeak, root.tags.Length);
                topTags = new string[n];
                for (int i = 0; i < n; i++) topTags[i] = root.tags[i].name;
            }

            return !string.IsNullOrWhiteSpace(caption) || (topTags != null && topTags.Length > 0);
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureSceneDescription] Parse exception: " + e.Message);
            return false;
        }
    }

    private string BuildNaturalMessageEnglish(string captionEn, float conf, string[] tagsEn)
    {
        string baseCaption = string.IsNullOrWhiteSpace(captionEn) ? "something in front of you" : captionEn.Trim();
        bool low = conf > 0f && conf < lowConfidenceThreshold;

        string msg = low ? $"It looks like {baseCaption}." : $"In front of you, I see {baseCaption}.";

        if (tagsEn != null && tagsEn.Length > 0)
        {
            int n = Mathf.Min(2, tagsEn.Length);
            if (n == 1) msg += $" Possibly related to {tagsEn[0]}.";
            if (n == 2) msg += $" Possibly related to {tagsEn[0]} and {tagsEn[1]}.";
        }

        return msg;
    }

    private void Feedback(string message, bool processing = false, bool success = false, bool error = false)
    {
        if (uiAnimator != null)
        {
            if (error) uiAnimator.ShowError(message);
            else if (success) uiAnimator.ShowSuccess(message);
            else if (processing) uiAnimator.ShowProcessing(message);
            else uiAnimator.ShowUI(message);
        }
        tts?.Speak(message);
    }
}