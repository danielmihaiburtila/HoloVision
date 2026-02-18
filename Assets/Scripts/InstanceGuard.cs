using UnityEngine;

public class InstanceGuard : MonoBehaviour
{
    [Header("Disable duplicates")]
    public bool disableDuplicateAzureTTS = true;
    public bool disableDuplicateAzureReadOCR = true;

    void Awake()
    {
        if (disableDuplicateAzureTTS)
            DisableDuplicates<AzureTTS>("[Guard] Duplicate AzureTTS found. Disabling extra instance: ");

        if (disableDuplicateAzureReadOCR)
            DisableDuplicates<AzureReadOCR>("[Guard] Duplicate AzureReadOCR found. Disabling extra instance: ");
    }

    private void DisableDuplicates<T>(string logPrefix) where T : Behaviour
    {
        var all = FindObjectsOfType<T>(true);
        if (all == null || all.Length <= 1) return;

        // păstrăm prima instanță activă, oprim restul
        for (int i = 1; i < all.Length; i++)
        {
            if (all[i] == null) continue;
            Debug.LogWarning(logPrefix + all[i].name);
            all[i].enabled = false;
        }
    }
}