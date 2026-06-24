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
    public MenuFlowController menuUI;
    public VoiceCommandRouter router;
    public PhotoCaptureManager captureManager;
    public AzureTTS tts;
    public SpeechUIAnimator uiAnimator;

    [Header("Behavior")]
    [Range(0, 10)] public int topTagsToSpeak = 5;
    [Range(0f, 1f)] public float lowConfidenceThreshold = 0.55f;

    [Header("Debug")]
    public bool logResponses = false;

    private string visionUrl;

    private int cancelToken = 0;

    public void Cancel()
    {
        cancelToken++;
        StopAllCoroutines();
        tts?.StopNow();
        uiAnimator?.ShowUI("Am oprit descrierea.");
    }

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
        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);
        if (menuUI == null) menuUI = FindObjectOfType<MenuFlowController>(true);

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
        menuUI?.ShowProcessingPanel();

        int myToken = ++cancelToken;

        if (captureManager == null)
        {
            Feedback("Eroare: PhotoCaptureManager nu este disponibil.", error: true);
            router?.ReturnToFeaturesMenu();
            return;
        }

        if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("CHEIA") ||
            string.IsNullOrWhiteSpace(visionUrl) ||
            string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Feedback("Eroare: Azure Vision nu este configurat (cheie/endpoint).", error: true);
            router?.ReturnToFeaturesMenu();
            return;
        }

        Feedback("Comandă recepționată: DESCRIERE. Analizez mediul, te rog nu te mișca.", processing: true);

        captureManager.TakePhoto(bytes =>
        {
            if (myToken != cancelToken) return; // ✅ STOP între timp

            if (bytes == null || bytes.Length < 1000)
            {
                Feedback("Nu am putut captura o imagine (camera ocupată sau fără permisiune WebCam).", error: true);
                router?.ReturnToFeaturesMenu();
                return;
            }

            StartCoroutine(UploadAnalyzeTranslateAndSpeak(bytes, myToken));
        });
    }

    private IEnumerator UploadAnalyzeTranslateAndSpeak(byte[] bytes, int myToken)
    {
        using (UnityWebRequest request = new UnityWebRequest(visionUrl, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/octet-stream");
            request.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);
            request.timeout = 10;

            yield return request.SendWebRequest();

            // Dacă între timp s-a dat STOP / Cancel, nu mai afișăm nimic.
            if (myToken != cancelToken)
                yield break;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string body = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogError($"[AzureSceneDescription] FAIL code={request.responseCode} err={request.error} body={body}");

                if (myToken != cancelToken)
                    yield break;

                Feedback($"Nu am putut analiza imaginea. Cod: {request.responseCode}", error: true);

                if (router != null)
                    router.ReturnToFeaturesMenu();
                else
                    menuUI?.ShowFeaturesMenuAfterError();

                yield break;
            }

            string json = request.downloadHandler != null ? request.downloadHandler.text : "";

            if (logResponses)
                Debug.Log("[AzureSceneDescription] Vision response: " + json);

            if (myToken != cancelToken)
                yield break;

            if (!TryParseAnalyze(json, out string captionEn, out float captionConf, out string[] tagsEn))
            {
                if (myToken != cancelToken)
                    yield break;

                Feedback("Nu pot descrie clar mediul. Încearcă mai multă lumină.", error: true);

                if (router != null)
                    router.ReturnToFeaturesMenu();
                else
                    menuUI?.ShowFeaturesMenuAfterError();

                yield break;
            }

            if (myToken != cancelToken)
                yield break;

            string messageEn = BuildNaturalMessageEnglish(captionEn, captionConf, tagsEn);

            // Dacă nu există translator, folosim mesajul în engleză ca fallback.
            if (translator == null)
            {
                if (myToken != cancelToken)
                    yield break;

                ShowFinalDescriptionResult(messageEn);
                yield break;
            }

            //Feedback("Am detectat scena. Pregătesc descrierea finală.", processing: true);

            string ro = null;

            yield return StartCoroutine(translator.TranslateToRomanian(messageEn, result =>
            {
                ro = result;
            }));

            if (myToken != cancelToken)
                yield break;

            string finalText = string.IsNullOrWhiteSpace(ro) ? messageEn : ro.Trim();

            finalText = MakeAssistantSpeechFeminine(finalText);

            ShowFinalDescriptionResult(finalText);
        }
    }
    private void ShowFinalDescriptionResult(string finalText)
    {
        if (string.IsNullOrWhiteSpace(finalText))
            finalText = "Nu am putut genera o descriere clară a mediului.";

        // Varianta normală: routerul controlează fluxul aplicației.
        if (router != null)
        {
            router.ShowDescriptionResult(finalText);
            return;
        }

        // Fallback: dacă routerul lipsește sau nu e legat, afișăm direct panelul.
        if (menuUI != null)
        {
            menuUI.ShowDescriptionResult(finalText);
            tts?.Speak(finalText);
            return;
        }

        // Ultimul fallback: măcar afișăm prin SpeechUIAnimator și vorbim.
        Feedback("Văd următoarele: " + finalText, success: true);
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
        string baseCaption = string.IsNullOrWhiteSpace(captionEn)
            ? "something in front of you"
            : captionEn.Trim();

        bool lowConfidence = conf > 0f && conf < lowConfidenceThreshold;

        string message = lowConfidence
            ? "I am not completely sure, but in front of you there seems to be " + baseCaption + "."
            : "In front of you, I can see " + baseCaption + ".";

        string tagSummary = BuildTagSummaryEnglish(tagsEn);
        if (!string.IsNullOrWhiteSpace(tagSummary))
            message += " I also notice " + tagSummary + ".";

        string hazard = BuildPossibleHazardWarningEnglish(captionEn, tagsEn);
        if (!string.IsNullOrWhiteSpace(hazard))
            message += " " + hazard;
        else
            message += " I do not notice an obvious danger in this image. Move carefully.";

        return message;
    }
    private string BuildTagSummaryEnglish(string[] tagsEn)
    {
        if (tagsEn == null || tagsEn.Length == 0)
            return "";

        int n = Mathf.Min(3, tagsEn.Length);

        if (n == 1)
            return tagsEn[0];

        if (n == 2)
            return tagsEn[0] + " and " + tagsEn[1];

        return tagsEn[0] + ", " + tagsEn[1] + " and " + tagsEn[2];
    }

    private string BuildPossibleHazardWarningEnglish(string captionEn, string[] tagsEn)
    {
        string combined = "";

        if (!string.IsNullOrWhiteSpace(captionEn))
            combined += " " + captionEn.ToLowerInvariant();

        if (tagsEn != null)
        {
            for (int i = 0; i < tagsEn.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(tagsEn[i]))
                    combined += " " + tagsEn[i].ToLowerInvariant();
            }
        }

        if (ContainsAny(combined, "stair", "stairs", "step", "steps", "staircase"))
            return "Attention: I may see stairs or steps ahead. Stop for a moment and check carefully before moving.";

        if (ContainsAny(combined, "hole", "pit", "pothole", "ditch", "crack", "uneven"))
            return "Attention: the ground may be uneven or unsafe. Move very slowly and check carefully before stepping.";

        if (ContainsAny(combined, "curb", "kerb", "sidewalk", "pavement", "crosswalk"))
            return "Attention: you may be near a sidewalk, curb, or crossing area. Stop and check your surroundings before moving forward.";

        if (ContainsAny(combined, "car", "vehicle", "bus", "truck", "bicycle", "motorcycle", "scooter", "road", "street"))
            return "Attention: I may see a vehicle or street area nearby. Do not move forward until you are sure it is safe.";

        if (ContainsAny(combined, "pole", "post", "sign", "traffic light", "lamp post", "barrier", "fence"))
            return "Attention: there may be a pole, sign, barrier, or fixed obstacle nearby. Move carefully and avoid walking straight ahead.";

        if (ContainsAny(combined, "chair", "table", "desk", "box", "bag", "suitcase", "furniture", "bench"))
            return "Attention: there may be an object or piece of furniture in front of you. Move slowly and avoid walking straight ahead.";

        if (ContainsAny(combined, "door", "cabinet", "drawer"))
            return "Attention: there may be a door, cabinet, or open object nearby. Move carefully and protect your head and shoulders.";

        if (ContainsAny(combined, "person", "people", "man", "woman", "child"))
            return "There seems to be a person nearby. Move slowly and leave space in front of you.";

        if (ContainsAny(combined, "wet", "water", "floor", "snow", "ice"))
            return "Attention: the floor area may be slippery or unsafe. Step carefully.";

        if (ContainsAny(combined, "cable", "wire", "cord"))
            return "Attention: there may be a cable or wire nearby. Lift your feet carefully to avoid tripping.";

        return "";
    }

    private bool ContainsAny(string text, params string[] words)
    {
        if (string.IsNullOrWhiteSpace(text) || words == null)
            return false;

        for (int i = 0; i < words.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(words[i]) && text.Contains(words[i]))
                return true;
        }

        return false;
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
    private string MakeAssistantSpeechFeminine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        string s = text;

        // Corectăm formulările generate de traducător pentru voce feminină.
        s = s.Replace("Nu sunt complet sigur", "Nu sunt complet sigură");
        s = s.Replace("nu sunt complet sigur", "nu sunt complet sigură");

        s = s.Replace("Nu sunt sigur", "Nu sunt sigură");
        s = s.Replace("nu sunt sigur", "nu sunt sigură");

        s = s.Replace("Nu sunt foarte sigur", "Nu sunt foarte sigură");
        s = s.Replace("nu sunt foarte sigur", "nu sunt foarte sigură");

        s = s.Replace("Nu sunt pe deplin sigur", "Nu sunt pe deplin sigură");
        s = s.Replace("nu sunt pe deplin sigur", "nu sunt pe deplin sigură");

        return s;
    }
}