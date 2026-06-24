using System;
using System.Collections;
using UnityEngine;

public class AzureReadProductOcrBridge : ProductOcrBridge
{
    public AzureReadOCR azureReadOcr;

    [Header("Safety")]
    public float perRotationTimeoutSeconds = 0.85f;
    public float totalTimeoutSeconds = 1.80f;
    public bool log = false;

    [Header("Robust OCR")]
    public bool tryRotations = true;
    public bool stopAtFirstGoodResult = true;
    [Range(10, 400)] public int minimumGoodTextScore = 22;

    [Header("Image optimization")]
    [Range(512, 2048)] public int maxImageDimension = 1280;
    [Range(50, 95)] public int jpgQuality = 72;

    private class OcrBestResult
    {
        public string text;
        public int score = -1;
    }

    private void Awake()
    {
        if (azureReadOcr == null)
            azureReadOcr = FindObjectOfType<AzureReadOCR>(true);
    }

    public override IEnumerator ReadTextFromJpg(byte[] jpgBytes, Action<string> onDone)
    {
        if (azureReadOcr == null || jpgBytes == null || jpgBytes.Length < 1500)
        {
            onDone?.Invoke(null);
            yield break;
        }

        Texture2D src = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        OcrBestResult best = new OcrBestResult();

        try
        {
            if (!src.LoadImage(jpgBytes))
            {
                UnityEngine.Object.Destroy(src);
                onDone?.Invoke(null);
                yield break;
            }
        }
        catch (Exception e)
        {
            if (log) Debug.LogWarning("[AzureReadProductOcrBridge] LoadImage exception: " + e.Message);
            UnityEngine.Object.Destroy(src);
            onDone?.Invoke(null);
            yield break;
        }

        Texture2D optimized = null;

        try
        {
            optimized = ResizeIfNeeded(src, maxImageDimension);
            byte[] optimizedJpg = optimized.EncodeToJPG(jpgQuality);

            float globalStart = Time.time;
            float totalTimeout = Mathf.Clamp(totalTimeoutSeconds, 0.8f, 4f);

            int[] angles = tryRotations ? new[] { 0, 180 } : new[] { 0 };

            yield return StartCoroutine(RunAngles(optimized, optimizedJpg, angles, best, globalStart, totalTimeout));
        }
        finally
        {
            if (optimized != null) UnityEngine.Object.Destroy(optimized);
            UnityEngine.Object.Destroy(src);
        }

        onDone?.Invoke(string.IsNullOrWhiteSpace(best.text) ? null : best.text);
    }

    private IEnumerator RunAngles(
        Texture2D src,
        byte[] originalJpgBytes,
        int[] angles,
        OcrBestResult best,
        float globalStart,
        float totalTimeout)
    {
        for (int i = 0; i < angles.Length; i++)
        {
            if (Time.time - globalStart >= totalTimeout)
            {
                if (log) Debug.LogWarning("[AzureReadProductOcrBridge] Total OCR timeout reached.");
                yield break;
            }

            int angle = angles[i];
            Texture2D working = null;
            byte[] candidateBytes = null;
            string candidateText = null;
            bool done = false;

            try
            {
                if (angle == 0)
                {
                    candidateBytes = originalJpgBytes;
                }
                else
                {
                    working = RotateTexture(src, angle);
                    candidateBytes = EncodeTexture(working);
                }

                azureReadOcr.AnalyzeBytesForProduct(candidateBytes, text =>
                {
                    candidateText = text;
                    done = true;
                });
            }
            catch (Exception e)
            {
                if (log) Debug.LogWarning("[AzureReadProductOcrBridge] OCR exception at " + angle + "°: " + e.Message);
                if (working != null) UnityEngine.Object.Destroy(working);
                continue;
            }

            float t = 0f;
            float perRotationTimeout = Mathf.Clamp(perRotationTimeoutSeconds, 0.35f, 2f);

            while (!done && t < perRotationTimeout && Time.time - globalStart < totalTimeout)
            {
                t += Time.deltaTime;
                yield return null;
            }

            if (!done)
            {
                if (log) Debug.LogWarning("[AzureReadProductOcrBridge] OCR timeout at " + angle + "°.");
                if (working != null) UnityEngine.Object.Destroy(working);
                continue;
            }

            int score = ScoreText(candidateText);

            if (log)
                Debug.Log("[AzureReadProductOcrBridge] angle=" + angle + " score=" + score);

            if (score > best.score)
            {
                best.score = score;
                best.text = candidateText;
            }

            if (stopAtFirstGoodResult && best.score >= minimumGoodTextScore)
            {
                if (working != null) UnityEngine.Object.Destroy(working);
                yield break;
            }

            if (working != null) UnityEngine.Object.Destroy(working);
        }
    }

    private int ScoreText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        text = text.Trim();

        int letters = 0;
        int digits = 0;
        int useful = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (char.IsLetter(c)) letters++;
            if (char.IsDigit(c)) digits++;

            if (char.IsLetterOrDigit(c) || c == ' ' || c == ',' || c == '.' || c == ':' || c == '-' || c == '/' || c == '%')
                useful++;
        }

        int lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

        return letters * 2 + digits + useful + lines * 3;
    }

    private Texture2D RotateTexture(Texture2D src, int angle)
    {
        angle = ((angle % 360) + 360) % 360;

        Color32[] srcPixels = src.GetPixels32();
        int sw = src.width;
        int sh = src.height;

        Texture2D dst;
        Color32[] dstPixels;

        if (angle == 180)
        {
            dst = new Texture2D(sw, sh, TextureFormat.RGBA32, false);
            dstPixels = new Color32[sw * sh];
        }
        else
        {
            dst = new Texture2D(sh, sw, TextureFormat.RGBA32, false);
            dstPixels = new Color32[sh * sw];
        }

        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                Color32 c = srcPixels[y * sw + x];

                int nx = x;
                int ny = y;

                switch (angle)
                {
                    case 0:
                        nx = x;
                        ny = y;
                        break;
                    case 90:
                        nx = sh - 1 - y;
                        ny = x;
                        break;
                    case 180:
                        nx = sw - 1 - x;
                        ny = sh - 1 - y;
                        break;
                    case 270:
                        nx = y;
                        ny = sw - 1 - x;
                        break;
                }

                dstPixels[ny * dst.width + nx] = c;
            }
        }

        dst.SetPixels32(dstPixels);
        dst.Apply(false);
        return dst;
    }

    private Texture2D ResizeIfNeeded(Texture2D src, int maxDim)
    {
        if (src == null) return null;

        int w = src.width;
        int h = src.height;

        if (Mathf.Max(w, h) <= maxDim)
        {
            Texture2D clone = new Texture2D(w, h, TextureFormat.RGBA32, false);
            clone.SetPixels32(src.GetPixels32());
            clone.Apply(false);
            return clone;
        }

        float scale = (float)maxDim / Mathf.Max(w, h);
        int nw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
        int nh = Mathf.Max(1, Mathf.RoundToInt(h * scale));

        RenderTexture rt = RenderTexture.GetTemporary(nw, nh, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(src, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D dst = new Texture2D(nw, nh, TextureFormat.RGBA32, false);
        dst.ReadPixels(new Rect(0, 0, nw, nh), 0, 0);
        dst.Apply(false);

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return dst;
    }

    private byte[] EncodeTexture(Texture2D tex)
    {
        if (tex == null) return null;
        return tex.EncodeToJPG(jpgQuality);
    }
}