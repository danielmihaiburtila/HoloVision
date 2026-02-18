using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AzureReadOCR : MonoBehaviour
{
    [Header("Azure Vision (Image Analysis v4.0)")]
    public string subscriptionKey = "PUT_VISION_KEY_HERE";
    public string endpoint = "https://nume.cognitiveservices.azure.com/";
    public string apiVersion = "2024-02-01";
    public string language = "en";

    [Header("Dependencies")]
    public PhotoCaptureManager captureManager;
    public AzureTTSInterruptible tts;
    public SpeechUIAnimator uiAnimator;
    public AzureOpenAISummarizer summarizer;

    [Header("Behavior")]
    public int timeoutSeconds = 25;
    public bool logResponses = false;

    [Header("Quality")]
    [Range(0f, 1f)] public float minWordConfidence = 0.60f;
    [Range(0f, 1f)] public float maxRejectedRatio = 0.35f;

    [Header("TTS")]
    [Tooltip("600-900 ok. Mai mic = mai des, mai natural pentru stop.")]
    public int maxCharsPerChunk = 650;

    [Header("Summarization")]
    public bool enableSummarization = true;
    public int summarizeIfCharsOver = 900;
    public bool askToReadFullAfterSummary = true;

    private bool busy;
    private string analyzeUrl;

    // token de anulare (STOP)
    private int speakToken = 0;

    // ca să oprim corutinele în curs (STOP total)
    private Coroutine activeAnalyze;
    private Coroutine activeSpeak;

    [Serializable] private class Root { public ReadResult readResult; }
    [Serializable] private class ReadResult { public Block[] blocks; }
    [Serializable] private class Block { public Line[] lines; }
    [Serializable] private class Line { public string text; public Word[] words; }
    [Serializable] private class Word { public string text; public float confidence; }

    private void Awake()
    {
        if (captureManager == null) captureManager = FindObjectOfType<PhotoCaptureManager>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTSInterruptible>(true);
        if (uiAnimator == null) uiAnimator = FindObjectOfType<SpeechUIAnimator>(true);
        if (summarizer == null) summarizer = FindObjectOfType<AzureOpenAISummarizer>(true);
    }

    private void Start()
    {
        BuildAnalyzeUrl();
        Debug.Log("[AzureReadOCR] Analyze URL: " + analyzeUrl);
    }

    private void BuildAnalyzeUrl()
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        analyzeUrl =
            $"{endpoint.TrimEnd('/')}/computervision/imageanalysis:analyze" +
            $"?api-version={apiVersion}" +
            $"&features=read" +
            $"&overload=stream" +
            $"&language={language}";
    }

    /// <summary>
    /// STOP/GATA: oprește TOT (citire + sumarizare + audio + corutine).
    /// </summary>
    public void StopReading()
    {
        // oprește orice corutină care rulează pe acest AzureReadOCR (inclusiv SpeakSmart)
        StopAllCoroutines();

        // invalidează orice sesiune curentă
        speakToken++;
        busy = false;

        // oprește sumarizarea (abort request)
        summarizer?.CancelSummarization();

        // oprește TTS imediat (request + audio)
        if (tts != null) tts.StopNow();

        if (uiAnimator != null) uiAnimator.ShowUI("Am oprit citirea.");
        Debug.Log("[AzureReadOCR] StopReading() hard stop.");
    }

    public void ReadText()
    {
        if (busy)
        {
            Feedback("Citesc deja. Spune GATA ca să opresc.", processing: true);
            return;
        }

        if (captureManager == null)
        {
            Feedback("Eroare: CameraManager nu este legat.", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(subscriptionKey) || subscriptionKey.Contains("PUT_"))
        {
            Feedback("Eroare: cheia de Azure Vision nu este setată.", error: true);
            return;
        }

        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Feedback("Eroare: endpoint-ul Azure Vision nu este setat corect.", error: true);
            return;
        }

        BuildAnalyzeUrl();

        // IMPORTANT: înainte să pornim o citire nouă, oprim orice “resturi”
        StopReading();

        // acum pornim sesiunea nouă
        busy = true;
        int myToken = speakToken;

        Feedback("Citesc textul. Umple cadrul cu text și ține stabil o secundă.", processing: true);

        captureManager.TakeBestPhotoForOCR(bytes =>
        {
            if (myToken != speakToken) return;

            if (bytes == null || bytes.Length < 2000)
            {
                busy = false;
                Feedback("Eroare: nu am primit o imagine validă.", error: true);
                return;
            }

            activeAnalyze = StartCoroutine(AnalyzeRead(bytes, myToken));
        });
    }

    private IEnumerator AnalyzeRead(byte[] imageBytes, int myToken)
    {
        using (UnityWebRequest req = new UnityWebRequest(analyzeUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(imageBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);

            yield return req.SendWebRequest();

            if (myToken != speakToken) yield break;

            busy = false;
            activeAnalyze = null;

            string body = req.downloadHandler != null ? req.downloadHandler.text : "";
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError($"[AzureReadOCR] FAIL code={req.responseCode} err={req.error} body={body}");
                Feedback($"Eroare OCR. Cod: {req.responseCode}", error: true);
                yield break;
            }

            if (logResponses) Debug.Log("[AzureReadOCR] JSON: " + body);

            if (!TryParseRead(body, out string rawText, out float rejectedRatio))
            {
                Feedback("Nu am găsit text clar. Apropie foaia și ține-o nemișcată.", error: true);
                yield break;
            }

            if (rejectedRatio > maxRejectedRatio)
            {
                Feedback("Text neclar. Ține foaia mai stabilă și umple cadrul cu text.", error: true);
                yield break;
            }

            string cleanText = CleanupText(rawText);
            string speechText = ReflowForSpeech(cleanText);

            if (myToken != speakToken) yield break;

            // 1) Rezumat pentru texte lungi
            if (enableSummarization && summarizer != null && speechText.Length >= summarizeIfCharsOver)
            {
                Feedback("Text lung detectat. Încerc un rezumat. Spune GATA ca să opresc.", processing: true);

                string summary = null;
                yield return StartCoroutine(summarizer.SummarizeForBlindRo(speechText, s => summary = s));

                if (myToken != speakToken) yield break;

                if (!string.IsNullOrWhiteSpace(summary))
                {
                    activeSpeak = StartCoroutine(SpeakSmart(summary, myToken));
                    yield return activeSpeak;
                    activeSpeak = null;

                    if (myToken != speakToken) yield break;

                    if (askToReadFullAfterSummary)
                        Feedback("Dacă vrei să îți citesc tot textul, spune: CITEȘTE TOT.", success: true);

                    yield break;
                }

                // fallback: citire normală
                Feedback("Nu am reușit rezumatul. Îl citesc pe bucăți. Spune GATA ca să opresc.", error: true);
            }

            // 2) Citire completă
            activeSpeak = StartCoroutine(SpeakSmart(speechText, myToken));
            yield return activeSpeak;
            activeSpeak = null;
        }
    }

    // =========================
    // Speaking (natural + stop)
    // =========================
    private IEnumerator SpeakSmart(string text, int myToken)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (var chunk in SplitForSpeechNatural(text, maxCharsPerChunk))
        {
            if (myToken != speakToken) yield break;
            if (string.IsNullOrWhiteSpace(chunk)) continue;

            string speak = chunk.Trim();

            // Intonație: dacă nu se termină cu . ! ? -> adaugă virgulă (nu “final de propoziție”)
            if (!EndsWithTerminalPunctuation(speak))
                speak += ",";

            tts?.Speak(speak);

            // Așteaptă până termină (dar iese imediat la STOP)
            float t = 0f;
            float hardMax = Mathf.Clamp(speak.Length / 10f, 2f, 25f); // suficient pt request+play
            while (t < hardMax)
            {
                if (myToken != speakToken) yield break;
                if (tts != null && !tts.IsSpeaking()) break;
                t += Time.deltaTime;
                yield return null;
            }
        }
    }

    private static bool EndsWithTerminalPunctuation(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;
        char c = s[s.Length - 1];
        return c == '.' || c == '!' || c == '?';
    }

    // =========================
    // OCR Parse
    // =========================
    private bool TryParseRead(string json, out string text, out float rejectedRatio)
    {
        text = null;
        rejectedRatio = 0f;

        try
        {
            var root = JsonUtility.FromJson<Root>(json);
            if (root?.readResult?.blocks == null || root.readResult.blocks.Length == 0)
                return false;

            int kept = 0;
            int rejected = 0;
            StringBuilder sb = new StringBuilder();

            foreach (var b in root.readResult.blocks)
            {
                if (b?.lines == null) continue;

                foreach (var line in b.lines)
                {
                    if (line?.words != null && line.words.Length > 0)
                    {
                        StringBuilder lineSb = new StringBuilder();
                        bool any = false;

                        foreach (var w in line.words)
                        {
                            if (w == null || string.IsNullOrWhiteSpace(w.text)) continue;

                            if (w.confidence >= minWordConfidence)
                            {
                                lineSb.Append(w.text).Append(" ");
                                kept++;
                                any = true;
                            }
                            else rejected++;
                        }

                        if (any) sb.AppendLine(lineSb.ToString().Trim());
                    }
                    else if (!string.IsNullOrWhiteSpace(line?.text))
                    {
                        sb.AppendLine(line.text.Trim());
                        kept += Mathf.Max(1, line.text.Length / 4);
                    }
                }
            }

            int total = kept + rejected;
            rejectedRatio = total > 0 ? (float)rejected / total : 0f;

            string result = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(result)) return false;

            text = result;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureReadOCR] Parse exception: " + e.Message);
            return false;
        }
    }

    // =========================
    // Text formatting
    // =========================
    private static string CleanupText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        input = input.Replace("\r", "\n").Replace("\t", " ");
        while (input.Contains("  ")) input = input.Replace("  ", " ");
        while (input.Contains("\n\n\n")) input = input.Replace("\n\n\n", "\n\n");
        return input.Trim();
    }

    /// <summary>
    /// Unește liniile OCR în propoziții mai naturale.
    /// Păstrează paragrafele (linie goală => paragraf nou).
    /// </summary>
    private static string ReflowForSpeech(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;

        input = input.Replace("\r", "\n");
        while (input.Contains("\n\n\n")) input = input.Replace("\n\n\n", "\n\n");

        var paragraphs = input.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        for (int p = 0; p < paragraphs.Length; p++)
        {
            var lines = paragraphs[p].Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;

                // unește despărțiri cu '-' la capăt de rând
                if (line.EndsWith("-") && i < lines.Length - 1)
                {
                    sb.Append(line.Substring(0, line.Length - 1));
                    continue;
                }

                sb.Append(line);

                // dacă linia se termină cu punctuație terminală, păstrează pauza
                // altfel pune spațiu (nu “final de propoziție”)
                sb.Append(" ");
            }

            paragraphs[p] = sb.ToString().Trim();
        }

        return string.Join("\n\n", paragraphs).Trim();
    }

    /// <summary>
    /// Split natural:
    /// 1) pe propoziții reale: . ! ?
    /// 2) dacă e prea lung, split soft pe virgulă
    /// 3) dacă e încă prea lung, split pe spații (hard)
    /// </summary>
    private static IEnumerable<string> SplitForSpeechNatural(string text, int maxChars)
    {
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var paraRaw in paragraphs)
        {
            string para = paraRaw.Trim();
            if (para.Length == 0) continue;

            var sentences = SplitBySentenceEnd(para); // doar .!? sunt “sentence end”

            foreach (var s in sentences)
            {
                string sent = s.Trim();
                if (sent.Length == 0) continue;

                if (sent.Length <= maxChars)
                {
                    yield return sent;
                    continue;
                }

                // split soft pe virgulă
                var parts = SplitByDelimiter(sent, ',');
                foreach (var partRaw in parts)
                {
                    string part = partRaw.Trim();
                    if (part.Length == 0) continue;

                    if (part.Length <= maxChars)
                    {
                        yield return part;
                    }
                    else
                    {
                        foreach (var hard in HardSplitBySpaces(part, maxChars))
                            yield return hard;
                    }
                }
            }
        }
    }

    private static List<string> SplitBySentenceEnd(string input)
    {
        List<string> parts = new List<string>();
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            sb.Append(c);

            if (c == '.' || c == '!' || c == '?')
            {
                string part = sb.ToString().Trim();
                if (part.Length > 0) parts.Add(part);
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            string last = sb.ToString().Trim();
            if (last.Length > 0) parts.Add(last);
        }

        return parts;
    }

    private static List<string> SplitByDelimiter(string input, char delim)
    {
        List<string> parts = new List<string>();
        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            sb.Append(c);

            if (c == delim)
            {
                string part = sb.ToString().Trim();
                if (part.Length > 0) parts.Add(part);
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            string last = sb.ToString().Trim();
            if (last.Length > 0) parts.Add(last);
        }

        return parts;
    }

    private static IEnumerable<string> HardSplitBySpaces(string input, int maxChars)
    {
        int i = 0;
        while (i < input.Length)
        {
            int len = Mathf.Min(maxChars, input.Length - i);
            int end = i + len;

            int cut = input.LastIndexOf(' ', end - 1, len);
            if (cut <= i) cut = end;

            string part = input.Substring(i, cut - i).Trim();
            if (part.Length > 0) yield return part;

            i = cut;
        }
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

        // IMPORTANT: feedback-ul trebuie să fie interruptible
        tts?.Speak(message);
    }
}