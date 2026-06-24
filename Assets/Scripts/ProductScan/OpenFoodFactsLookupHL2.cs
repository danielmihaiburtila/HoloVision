using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class OpenFoodFactsService : MonoBehaviour
{
    [Header("Behavior")]
    public int timeoutSeconds = 15;
    public bool log = false;

    public IEnumerator LookupBarcode(string barcode, Action<OpenFoodFactsProductResponse> onDone)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            onDone?.Invoke(null);
            yield break;
        }

        string url = $"https://world.openfoodfacts.org/api/v0/product/{UnityWebRequest.EscapeURL(barcode)}.json";

        using (UnityWebRequest req = UnityWebRequest.Get(url))
        {
            req.timeout = timeoutSeconds;
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                if (log) Debug.LogWarning("[OpenFoodFactsService] GET failed: " + req.error);
                onDone?.Invoke(null);
                yield break;
            }

            try
            {
                var data = JsonUtility.FromJson<OpenFoodFactsProductResponse>(req.downloadHandler.text);
                onDone?.Invoke(data);
            }
            catch (Exception e)
            {
                if (log) Debug.LogWarning("[OpenFoodFactsService] JSON parse error: " + e.Message);
                onDone?.Invoke(null);
            }
        }
    }
}

[Serializable]
public class OpenFoodFactsProductResponse
{
    public int status;
    public string code;
    public OpenFoodFactsProduct product;
}

[Serializable]
public class OpenFoodFactsProduct
{
    public string product_name;
    public string brands;
    public string quantity;
    public string ingredients_text;
    public string allergens;
    public string allergens_from_ingredients;
    public string generic_name;
}