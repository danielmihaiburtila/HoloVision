using System;
using System.Collections.Generic;
using UnityEngine;
using MixedReality.Toolkit;
using MixedReality.Toolkit.Subsystems;
using UnityEngine.Events;

public class VirtualAssistant : MonoBehaviour
{
    [Header("Routing")]
    public VoiceCommandRouter router;

    [Header("Keywords (MRTK3 KeywordRecognitionSubsystem)")]
    [Tooltip("Cuvintele pe care vrei să le recunoască MRTK keyword subsystem.")]
    [SerializeField]
    private string[] keywords = new[]
    {
        "start", "exit", "parasire", "iesire",
        "descriere", "descrie",
        "citire", "citeste",
        "inapoi", "meniu",
        "stop", "opreste", "gata"
    };

    private KeywordRecognitionSubsystem keywordSubsystem;

    // Păstrăm handler-ele ca să le putem scoate exact pe ale noastre (fără RemoveAllListeners)
    private readonly Dictionary<string, UnityAction> handlers = new Dictionary<string, UnityAction>(StringComparer.OrdinalIgnoreCase);

    private bool initialized;

    private void Start()
    {
        if (router == null)
            router = FindObjectOfType<VoiceCommandRouter>();

        keywordSubsystem = XRSubsystemHelpers.GetFirstRunningSubsystem<KeywordRecognitionSubsystem>();

        if (keywordSubsystem == null)
        {
            Debug.LogWarning("[VirtualAssistant] KeywordRecognitionSubsystem is NOT running! Check MRTK3 Subsystems settings.");
            return;
        }

        if (router == null)
        {
            Debug.LogError("[VirtualAssistant] VoiceCommandRouter not found! Assign it in Inspector.");
            return;
        }

        RegisterKeywords();
        initialized = true;

        Debug.Log("[VirtualAssistant] Keywords registered: " + string.Join(", ", keywords));
    }

    private void RegisterKeywords()
    {
        // Curățăm dacă există resturi (siguranță)
        handlers.Clear();

        foreach (var k in keywords)
        {
            if (string.IsNullOrWhiteSpace(k)) continue;

            string keyword = k.Trim().ToLowerInvariant();

            // evităm dubluri
            if (handlers.ContainsKey(keyword)) continue;

            UnityAction action = () =>
            {
                // trimitem keyword-ul direct (router-ul tău deja normalizează și decide)
                router?.AcceptCommand(keyword);
            };

            handlers[keyword] = action;

            var evt = keywordSubsystem.CreateOrGetEventForKeyword(keyword);
            evt.AddListener(action);
        }
    }

    private void OnDisable()
    {
        // dacă obiectul se dezactivează, scoatem listeners ca să nu rămână agățați
        UnregisterKeywords();
    }

    private void OnDestroy()
    {
        UnregisterKeywords();
    }

    private void UnregisterKeywords()
    {
        if (!initialized) return;
        if (keywordSubsystem == null) return;
        if (handlers.Count == 0) return;

        foreach (var kv in handlers)
        {
            string keyword = kv.Key;
            UnityAction action = kv.Value;

            // IMPORTANT: scoatem doar listenerul nostru
            var evt = keywordSubsystem.CreateOrGetEventForKeyword(keyword);
            evt.RemoveListener(action);
        }

        handlers.Clear();
        initialized = false;

        Debug.Log("[VirtualAssistant] Keywords unregistered.");
    }
}