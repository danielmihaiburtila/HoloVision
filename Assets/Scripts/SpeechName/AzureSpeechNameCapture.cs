using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public class AzureSpeechNameCapture : MonoBehaviour
{
    [Header("Azure Speech Settings")]
    public string subscriptionKey = "CHEIA_TA_AICI";
    public string region = "francecentral";
    public string language = "ro-RO";

    [Header("Recording")]
    [Range(2f, 10f)] public float recordingSeconds = 7f;
    [Range(0f, 2f)] public float startCaptureDelay = 0.15f;
    public int sampleRate = 16000;
    public string microphoneDevice = null;

    [Header("References")]
    public VoiceCommandRouter router;
    public AzureTTS azureTTS;
    public SpeechUIAnimator speechUI;
    public SecretsConfig secretsConfig;

    [Header("Debug")]
    public bool logRequests = true;
    public int timeoutSeconds = 20;

    private Coroutine captureRoutine;
    private bool isCapturing = false;

    [Serializable]
    private class SpeechDetailedResponse
    {
        public RecognitionCandidate[] NBest;
        public string RecognitionStatus;
        public string DisplayText;
        public int Offset;
        public int Duration;
    }

    [Serializable]
    private class RecognitionCandidate
    {
        public string Lexical;
        public string ITN;
        public string MaskedITN;
        public string Display;
        public float Confidence;
    }

    private void Awake()
    {
        if (secretsConfig == null)
            secretsConfig = Resources.Load<SecretsConfig>("SecretsConfig");

        if (secretsConfig != null)
        {
            // Dacă TTS și STT folosesc aceeași resursă Speech din Azure, cheia asta e ok.
            if (!string.IsNullOrWhiteSpace(secretsConfig.ttsKey))
                subscriptionKey = secretsConfig.ttsKey;

            if (!string.IsNullOrWhiteSpace(secretsConfig.ttsRegion))
                region = secretsConfig.ttsRegion;
        }

        if (router == null) router = FindObjectOfType<VoiceCommandRouter>(true);
        if (azureTTS == null) azureTTS = FindObjectOfType<AzureTTS>(true);
        if (speechUI == null) speechUI = FindObjectOfType<SpeechUIAnimator>(true);
    }

    public bool IsCapturing()
    {
        return isCapturing;
    }



    public void CancelCapture()
    {
        if (captureRoutine != null)
        {
            StopCoroutine(captureRoutine);
            captureRoutine = null;
        }

        try
        {
            if (Microphone.IsRecording(microphoneDevice))
                Microphone.End(microphoneDevice);
        }
        catch { }

        isCapturing = false;
    }
    public void BeginUserNameCapture()
    {
        CancelCapture();
        captureRoutine = StartCoroutine(CaptureAndRecognizeRoutine());
    }

    private IEnumerator CaptureAndRecognizeRoutine()
    {
        isCapturing = true;

        if (logRequests)
            Debug.Log("[AzureSpeechNameCapture] BeginUserNameCapture");

        
        float safetyWait = 12f;
        float t = 0f;
        while (azureTTS != null && azureTTS.IsSpeaking() && t < safetyWait)
        {
            t += Time.deltaTime;
            yield return null;
        }

        yield return new WaitForSeconds(startCaptureDelay);

        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            Debug.LogError("[AzureSpeechNameCapture] Nu există microfon disponibil.");
            speechUI?.ShowError("Nu am găsit microfonul.");
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        speechUI?.ShowUI("Ascult numele...");

        AudioClip clip = null;

        try
        {
            clip = Microphone.Start(microphoneDevice, false, Mathf.CeilToInt(recordingSeconds), sampleRate);
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureSpeechNameCapture] Microphone.Start exception: " + e.Message);
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        float startWait = 0f;
        while (Microphone.GetPosition(microphoneDevice) <= 0 && startWait < 2f)
        {
            startWait += Time.deltaTime;
            yield return null;
        }

        if (Microphone.GetPosition(microphoneDevice) <= 0)
        {
            Debug.LogError("[AzureSpeechNameCapture] Microfonul nu a pornit.");
            try { Microphone.End(microphoneDevice); } catch { }
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        yield return new WaitForSeconds(recordingSeconds);

        int samplesRecorded = 0;
        try
        {
            samplesRecorded = Microphone.GetPosition(microphoneDevice);
            Microphone.End(microphoneDevice);
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureSpeechNameCapture] Microphone stop exception: " + e.Message);
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        if (samplesRecorded <= 0 || clip == null)
        {
            Debug.LogWarning("[AzureSpeechNameCapture] Nu s-a înregistrat audio util.");
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        AudioClip trimmed = TrimClip(clip, samplesRecorded);
        byte[] wavData = WavUtility.FromAudioClip(trimmed, sampleRate, 1);

        if (wavData == null || wavData.Length == 0)
        {
            Debug.LogError("[AzureSpeechNameCapture] WAV gol sau invalid.");
            isCapturing = false;
            captureRoutine = null;
            router?.OnAzureUserNameCaptureFailed();
            yield break;
        }

        string url =
            $"https://{region}.stt.speech.microsoft.com/speech/recognition/conversation/cognitiveservices/v1" +
            $"?language={UnityWebRequest.EscapeURL(language)}&format=detailed";

        if (logRequests)
            Debug.Log("[AzureSpeechNameCapture] POST " + url);

        using (UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(wavData);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "audio/wav; codecs=audio/pcm; samplerate=16000");
            req.SetRequestHeader("Accept", "application/json");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string body = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.LogError($"[AzureSpeechNameCapture] FAIL code={req.responseCode} err={req.error} body={body}");
                isCapturing = false;
                captureRoutine = null;
                router?.OnAzureUserNameCaptureFailed();
                yield break;
            }

            string json = req.downloadHandler.text;

            if (logRequests)
                Debug.Log("[AzureSpeechNameCapture] Response: " + json);

            string recognized = ParseBestTextFromDetailed(json);

            Debug.Log("[AzureSpeechNameCapture] Recognized text: " + (recognized ?? "<null>"));

            isCapturing = false;
            captureRoutine = null;

            if (string.IsNullOrWhiteSpace(recognized))
            {
                router?.OnAzureUserNameCaptureFailed();
            }
            else
            {
                router?.OnAzureUserNameRecognized(recognized);
            }
        }
    }

    private static string ParseBestTextFromDetailed(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            var obj = JsonUtility.FromJson<SpeechDetailedResponse>(json);
            if (obj == null) return null;

            if (!string.Equals(obj.RecognitionStatus, "Success", StringComparison.OrdinalIgnoreCase))
                return null;

            // 1. Încearcă întâi NBest[0].Display / Lexical
            if (obj.NBest != null && obj.NBest.Length > 0)
            {
                for (int i = 0; i < obj.NBest.Length; i++)
                {
                    string candidate =
                        FirstNonEmpty(
                            obj.NBest[i].Display,
                            obj.NBest[i].Lexical,
                            obj.NBest[i].ITN,
                            obj.NBest[i].MaskedITN
                        );

                    candidate = CleanRecognizedText(candidate);
                    if (!string.IsNullOrWhiteSpace(candidate))
                        return candidate;
                }
            }

            // 2. Fallback la DisplayText
            string display = CleanRecognizedText(obj.DisplayText);
            if (!string.IsNullOrWhiteSpace(display))
                return display;

            return null;
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureSpeechNameCapture] Parse exception: " + e.Message);
            return null;
        }
    }

    private static string FirstNonEmpty(params string[] values)
    {
        if (values == null) return null;

        for (int i = 0; i < values.Length; i++)
        {
            if (!string.IsNullOrWhiteSpace(values[i]))
                return values[i];
        }

        return null;
    }

    private static string CleanRecognizedText(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;

        s = s.Trim();

        while (s.EndsWith(".") || s.EndsWith("!") || s.EndsWith("?") || s.EndsWith(","))
            s = s.Substring(0, s.Length - 1).Trim();

        return s;
    }

    private static AudioClip TrimClip(AudioClip original, int samplesRecorded)
    {
        int channels = original.channels;
        float[] data = new float[samplesRecorded * channels];
        original.GetData(data, 0);

        AudioClip trimmed = AudioClip.Create(
            "trimmed_name_capture",
            samplesRecorded,
            channels,
            original.frequency,
            false
        );

        trimmed.SetData(data, 0);
        return trimmed;
    }
}