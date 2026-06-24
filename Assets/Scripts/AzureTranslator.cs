using System;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class AzureTranslator : MonoBehaviour
{
    [Header("Azure Translator Settings")]
    public string subscriptionKey = "CHEIA_TA_TRANSLATOR";
    public string region = "francecentral";
    public string endpoint = "https://api.cognitive.microsofttranslator.com";

    [Header("Debug")]
    public bool logRequests = false;
    public int timeoutSeconds = 10;

    [Serializable] private class TranslationItem { public string text; public string to; }
    [Serializable] private class TranslatorResponseItem { public TranslationItem[] translations; }
    [Serializable] private class WrappedResponse { public TranslatorResponseItem[] items; }

    public IEnumerator TranslateToRomanian(string englishText, Action<string> onDone)
    {
        yield return StartCoroutine(Translate("en", "ro", englishText, onDone));
    }

    public IEnumerator TranslateRomanianToEnglish(string romanianText, Action<string> onDone)
    {
        yield return StartCoroutine(Translate("ro", "en", romanianText, onDone));
    }

    private IEnumerator Translate(string from, string to, string text, Action<string> onDone)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string url = $"{endpoint.TrimEnd('/')}/translate?api-version=3.0&from={from}&to={to}";
        string safe = EscapeJsonString(text);
        string bodyJson = $"[{{\"Text\":\"{safe}\"}}]";
        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);

        if (logRequests)
        {
            Debug.Log("[AzureTranslator] URL: " + url);
            Debug.Log("[AzureTranslator] Body: " + bodyJson);
            Debug.Log("[AzureTranslator] Region: " + region);
        }

        using (UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = timeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/json; charset=UTF-8");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", subscriptionKey);
            if (!string.IsNullOrWhiteSpace(region))
                req.SetRequestHeader("Ocp-Apim-Subscription-Region", region);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string bodyFail = req.downloadHandler != null ? req.downloadHandler.text : "";
                Debug.LogError($"[AzureTranslator] FAIL code={req.responseCode} err={req.error} body={bodyFail}");
                onDone?.Invoke(null);
                yield break;
            }

            string json = req.downloadHandler.text;
            if (logRequests) Debug.Log("[AzureTranslator] Response: " + json);

            string outText = ParseTranslatedTextSafe(json);
            onDone?.Invoke(outText);
        }
    }

    private string ParseTranslatedTextSafe(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            string wrapped = "{\"items\":" + json + "}";
            WrappedResponse wrapper = JsonUtility.FromJson<WrappedResponse>(wrapped);

            if (wrapper?.items == null || wrapper.items.Length == 0) return null;
            var first = wrapper.items[0];
            if (first.translations == null || first.translations.Length == 0) return null;

            return string.IsNullOrWhiteSpace(first.translations[0].text) ? null : first.translations[0].text.Trim();
        }
        catch (Exception e)
        {
            Debug.LogError("[AzureTranslator] Parse exception: " + e.Message);
            return null;
        }
    }

    private static string EscapeJsonString(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }
}