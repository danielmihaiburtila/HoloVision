using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class OpenFoodFactsLookupHL2 : MonoBehaviour
{
    [Header("API")]
    public string baseUrl = "https://world.openfoodfacts.org/api/v0/product/";
    public int timeoutSeconds = 10;
    public bool log = false;

    [Serializable]
    public class OffProductResponse
    {
        public int status;                 // 1 = found, 0 = not found
        public string status_verbose;
        public OffProduct product;
    }

    [Serializable]
    public class OffProduct
    {
        public string product_name;
        public string product_name_ro;
        public string brands;
        public string quantity;
        public string ingredients_text;
        public string ingredients_text_ro;
        public string allergens;
    }

    public IEnumerator GetProductByBarcode(string barcode, Action<OffProductResponse> onDone)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string digits = CleanToDigits(barcode);
        if (digits.Length < 8)
        {
            onDone?.Invoke(null);
            yield break;
        }

        string url = baseUrl.TrimEnd('/') + "/" + digits + ".json";
        if (log) Debug.Log("[OFF] GET " + url);

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = timeoutSeconds;
            req.SetRequestHeader("Accept", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (log) Debug.LogWarning("[OFF] Request failed: " + req.error);
                onDone?.Invoke(null);
                yield break;
            }

            string json = req.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(json))
            {
                onDone?.Invoke(null);
                yield break;
            }

            OffProductResponse resp = null;
            try
            {
                resp = JsonUtility.FromJson<OffProductResponse>(json);
            }
            catch (Exception e)
            {
                if (log) Debug.LogWarning("[OFF] JSON parse failed: " + e.Message);
                resp = null;
            }

            // Prefer românã dacã existã
            if (resp != null && resp.product != null)
            {
                if (!string.IsNullOrWhiteSpace(resp.product.product_name_ro))
                    resp.product.product_name = resp.product.product_name_ro;

                if (!string.IsNullOrWhiteSpace(resp.product.ingredients_text_ro))
                    resp.product.ingredients_text = resp.product.ingredients_text_ro;
            }

            onDone?.Invoke(resp);
        }
    }

    private static string CleanToDigits(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        char[] buf = new char[s.Length];
        int j = 0;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c >= '0' && c <= '9') buf[j++] = c;
        }

        return new string(buf, 0, j);
    }
}