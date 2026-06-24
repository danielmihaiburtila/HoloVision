using UnityEngine;

[CreateAssetMenu(fileName = "SecretsConfig", menuName = "Config/SecretsConfig")]
public class SecretsConfig : ScriptableObject
{
    [Header("Azure Vision")]
    public string visionKey;
    public string visionEndpoint;

    [Header("Azure Translator")]
    public string translatorKey;
    public string translatorRegion;
    public string translatorEndpoint = "https://api.cognitive.microsofttranslator.com";

    [Header("Azure TTS")]
    public string ttsKey;
    public string ttsRegion = "francecentral";
    public string ttsVoice = "ro-RO-AlinaNeural";

    [Header("Azure OpenAI")]
    public string openaiKey;
    public string openaiEndpoint;
    public string openaiDeployment = "gpt-4o-mini";
    public string openaiApiVersion = "2024-02-15-preview";

    [Header("Google Vision (optional)")]
    public string googleVisionApiKey;
}