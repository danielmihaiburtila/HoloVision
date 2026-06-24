using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class AzureOpenAISummarizer : MonoBehaviour
{
    [Header("Azure OpenAI (Chat Completions)")]
    [Tooltip("Ex: https://<resource-name>.openai.azure.com/  (fără / la final e ok)")]
    public string endpoint = "https://YOUR_RESOURCE.openai.azure.com/";

    [Tooltip("Cheia de Azure OpenAI")]
    public string apiKey = "PUT_AZURE_OPENAI_KEY_HERE";

    [Tooltip("Numele deployment-ului (Model deployment name) din Azure OpenAI Studio")]
    public string deploymentName = "gpt-4o-mini";

    [Tooltip("Versiunea API (ex: 2024-02-15-preview sau ce ai setat pe proiect)")]
    public string apiVersion = "2024-02-15-preview";

    [Header("Behavior")]
    public int timeoutSeconds = 25;
    [Range(0f, 1f)] public float temperature = 0.2f;

    [Header("Debug")]
    public bool logRequests = false;
    public bool logResponses = false;

    // --- cancel token ---
    private int _cancelToken = 0;
    private Coroutine _active;

    /// <summary>
    /// Oprește sumarizarea în curs (STOP).
    /// </summary>
    public void CancelSummarization()
    {
        _cancelToken++;

        if (_active != null)
        {
            StopCoroutine(_active);
            _active = null;
        }

        Debug.Log("[AzureOpenAISummarizer] CancelSummarization()");
    }

    /// <summary>
    /// Metoda cerută de AzureReadOCR:
    /// StartCoroutine(summarizer.SummarizeForBlindRo(text, s => summary = s));
    /// </summary>
    public IEnumerator SummarizeForBlindRo(string inputText, Action<string> onDone)
    {
        int myToken = _cancelToken;

        if (string.IsNullOrWhiteSpace(inputText))
        {
            onDone?.Invoke(null);
            yield break;
        }

        // Dacă nu e configurat, facem fallback ca să NU se blocheze aplicația.
        if (!IsConfigured())
        {
            string fallback = FallbackSummaryRo(inputText);
            onDone?.Invoke(fallback);
            yield break;
        }

        string url =
            $"{endpoint.TrimEnd('/')}/openai/deployments/{deploymentName}/chat/completions" +
            $"?api-version={apiVersion}";

        // Prompt simplu și stabil pentru “blind-friendly”
        string system =
    "Ești asistentul HoloVision pentru o persoană nevăzătoare. " +
    "Primești text extras prin OCR, deci poate conține greșeli. " +
    "Trebuie să faci un rezumat clar, natural și util în limba română. " +
    "Nu răspunde în engleză. Nu inventa informații care nu apar în text. " +
    "Dacă textul este neclar, spune simplu că rezumatul poate fi aproximativ.";

        string user =
       "Rezumă textul următor pentru o persoană nevăzătoare. " +
       "Scrie strict în limba română. " +
       "Folosește 3 până la 5 propoziții scurte. " +
       "Spune ideea principală, informațiile importante, eventualele date, sume, nume sau avertizări. " +
       "Nu folosi listă lungă. Nu adăuga informații care nu apar în text.\n\nTEXT OCR:\n" + inputText;

        string payload = BuildChatCompletionsJson(system, user, temperature);

        if (logRequests) Debug.Log("[AzureOpenAISummarizer] POST " + url + "\n" + payload);

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("api-key", apiKey);

            _active = StartCoroutine(Send(req, myToken, inputText, onDone));
            yield return _active;
            _active = null;
        }
    }

    private IEnumerator Send(UnityWebRequest req, int myToken, string originalText, Action<string> onDone)
    {
        yield return req.SendWebRequest();

        if (myToken != _cancelToken)
            yield break;

        string body = req.downloadHandler != null ? req.downloadHandler.text : "";

        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[AzureOpenAISummarizer] FAIL code={req.responseCode} err={req.error} body={body}");

            // IMPORTANT:
            // Nu rezumăm eroarea Azure. Rezumăm textul citit de OCR.
            onDone?.Invoke(FallbackSummaryRo(originalText));
            yield break;
        }

        if (logResponses) Debug.Log("[AzureOpenAISummarizer] OK: " + body);

        // Parse minim (fără JSON libs externe): extragem "content":"..."
        // Azure OpenAI: choices[0].message.content
        string content = ExtractAssistantContent(body);
        if (string.IsNullOrWhiteSpace(content))
        {
            onDone?.Invoke(FallbackSummaryRo(body));
            yield break;
        }

        onDone?.Invoke(content.Trim());
    }

    private bool IsConfigured()
    {
        if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Contains("PUT_") || apiKey.Contains("YOUR_"))
            return false;

        if (string.IsNullOrWhiteSpace(deploymentName))
            return false;

        if (string.IsNullOrWhiteSpace(apiVersion))
            return false;

        return true;
    }

    // -------------------------
    // Helpers
    // -------------------------

    private static string BuildChatCompletionsJson(string system, string user, float temp)
    {
        // JSON manual (fără dependențe), cu escaping minim necesar
        system = JsonEscape(system);
        user = JsonEscape(user);

        // max_tokens modest; dacă vrei mai mult, mărești.
        return
            "{"
            + "\"messages\":["
                + "{\"role\":\"system\",\"content\":\"" + system + "\"},"
                + "{\"role\":\"user\",\"content\":\"" + user + "\"}"
            + "],"
            + "\"temperature\":" + temp.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ","
            + "\"max_tokens\":350"
            + "}";
    }

    private static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    /// <summary>
    /// Extrage contentul assistantului din răspuns (parse "content":"...").
    /// Nu e parser complet JSON, dar e suficient pentru formatul standard.
    /// </summary>
    private static string ExtractAssistantContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        // Căutăm prima apariție a: "content":
        int idx = json.IndexOf("\"content\"", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        // Căutăm ':' după "content"
        idx = json.IndexOf(':', idx);
        if (idx < 0) return null;

        // Căutăm primul ghilimele după ':'
        idx = json.IndexOf('"', idx);
        if (idx < 0) return null;

        int start = idx + 1;

        // Parcurgem până la ghilimele ne-escapat
        StringBuilder sb = new StringBuilder();
        bool escape = false;

        for (int i = start; i < json.Length; i++)
        {
            char c = json[i];

            if (escape)
            {
                // unescape minim
                if (c == 'n') sb.Append('\n');
                else if (c == 'r') sb.Append('\r');
                else if (c == 't') sb.Append('\t');
                else sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                return sb.ToString();
            }

            sb.Append(c);
        }

        return null;
    }

    /// <summary>
    /// Fallback: dacă Azure OpenAI nu e configurat / eșuează, returnăm primele propoziții.
    /// Important: să nu blocheze aplicația și să nu dea null.
    /// </summary>
    private static string FallbackSummaryRo(string bodyOrText)
    {
        if (string.IsNullOrWhiteSpace(bodyOrText))
            return "Nu am putut genera un rezumat clar, dar voi continua cu citirea textului complet.";

        string t = bodyOrText.Replace("\r", " ").Replace("\n", " ").Trim();

        while (t.Contains("  "))
            t = t.Replace("  ", " ");

        // Dacă accidental primește JSON/eroare, nu o citi utilizatorului.
        if (t.StartsWith("{") || t.Contains("\"error\"") || t.Contains("Invalid") || t.Contains("Unauthorized"))
            return "Nu am putut genera rezumatul automat, dar voi continua cu citirea textului complet.";

        string[] sentences = SplitFallbackSentences(t);

        StringBuilder sb = new StringBuilder();
        int count = 0;

        for (int i = 0; i < sentences.Length && count < 3; i++)
        {
            string s = sentences[i].Trim();
            if (s.Length < 20) continue;

            if (sb.Length + s.Length > 500)
                break;

            sb.Append(s);

            if (!s.EndsWith(".") && !s.EndsWith("!") && !s.EndsWith("?"))
                sb.Append(".");

            sb.Append(" ");
            count++;
        }

        if (sb.Length == 0)
        {
            int max = Mathf.Min(400, t.Length);
            string cut = t.Substring(0, max).Trim();
            return "Nu am putut genera un rezumat inteligent. Primele informații importante par să fie: " + cut;
        }

        return "Nu am putut genera rezumatul inteligent, dar am extras ideea de început: " + sb.ToString().Trim();
    }

    private static string[] SplitFallbackSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new string[0];

        return text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
    }
}