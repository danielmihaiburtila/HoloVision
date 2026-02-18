using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Azure OpenAI Summarizer (Chat Completions) cu MAP-REDUCE robust pentru texte lungi.
/// STRICT: nu inventează, nu presupune.
/// + CancelSummarization(): oprește imediat sumarizarea și dă Abort() la request-ul curent.
/// </summary>
public class AzureOpenAISummarizer : MonoBehaviour
{
    [Header("Azure OpenAI Settings")]
    [Tooltip("Ex: https://<resource-name>.openai.azure.com/")]
    public string azureOpenAIEndpoint = "https://YOUR_RESOURCE_NAME.openai.azure.com/";
    [Tooltip("Deployment name (ex: gpt-4o-mini)")]
    public string deploymentName = "gpt-4o-mini";
    [Tooltip("API version (ex: 2024-06-01)")]
    public string apiVersion = "2024-06-01";
    [Tooltip("Azure OpenAI API key")]
    public string apiKey = "PUT_AZURE_OPENAI_KEY_HERE";

    [Header("Behavior")]
    [Range(0f, 1f)] public float temperature = 0.2f;
    public int timeoutSeconds = 35;

    [Header("Long text handling")]
    public int maxCharsPerRequest = 6000;
    public bool useMapReduceForLongText = true;
    public int segmentChars = 3200;

    [Header("Tokens")]
    public int maxTokensFinal = 900;
    public int maxTokensMap = 450;

    [Header("Reliability")]
    public int maxRetries = 3;
    public float retryBackoffSeconds = 1.2f;
    public int maxCompressionPasses = 3;

    // ---- Cancel support ----
    private int cancelToken = 0;
    private UnityWebRequest activeReq;

    /// <summary>
    /// Oprește imediat sumarizarea: invalidează corutinele + Abort request curent.
    /// </summary>
    public void CancelSummarization()
    {
        cancelToken++;
        try
        {
            if (activeReq != null)
            {
                activeReq.Abort();
            }
        }
        catch { /* ignore */ }

        activeReq = null;
        Debug.Log("[AzureOpenAISummarizer] CancelSummarization() called.");
    }

    private bool IsCancelled(int myToken) => myToken != cancelToken;

    // ======= Public API =======

    public IEnumerator SummarizeForBlindRo(string inputText, Action<string> onDone)
    {
        int myToken = cancelToken;

        if (string.IsNullOrWhiteSpace(inputText) || IsCancelled(myToken))
        {
            onDone?.Invoke(null);
            yield break;
        }

        if (!IsConfigured())
        {
            onDone?.Invoke(null);
            yield break;
        }

        inputText = inputText.Trim();

        // short path
        if (!useMapReduceForLongText || inputText.Length <= maxCharsPerRequest)
        {
            string oneShot = null;
            yield return StartCoroutine(SummarizeOneShot(inputText, myToken, s => oneShot = s));
            onDone?.Invoke(IsCancelled(myToken) ? null : oneShot);
            yield break;
        }

        // MAP
        List<string> segments = SplitIntoSegmentsSafe(inputText, segmentChars);
        if (segments == null || segments.Count == 0 || IsCancelled(myToken))
        {
            onDone?.Invoke(null);
            yield break;
        }

        List<string> extracted = new List<string>(segments.Count);

        for (int i = 0; i < segments.Count; i++)
        {
            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            string points = null;
            yield return StartCoroutine(ExtractKeyPoints(segments[i], i + 1, segments.Count, myToken, s => points = s));

            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            if (!string.IsNullOrWhiteSpace(points))
                extracted.Add(points.Trim());
        }

        if (extracted.Count == 0 || IsCancelled(myToken))
        {
            onDone?.Invoke(null);
            yield break;
        }

        // REDUCE input
        string reduceInput = string.Join("\n\n", extracted);

        // Compresie iterativă (foarte util la contracte)
        int pass = 0;
        while (reduceInput.Length > maxCharsPerRequest && pass < maxCompressionPasses)
        {
            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            pass++;
            string compressed = null;
            yield return StartCoroutine(CompressExtractedPoints(reduceInput, pass, myToken, s => compressed = s));

            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            if (string.IsNullOrWhiteSpace(compressed))
                break;

            reduceInput = compressed.Trim();
        }

        if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

        // Safety trim (nu vrem să crape request-ul)
        if (reduceInput.Length > maxCharsPerRequest)
            reduceInput = reduceInput.Substring(0, maxCharsPerRequest);

        string finalSummary = null;
        yield return StartCoroutine(FinalBlindSummaryFromPoints(reduceInput, myToken, s => finalSummary = s));

        onDone?.Invoke(IsCancelled(myToken) ? null : finalSummary);
    }

    // ======= Internals =======

    private bool IsConfigured()
    {
        if (string.IsNullOrWhiteSpace(azureOpenAIEndpoint) || !azureOpenAIEndpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("[AzureOpenAISummarizer] Endpoint invalid.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PUT_"))
        {
            Debug.LogError("[AzureOpenAISummarizer] API key missing/placeholder.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(deploymentName))
        {
            Debug.LogError("[AzureOpenAISummarizer] Deployment name missing.");
            return false;
        }
        return true;
    }

    private IEnumerator SummarizeOneShot(string text, int myToken, Action<string> onDone)
    {
        if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

        string system =
            "Ești un asistent pentru persoane cu dizabilități vizuale. " +
            "Primești text OCR și trebuie să rezumi STRICT doar ce apare în text. " +
            "REGULI: nu inventa, nu presupune. Dacă lipsește o informație, scrie «nu este menționat». " +
            "Păstrează exact nume, date, ore, sume, termeni legali.";

        string user =
            "Fă un REZUMAT UTIL PENTRU UN NEVĂZĂTOR.\n" +
            "Format obligatoriu:\n" +
            "1) Despre ce este textul (1 propoziție)\n" +
            "2) Ce trebuie să facă utilizatorul (dacă există)\n" +
            "3) Unde / când (dacă există)\n" +
            "4) Detalii importante (max 8 puncte scurte)\n" +
            "5) Este necesar să fie citit integral? (da/nu și de ce)\n\n" +
            "TEXT:\n" + text;

        yield return StartCoroutine(CallChatWithRetry(system, user, maxTokensFinal, myToken, onDone));
    }

    private IEnumerator ExtractKeyPoints(string segment, int idx, int total, int myToken, Action<string> onDone)
    {
        if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

        string system =
            "Ești un asistent care extrage informații factuale din texte OCR. " +
            "STRICT: nu inventa, nu presupune. Extrage doar ce este explicit.";

        string user =
            $"Segment {idx}/{total}. Extrage DOAR puncte factuale esențiale.\n" +
            "Concentrează-te pe: părți implicate, obiect, obligații, termene/date, sume, penalități, condiții, contacte.\n" +
            "Dacă nu există ceva, nu inventa.\n\n" +
            "TEXT:\n" + segment;

        yield return StartCoroutine(CallChatWithRetry(system, user, maxTokensMap, myToken, onDone));
    }

    private IEnumerator CompressExtractedPoints(string extractedPoints, int passIndex, int myToken, Action<string> onDone)
    {
        if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

        string system =
            "Comprimă liste de puncte fără să pierzi informații critice. " +
            "STRICT: nu inventa. Nu elimina date/sume/termene.";

        string user =
            $"Trecerea {passIndex}. Comprimă lista de puncte (fără repetări), păstrând TOATE datele, sumele și termenele:\n\n" +
            extractedPoints;

        yield return StartCoroutine(CallChatWithRetry(system, user, maxTokensMap, myToken, onDone));
    }

    private IEnumerator FinalBlindSummaryFromPoints(string points, int myToken, Action<string> onDone)
    {
        if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

        string system =
            "Ești un asistent pentru persoane cu dizabilități vizuale. " +
            "Primești puncte extrase din segmente. Construiește un rezumat final STRICT pe baza punctelor.";

        string user =
            "Construiește REZUMATUL FINAL pentru nevăzător din punctele de mai jos.\n" +
            "Format obligatoriu:\n" +
            "1) Despre ce este textul (1 propoziție)\n" +
            "2) Ce trebuie să facă utilizatorul (dacă există)\n" +
            "3) Unde / când (dacă există)\n" +
            "4) Detalii importante (max 10 puncte scurte)\n" +
            "5) Este necesar să fie citit integral? (da/nu și de ce)\n\n" +
            "PUNCTE:\n" + points;

        yield return StartCoroutine(CallChatWithRetry(system, user, maxTokensFinal, myToken, onDone));
    }

    // ======= HTTP (cu retry + cancel) =======

    [Serializable] private class ChatResponse { public Choice[] choices; public ErrorObj error; }
    [Serializable] private class Choice { public Message message; }
    [Serializable] private class Message { public string role; public string content; }
    [Serializable] private class ErrorObj { public string message; }

    private IEnumerator CallChatWithRetry(string system, string user, int outTokens, int myToken, Action<string> onDone)
    {
        string url =
            $"{azureOpenAIEndpoint.TrimEnd('/')}/openai/deployments/{deploymentName}/chat/completions" +
            $"?api-version={apiVersion}";

        float backoff = Mathf.Max(0.2f, retryBackoffSeconds);

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            string result = null;
            long code = 0;
            string err = null;
            string body = null;

            yield return StartCoroutine(CallChatOnce(url, system, user, outTokens, myToken,
                (okContent, responseCode, error, responseBody) =>
                {
                    result = okContent;
                    code = responseCode;
                    err = error;
                    body = responseBody;
                }));

            if (IsCancelled(myToken)) { onDone?.Invoke(null); yield break; }

            if (!string.IsNullOrWhiteSpace(result))
            {
                onDone?.Invoke(result.Trim());
                yield break;
            }

            bool transient =
                code == 0 || code == 408 || code == 429 || (code >= 500 && code <= 599);

            if (!transient)
            {
                Debug.LogError($"[AzureOpenAISummarizer] Non-transient fail. code={code} err={err} body={body}");
                onDone?.Invoke(null);
                yield break;
            }

            if (attempt == maxRetries)
            {
                Debug.LogError($"[AzureOpenAISummarizer] Retries exhausted. code={code} err={err} body={body}");
                onDone?.Invoke(null);
                yield break;
            }

            Debug.LogWarning($"[AzureOpenAISummarizer] Transient fail (attempt {attempt + 1}/{maxRetries}). code={code} err={err}. Retrying in {backoff:0.0}s");
            yield return new WaitForSeconds(backoff);
            backoff *= 2f;
        }

        onDone?.Invoke(null);
    }

    private IEnumerator CallChatOnce(string url, string system, string user, int outTokens, int myToken,
        Action<string, long, string, string> onFinished)
    {
        if (IsCancelled(myToken)) { onFinished?.Invoke(null, 0, "cancelled", null); yield break; }

        string bodyJson = BuildChatJson(system, user, temperature, outTokens);

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            activeReq = req;

            byte[] body = Encoding.UTF8.GetBytes(bodyJson);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("api-key", apiKey);

            yield return req.SendWebRequest();

            // dacă s-a dat cancel în timpul requestului
            if (IsCancelled(myToken))
            {
                activeReq = null;
                onFinished?.Invoke(null, 0, "cancelled", null);
                yield break;
            }

            string responseBody = req.downloadHandler != null ? req.downloadHandler.text : "";

            if (req.result != UnityWebRequest.Result.Success)
            {
                // Dacă a fost abort -> considerăm cancelled/timeout
                activeReq = null;
                Debug.LogWarning($"[AzureOpenAISummarizer] FAIL code={req.responseCode} err={req.error} body={responseBody}");
                onFinished?.Invoke(null, req.responseCode, req.error, responseBody);
                yield break;
            }

            string content = ParseChatContent(responseBody);
            activeReq = null;

            if (string.IsNullOrWhiteSpace(content))
            {
                Debug.LogWarning($"[AzureOpenAISummarizer] Empty content. code={req.responseCode} body={responseBody}");
                onFinished?.Invoke(null, req.responseCode, "empty_content", responseBody);
                yield break;
            }

            onFinished?.Invoke(content, req.responseCode, null, responseBody);
        }
    }

    private static string ParseChatContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var resp = JsonUtility.FromJson<ChatResponse>(json);
            if (resp == null) return null;

            if (resp.error != null && !string.IsNullOrWhiteSpace(resp.error.message))
            {
                Debug.LogError("[AzureOpenAISummarizer] API error: " + resp.error.message);
                return null;
            }

            if (resp.choices == null || resp.choices.Length == 0) return null;
            if (resp.choices[0].message == null) return null;

            return resp.choices[0].message.content;
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureOpenAISummarizer] Parse exception: " + e.Message);
            return null;
        }
    }

    private static string BuildChatJson(string system, string user, float temp, int maxTokens)
    {
        system = EscapeJson(system);
        user = EscapeJson(user);

        return "{"
               + "\"messages\":["
               + "{\"role\":\"system\",\"content\":\"" + system + "\"},"
               + "{\"role\":\"user\",\"content\":\"" + user + "\"}"
               + "],"
               + "\"temperature\":" + temp.ToString("0.0", CultureInfo.InvariantCulture) + ","
               + "\"max_tokens\":" + maxTokens.ToString(CultureInfo.InvariantCulture)
               + "}";
    }

    private static string EscapeJson(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    private static List<string> SplitIntoSegmentsSafe(string text, int segChars)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        segChars = Mathf.Clamp(segChars, 1200, 6000);

        List<string> segments = new List<string>();
        int i = 0;

        while (i < text.Length)
        {
            int len = Mathf.Min(segChars, text.Length - i);
            int end = i + len;

            int cut = text.LastIndexOfAny(new[] { '\n', '.', ';', ':', '?', '!' }, end - 1, len);
            if (cut <= i + (int)(segChars * 0.55f))
                cut = end;

            string part = text.Substring(i, cut - i).Trim();
            if (!string.IsNullOrWhiteSpace(part))
                segments.Add(part);

            i = cut;
        }

        return segments;
    }
}