using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

public class OnScreenDebugConsole : MonoBehaviour
{
    public TextMeshProUGUI output;
    public int maxLines = 14;

    public string[] includePrefixes = { "[Menu]", "[Router]", "[ObjectFinder]" };

    private readonly Queue<string> lines = new Queue<string>();

    private void Awake()
    {
        if (output == null) output = GetComponent<TextMeshProUGUI>();
        if (output != null) output.text = "DEBUG READY\n";
    }

    private void OnEnable() => Application.logMessageReceived += HandleLog;
    private void OnDisable() => Application.logMessageReceived -= HandleLog;

    private void HandleLog(string condition, string stackTrace, LogType type)
    {
        bool isError = (type == LogType.Error || type == LogType.Exception);
        bool allowed = isError;

        if (!allowed && includePrefixes != null)
        {
            foreach (var p in includePrefixes)
            {
                if (!string.IsNullOrEmpty(p) && condition.StartsWith(p))
                {
                    allowed = true;
                    break;
                }
            }
        }

        if (!allowed) return;

        string msg = isError ? $"[{type}] {condition}" : condition;

        lines.Enqueue(msg);
        while (lines.Count > maxLines) lines.Dequeue();

        if (output == null) return;
        var sb = new StringBuilder();
        foreach (var l in lines) sb.AppendLine(l);
        output.text = sb.ToString();
    }
}