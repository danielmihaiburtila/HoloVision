using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Windows.WebCam;

public class PhotoCaptureManager : MonoBehaviour
{
    [Header("Capture Settings (recommended for OCR)")]
    public int maxWidth = 1920;
    public int maxHeight = 1080;

    [Range(30, 95)]
    public int jpgQuality = 90;

    [Range(0f, 1f)]
    public float hologramOpacity = 0.0f;

    [Header("Multi-shot (recommended for OCR on HoloLens)")]
    [Range(1, 5)]
    public int burstCount = 3;

    [Range(0.05f, 0.5f)]
    public float burstInterval = 0.18f;

    [Header("Debug")]
    public bool log = false;

    private bool busy;

    // ✅ last capture resolution (useful for bbox left/right in ObjectFinder)
    public int LastWidth { get; private set; }
    public int LastHeight { get; private set; }

    // ==========================
    // Public API
    // ==========================
    public void TakePhoto(Action<byte[]> onPhotoTaken)
    {
#if UNITY_EDITOR
        string path = System.IO.Path.Combine(Application.streamingAssetsPath, "test.jpg");
        if (System.IO.File.Exists(path))
        {
            var bytes = System.IO.File.ReadAllBytes(path);

            try
            {
                var tmp = new Texture2D(2, 2);
                if (tmp.LoadImage(bytes))
                {
                    LastWidth = tmp.width;
                    LastHeight = tmp.height;
                }
                Destroy(tmp);
            }
            catch { }

            onPhotoTaken?.Invoke(bytes);
        }
        else
        {
            Debug.LogError("[PhotoCaptureManager] Lipseste Assets/StreamingAssets/test.jpg");
            onPhotoTaken?.Invoke(null);
        }
#else
    // IMPORTANT: dacă e busy, NU ieși fără callback
    if (busy)
    {
        if (log) Debug.LogWarning("[PhotoCaptureManager] Busy - ignor cererea (return null).");
        onPhotoTaken?.Invoke(null);
        return;
    }

    busy = true;
    StartCoroutine(CaptureSingleJpgCoroutine(onPhotoTaken));
#endif
    }

    public void TakeBestPhotoForOCR(Action<byte[]> onPhotoTaken)
    {
#if UNITY_EDITOR
        TakePhoto(onPhotoTaken);
#else
    if (busy)
    {
        if (log) Debug.LogWarning("[PhotoCaptureManager] Busy - ignor cererea (return null).");
        onPhotoTaken?.Invoke(null);
        return;
    }

    busy = true;
    StartCoroutine(CaptureBurstPickBestCoroutine(onPhotoTaken));
#endif
    }

    // ==========================
    // Coroutines
    // ==========================
    private IEnumerator CaptureSingleJpgCoroutine(Action<byte[]> onDone)
    {
        byte[] result = null;
        bool finished = false;

        CaptureOnceTexture((tex, res) =>
        {
            try
            {
                result = (tex == null) ? null : tex.EncodeToJPG(jpgQuality);
            }
            catch (Exception e)
            {
                Debug.LogError("[PhotoCaptureManager] EncodeToJPG exception: " + e.Message);
                result = null;
            }
            finally
            {
                if (tex != null) Destroy(tex);
                finished = true;
            }
        });

        while (!finished) yield return null;

        busy = false;
        onDone?.Invoke(result);
    }

    private struct FrameScore
    {
        public Texture2D tex;
        public float score;
        public FrameScore(Texture2D t, float s) { tex = t; score = s; }
    }

    private IEnumerator CaptureBurstPickBestCoroutine(Action<byte[]> onDone)
    {
        int frames = Mathf.Max(1, burstCount);
        List<FrameScore> captured = new List<FrameScore>(frames);

        for (int i = 0; i < frames; i++)
        {
            Texture2D t = null;
            bool finished = false;

            CaptureOnceTexture((tex, res) =>
            {
                t = tex;
                finished = true;
            });

            while (!finished) yield return null;

            if (t != null)
            {
                float score = SharpnessScore(t);
                captured.Add(new FrameScore(t, score));

                if (log) Debug.Log($"[PhotoCaptureManager] Burst frame {i + 1}/{frames} sharpness={score:0.00}");
            }

            if (i < frames - 1)
                yield return new WaitForSeconds(burstInterval);
        }

        if (captured.Count == 0)
        {
            busy = false;
            onDone?.Invoke(null);
            yield break;
        }

        FrameScore best = captured.OrderByDescending(x => x.score).First();
        byte[] bestJpg = null;

        try
        {
            bestJpg = best.tex.EncodeToJPG(jpgQuality);
        }
        catch (Exception e)
        {
            Debug.LogError("[PhotoCaptureManager] Best EncodeToJPG exception: " + e.Message);
            bestJpg = null;
        }

        foreach (var item in captured)
        {
            if (item.tex != null) Destroy(item.tex);
        }

        busy = false;
        onDone?.Invoke(bestJpg);
    }

    // ==========================
    // Core capture (one frame -> Texture2D)
    // ==========================
    private void CaptureOnceTexture(Action<Texture2D, Resolution> onCaptured)
    {
        PhotoCapture.CreateAsync(false, capture =>
        {
            if (capture == null)
            {
                Debug.LogError("[PhotoCaptureManager] PhotoCapture.CreateAsync a returnat null.");
                onCaptured?.Invoke(null, default);
                return;
            }

            Resolution res = PickSafeResolution(maxWidth, maxHeight);

            // ✅ store last resolution chosen
            LastWidth = res.width;
            LastHeight = res.height;

            if (log) Debug.Log($"[PhotoCaptureManager] Using {res.width}x{res.height}, jpgQ={jpgQuality}");

            CameraParameters cp = new CameraParameters
            {
                hologramOpacity = hologramOpacity,
                pixelFormat = CapturePixelFormat.BGRA32,
                cameraResolutionWidth = res.width,
                cameraResolutionHeight = res.height
            };

            capture.StartPhotoModeAsync(cp, startResult =>
            {
                if (!startResult.success)
                {
                    Debug.LogError("[PhotoCaptureManager] StartPhotoModeAsync FAILED.");
                    SafeDispose(capture);
                    onCaptured?.Invoke(null, res);
                    return;
                }

                capture.TakePhotoAsync((photoResult, frame) =>
                {
                    if (!photoResult.success || frame == null)
                    {
                        Debug.LogError("[PhotoCaptureManager] TakePhotoAsync FAILED.");
                        StopAndDispose(capture, () => onCaptured?.Invoke(null, res));
                        return;
                    }

                    Texture2D tex = null;

                    try
                    {
                        tex = new Texture2D(res.width, res.height, TextureFormat.BGRA32, false);
                        frame.UploadImageDataToTexture(tex);
                        tex.Apply(false);
                    }
                    catch (Exception e)
                    {
                        Debug.LogError("[PhotoCaptureManager] UploadImageDataToTexture exception: " + e.Message);
                        if (tex != null) Destroy(tex);
                        tex = null;
                    }

                    StopAndDispose(capture, () => onCaptured?.Invoke(tex, res));
                });
            });
        });
    }

    // ==========================
    // Helpers
    // ==========================
    private static Resolution PickSafeResolution(int maxW, int maxH)
    {
        var supported = PhotoCapture.SupportedResolutions.ToList();
        if (supported == null || supported.Count == 0)
            return new Resolution { width = 1280, height = 720};

        var best = supported
            .Where(r => r.width <= maxW && r.height <= maxH)
            .OrderByDescending(r => r.width * r.height)
            .FirstOrDefault();

        if (best.width != 0 && best.height != 0)
            return best;

        return supported.OrderBy(r => r.width * r.height).First();
    }

    private static void StopAndDispose(PhotoCapture capture, Action done)
    {
        try
        {
            capture.StopPhotoModeAsync(stopResult =>
            {
                SafeDispose(capture);
                done?.Invoke();
            });
        }
        catch
        {
            SafeDispose(capture);
            done?.Invoke();
        }
    }

    private static void SafeDispose(PhotoCapture capture)
    {
        try { capture.Dispose(); } catch { }
    }

    private static float SharpnessScore(Texture2D tex)
    {
        if (tex == null) return 0f;

        Color32[] pixels;
        try { pixels = tex.GetPixels32(); }
        catch { return 0f; }

        int w = tex.width;
        int h = tex.height;
        if (w < 16 || h < 16) return 0f;

        int step = 4;

        double sum = 0;
        double sumSq = 0;
        int count = 0;

        for (int y = step; y < h - step; y += step)
        {
            int yw = y * w;
            for (int x = step; x < w - step; x += step)
            {
                int i = yw + x;

                float c = Luma(pixels[i]);
                float l = Luma(pixels[i - step]);
                float r = Luma(pixels[i + step]);
                float u = Luma(pixels[i - step * w]);
                float d = Luma(pixels[i + step * w]);

                float lap = (4f * c) - l - r - u - d;

                sum += lap;
                sumSq += lap * lap;
                count++;
            }
        }

        if (count <= 1) return 0f;

        double mean = sum / count;
        double var = (sumSq / count) - (mean * mean);
        return (float)Math.Max(0.0, var);
    }

    private static float Luma(Color32 c)
    {
        return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
    }
}