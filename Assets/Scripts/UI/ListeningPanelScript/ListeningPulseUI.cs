using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ListeningPulseUI : MonoBehaviour
{
    [Header("UI")]
    public RectTransform[] rings;
    public Image centerDot;
    public TextMeshProUGUI listeningText;

    [Header("Pulse")]
    public float speed = 1.2f;
    public float scaleAmount = 0.12f;
    public float alphaMin = 0.25f;
    public float alphaMax = 0.9f;

    private Vector3[] baseScales;
    private Image[] ringImages;

    private void Awake()
    {
        baseScales = new Vector3[rings.Length];
        ringImages = new Image[rings.Length];

        for (int i = 0; i < rings.Length; i++)
        {
            if (rings[i] == null) continue;

            baseScales[i] = rings[i].localScale;
            ringImages[i] = rings[i].GetComponent<Image>();
        }
    }

    private void OnEnable()
    {
        for (int i = 0; i < rings.Length; i++)
        {
            if (rings[i] != null)
                rings[i].localScale = baseScales[i];
        }
    }

    private void Update()
    {
        float time = Time.time * speed;

        for (int i = 0; i < rings.Length; i++)
        {
            if (rings[i] == null) continue;

            float offset = i * 0.55f;
            float wave = (Mathf.Sin(time - offset) + 1f) * 0.5f;

            float scale = 1f + wave * scaleAmount;
            rings[i].localScale = baseScales[i] * scale;

            if (ringImages[i] != null)
            {
                Color c = ringImages[i].color;
                c.a = Mathf.Lerp(alphaMin, alphaMax, 1f - wave);
                ringImages[i].color = c;
            }
        }

        if (centerDot != null)
        {
            float dotWave = (Mathf.Sin(time * 1.6f) + 1f) * 0.5f;
            centerDot.transform.localScale = Vector3.one * Mathf.Lerp(0.9f, 1.12f, dotWave);
        }

        if (listeningText != null)
        {
            int dots = Mathf.FloorToInt(Time.time * 2f) % 4;
            listeningText.text = "Ascult" + new string('.', dots);
        }
    }
}