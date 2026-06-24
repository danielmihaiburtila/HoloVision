using UnityEngine;

/// <summary>
/// Injectează cheile din Resources/SecretsConfig.asset în componentele din scenă.
/// IMPORTANT: rulează foarte devreme ca să nu prindă comenzi vocale înainte de injectare.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class SecretsBootstrapper : MonoBehaviour
{
    [Tooltip("Numele asset-ului din Resources (fără extensie). Implicit: SecretsConfig")]
    public string resourceName = "SecretsConfig";

    private void Awake()
    {
        var cfg = Resources.Load<SecretsConfig>(resourceName);
        if (cfg == null)
        {
            Debug.LogWarning($"[SecretsBootstrapper] Missing Resources/{resourceName}.asset");
            return;
        }

        // ==============
        // AzureReadOCR
        // ==============
        var ocr = FindObjectOfType<AzureReadOCR>(true);
        if (ocr != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.visionKey)) ocr.subscriptionKey = cfg.visionKey;
            if (!string.IsNullOrWhiteSpace(cfg.visionEndpoint)) ocr.endpoint = cfg.visionEndpoint;
        }

        // ==============
        // AzureSceneDescription
        // ==============
        var scene = FindObjectOfType<AzureSceneDescription>(true);
        if (scene != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.visionKey)) scene.subscriptionKey = cfg.visionKey;
            if (!string.IsNullOrWhiteSpace(cfg.visionEndpoint)) scene.endpoint = cfg.visionEndpoint;
        }

        // ==============
        // AzureTranslator
        // ==============
        var tr = FindObjectOfType<AzureTranslator>(true);
        if (tr != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.translatorKey)) tr.subscriptionKey = cfg.translatorKey;
            if (!string.IsNullOrWhiteSpace(cfg.translatorRegion)) tr.region = cfg.translatorRegion;
            if (!string.IsNullOrWhiteSpace(cfg.translatorEndpoint)) tr.endpoint = cfg.translatorEndpoint;
        }

        // ==============
        // AzureTTS
        // ==============
        var tts = FindObjectOfType<AzureTTS>(true);
        if (tts != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.ttsKey)) tts.subscriptionKey = cfg.ttsKey;
            if (!string.IsNullOrWhiteSpace(cfg.ttsRegion)) tts.region = cfg.ttsRegion;
            if (!string.IsNullOrWhiteSpace(cfg.ttsVoice)) tts.voiceName = cfg.ttsVoice;
        }

        // ==============
        // AzureOpenAISummarizer
        // ==============
        var sum = FindObjectOfType<AzureOpenAISummarizer>(true);
        if (sum != null)
        {
            if (!string.IsNullOrWhiteSpace(cfg.openaiKey)) sum.apiKey = cfg.openaiKey;
            if (!string.IsNullOrWhiteSpace(cfg.openaiEndpoint)) sum.endpoint = cfg.openaiEndpoint;
            if (!string.IsNullOrWhiteSpace(cfg.openaiDeployment)) sum.deploymentName = cfg.openaiDeployment;
            if (!string.IsNullOrWhiteSpace(cfg.openaiApiVersion)) sum.apiVersion = cfg.openaiApiVersion;
        }

        // ==============
        // ✅ Azure Vision -> ObjectFinderSystem
        // ==============
        var finder = FindObjectOfType<ObjectFinderSystem>(true);
        if (finder != null)
        {
            // Pentru stabilitate, folosim Azure Vision la Object Finder.
            finder.provider = ObjectFinderSystem.Provider.AzureVision;

            if (!string.IsNullOrWhiteSpace(cfg.visionKey))
                finder.subscriptionKey = cfg.visionKey;

            if (!string.IsNullOrWhiteSpace(cfg.visionEndpoint))
                finder.endpoint = cfg.visionEndpoint;

            // Păstrăm Google key injectată, dar NU o mai folosim când provider = AzureVision.
            if (!string.IsNullOrWhiteSpace(cfg.googleVisionApiKey))
                finder.googleApiKey = cfg.googleVisionApiKey;

            finder.language = "en";
            finder.RebuildUrlsAfterSecretsInjected();

            Debug.Log(
                "[SecretsBootstrapper] ObjectFinder injected. " +
                "Provider=" + finder.provider +
                ", AzureKeySet=" + !string.IsNullOrWhiteSpace(finder.subscriptionKey) +
                ", AzureEndpoint=" + finder.endpoint +
                ", GoogleKeySet=" + !string.IsNullOrWhiteSpace(finder.googleApiKey)
            );
        }
        else
        {
            Debug.LogWarning("[SecretsBootstrapper] ObjectFinderSystem not found in scene.");
        }

        Debug.Log("[SecretsBootstrapper] Secrets injected (Azure + Google).");
    }
}