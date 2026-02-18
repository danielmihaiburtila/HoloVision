using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AzureTTS : MonoBehaviour
{
    [Header("Azure Configuration")]
    public string subscriptionKey = "CHEIA_TA_AICI";
    public string region = "francecentral";
    public string voiceName = "ro-RO-AlinaNeural";

    [Header("Audio Output")]
    public AudioSource audioSource;

    [Header("Behavior")]
    [Tooltip("Recomandat TRUE pentru OCR: Speak() pune text la coadă, nu întrerupe frazele.")]
    public bool queueMode = true;

    [Tooltip("Timeout request TTS (secunde).")]
    public int timeoutSeconds = 20;

    private string synthesisEndpoint;

    // Queue
    private readonly Queue<string> speakQueue = new Queue<string>();
    private Coroutine runner;

    // Request control
    private UnityWebRequest activeRequest;
    private bool requestInFlight;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        audioSource.spatialBlend = 0f;   // 2D
        audioSource.playOnAwake = false;
    }

    private void Start()
    {
        synthesisEndpoint = $"https://{region}.tts.speech.microsoft.com/cognitiveservices/v1";
    }

    /// <summary>
    /// True dacă redă audio SAU există request în zbor.
    /// </summary>
    public bool IsSpeaking()
    {
        bool playing = audioSource != null && audioSource.isPlaying;
        return playing || requestInFlight;
    }

    /// <summary>
    /// Vorbește text.
    /// - queueMode=true: pune la coadă (ideal pentru OCR chunks)
    /// - queueMode=false: întrerupe și vorbește imediat noul text
    /// </summary>
    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        if (!queueMode)
        {
            StopNow();
            Enqueue(text);
            return;
        }

        Enqueue(text);
    }

    private void Enqueue(string text)
    {
        speakQueue.Enqueue(text);

        if (runner == null)
            runner = StartCoroutine(QueueRunner());
    }

    /// <summary>
    /// Oprește imediat: coadă + audio + request în zbor.
    /// </summary>
    public void StopNow()
    {
        // Clear queue
        speakQueue.Clear();

        // Abort request în zbor
        try
        {
            if (activeRequest != null) activeRequest.Abort();
        }
        catch { /* ignore */ }

        activeRequest = null;
        requestInFlight = false;

        // Stop audio
        if (audioSource != null)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }

        // Stop runner
        if (runner != null)
        {
            StopCoroutine(runner);
            runner = null;
        }
    }

    private IEnumerator QueueRunner()
    {
        while (speakQueue.Count > 0)
        {
            string text = speakQueue.Dequeue();
            yield return StartCoroutine(SynthesizeAndPlay(text));

            // așteaptă finalul clipului
            while (audioSource != null && audioSource.isPlaying)
                yield return null;
        }

        runner = null;
    }

    private IEnumerator SynthesizeAndPlay(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        // SSML
        string ssml =
            $@"<speak version='1.0' xml:lang='ro-RO'>
                 <voice xml:lang='ro-RO' xml:gender='Female' name='{voiceName}'>
                   {System.Security.SecurityElement.Escape(text)}
                 </voice>
               </speak>";

        using (UnityWebRequest request = new UnityWebRequest(synthesisEndpoint, "POST"))
        {
            activeRequest = request;
            requestInFlight = true;

            byte[] body = Encoding.UTF8.GetBytes(ssml);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerAudioClip(synthesisEndpoint, AudioType.MPEG);
            request.timeout = timeoutSeconds;

            request.SetRequestHeader("Content-Type", "application/ssml+xml");
            request.SetRequestHeader("X-Microsoft-OutputFormat", "audio-16khz-128kbitrate-mono-mp3");
            request.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            yield return request.SendWebRequest();

            requestInFlight = false;
            activeRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AzureTTS] FAIL {request.responseCode} - {request.error}");
                yield break;
            }

            AudioClip clip = null;
            try { clip = DownloadHandlerAudioClip.GetContent(request); }
            catch (Exception e)
            {
                Debug.LogError("[AzureTTS] GetContent exception: " + e.Message);
                yield break;
            }

            if (clip == null || audioSource == null) yield break;

            audioSource.clip = clip;
            audioSource.Play();
        }
    }
}