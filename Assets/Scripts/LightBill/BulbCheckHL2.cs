using System;
using UnityEngine;
using System.Collections;

public class BulbCheckHL2 : MonoBehaviour
{
    [Header("Dependencies")]
    public PhotoCaptureManager photo;
    public AzureTTS tts;
    public SpeechUIAnimator ui;

    [Header("ROI (sus-centru, pentru tavan/bec)")]
    [Range(0.05f, 0.8f)] public float roiWidth = 0.35f;
    [Range(0.05f, 0.8f)] public float roiHeight = 0.30f;
    [Range(0f, 0.6f)] public float roiTopOffset = 0.05f;

    [Header("Decision thresholds")]
    [Range(0.02f, 0.50f)] public float deltaOn = 0.07f;
    [Range(0.0001f, 0.20f)] public float hotRatioOn = 0.008f;
    [Range(0.70f, 0.98f)] public float hotLuma = 0.86f;

    [Header("User-friendly capture")]
    public float aimDelaySeconds = 1.4f;

    [Header("Robust bulb search")]
    public bool scanUpperImageForBulb = true;

    [Range(0.25f, 0.90f)]
    public float upperSearchHeight = 0.65f;

    [Tooltip("Cât de mult trebuie să iasă celula luminoasă peste media imaginii.")]
    [Range(0.02f, 0.40f)]
    public float candidateDeltaFromScene = 0.09f;

    [Tooltip("Prag pentru puncte extrem de luminoase, specifice unei surse de lumină.")]
    [Range(0.80f, 0.99f)]
    public float veryHotLuma = 0.93f;

    [Tooltip("Procent minim de pixeli foarte luminoși într-o celulă candidat.")]
    [Range(0.0001f, 0.10f)]
    public float veryHotRatioOn = 0.0025f;

    [Header("Dark decision")]
    [Range(0.02f, 0.40f)]
    public float veryDarkMean = 0.16f;

    [Range(0.02f, 0.50f)]
    public float darkMean = 0.23f;

    public bool IsRunning => _running;
    private bool _running;

    [Header("Interaction instructions")]
    public bool preferPhoneControlInstructions = false;

    private void Awake()
    {
        if (photo == null) photo = FindObjectOfType<PhotoCaptureManager>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTS>(true);
        if (ui == null) ui = FindObjectOfType<SpeechUIAnimator>(true);
    }

    public void CheckBulbOnce()
    {
        if (_running) return;

        if (photo == null)
        {
            ui?.ShowError("Eroare: PhotoCaptureManager lipsește.");
            tts?.Speak("Eroare. Modulul de captură lipsește.");
            return;
        }

        _running = true;

        ui?.ShowUI("În regulă. Verific becul. Uită-te spre tavan sau spre zona unde se află becul și ține capul cât mai fix.");
        tts?.Speak("În regulă. Verific becul. Uită-te spre tavan sau spre zona unde se află becul și ține privirea fixă o secundă.");

        StartCoroutine(CaptureAfterAimDelay());
    }

    public void Stop(bool speak = false)
    {
        _running = false;
        if (speak) tts?.Speak("Am oprit verificarea becului.");
    }

    private IEnumerator CaptureAfterAimDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.3f, aimDelaySeconds));

        if (!_running) yield break;

        photo.TakeBestPhotoForOCR(OnJpgReady);
    }

    private void OnJpgReady(byte[] jpg)
    {
        if (!_running) return;
        _running = false;

        if (jpg == null || jpg.Length < 16)
        {
            ui?.ShowError("Nu am putut capta imaginea.");
            tts?.Speak("Nu am reușit să captez imaginea. Reîncearcă.");
            return;
        }

        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(jpg))
        {
            Destroy(tex);
            ui?.ShowError("Imagine invalidă.");
            tts?.Speak("Imagine invalidă. Reîncearcă.");
            return;
        }

        AnalyzeAndSpeak(tex);
        Destroy(tex);
    }

    private void AnalyzeAndSpeak(Texture2D tex)
    {
        int w = tex.width;
        int h = tex.height;
        Color32[] pixels = tex.GetPixels32();

        int rw = Mathf.Clamp(Mathf.RoundToInt(w * roiWidth), 8, w);
        int rh = Mathf.Clamp(Mathf.RoundToInt(h * roiHeight), 8, h);

        int x0 = (w - rw) / 2;
        int y0 = Mathf.Clamp(h - rh - Mathf.RoundToInt(h * roiTopOffset), 0, h - rh);

        double sumAll = 0; long cntAll = 0;
        double sumRoi = 0; long cntRoi = 0;
        double sumRest = 0; long cntRest = 0;

        long hotRoi = 0;
        long veryHotRoi = 0;
        long hotRest = 0;

        int step = 3;

        for (int y = 0; y < h; y += step)
        {
            int row = y * w;

            for (int x = 0; x < w; x += step)
            {
                float l = Luma(pixels[row + x]);

                sumAll += l;
                cntAll++;

                bool inRoi = x >= x0 && x < x0 + rw && y >= y0 && y < y0 + rh;

                if (inRoi)
                {
                    sumRoi += l;
                    cntRoi++;

                    if (l >= hotLuma) hotRoi++;
                    if (l >= veryHotLuma) veryHotRoi++;
                }
                else
                {
                    sumRest += l;
                    cntRest++;

                    if (l >= hotLuma) hotRest++;
                }
            }
        }

        float meanAll = cntAll > 0 ? (float)(sumAll / cntAll) : 0f;
        float meanRoi = cntRoi > 0 ? (float)(sumRoi / cntRoi) : 0f;
        float meanRest = cntRest > 0 ? (float)(sumRest / cntRest) : meanAll;

        float delta = meanRoi - meanRest;

        float hotRatio = cntRoi > 0 ? (float)hotRoi / cntRoi : 0f;
        float veryHotRatio = cntRoi > 0 ? (float)veryHotRoi / cntRoi : 0f;
        float hotRestRatio = cntRest > 0 ? (float)hotRest / cntRest : 0f;

        bool centerLooksLikeBulb =
            delta >= deltaOn &&
            hotRatio >= hotRatioOn &&
            hotRatio > hotRestRatio + 0.004f;

        bool centerVeryHot =
            delta >= deltaOn * 0.7f &&
            veryHotRatio >= veryHotRatioOn;

        bool upperLooksLikeBulb = false;
        float bestUpperMean = 0f;
        float bestUpperHotRatio = 0f;
        float bestUpperVeryHotRatio = 0f;
        float bestUpperDelta = 0f;
        float bestUpperLocalContrast = 0f;

        if (scanUpperImageForBulb)
        {
            upperLooksLikeBulb = FindBulbLikeCandidateInUpperImage(
                pixels,
                w,
                h,
                meanAll,
                out bestUpperMean,
                out bestUpperHotRatio,
                out bestUpperVeryHotRatio,
                out bestUpperDelta,
                out bestUpperLocalContrast
            );
        }

        bool likelyOn = centerLooksLikeBulb || centerVeryHot || upperLooksLikeBulb;

        if (likelyOn)
        {
            ui?.ShowSuccess(
                $"Bec probabil aprins (lum={meanAll:0.00}, roi={meanRoi:0.00}, Δ={delta:0.00}, hot={hotRatio:0.000}, vhot={veryHotRatio:0.000}, upper={bestUpperMean:0.00})."
            );

            tts?.Speak("Becul pare aprins. Detectez o sursă puternică și localizată de lumină în zona spre care privești.");
            return;
        }

        bool noLocalizedBulbLight =
      hotRatio < hotRatioOn &&
      veryHotRatio < veryHotRatioOn &&
      bestUpperHotRatio < hotRatioOn &&
      bestUpperVeryHotRatio < veryHotRatioOn &&
      bestUpperLocalContrast < 0.06f &&
      delta < deltaOn;

        if (noLocalizedBulbLight)
        {
            ui?.ShowSuccess(
                $"Bec probabil stins (lum={meanAll:0.00}, roi={meanRoi:0.00}, Δ={delta:0.00}, upperHot={bestUpperHotRatio:0.000}, vhot={bestUpperVeryHotRatio:0.000})."
            );

            tts?.Speak("Nu detectez o sursă puternică și localizată de lumină în zona becului. Becul pare stins.");
        }
        else
        {
            ui?.ShowUI(
                $"Neclar (lum={meanAll:0.00}, roi={meanRoi:0.00}, upper={bestUpperMean:0.00}, hot={bestUpperHotRatio:0.000}, vhot={bestUpperVeryHotRatio:0.000}, contrast={bestUpperLocalContrast:0.00})."
            );

            tts?.Speak("Există lumină în imagine, dar nu pot confirma sigur dacă vine de la bec. Privește mai direct spre bec și repetă comanda.");
        }
    }

    private bool FindBulbLikeCandidateInUpperImage(
        Color32[] pixels,
        int w,
        int h,
        float meanAll,
        out float bestMean,
        out float bestHotRatio,
        out float bestVeryHotRatio,
        out float bestDelta,
        out float bestLocalContrast
    )
    {
        bestMean = 0f;
        bestHotRatio = 0f;
        bestVeryHotRatio = 0f;
        bestDelta = 0f;
        bestLocalContrast = 0f;

        int searchStartY = Mathf.Clamp(Mathf.RoundToInt(h * (1f - upperSearchHeight)), 0, h - 1);

        int cellsX = 7;
        int cellsY = 4;

        int cellW = Mathf.Max(12, w / cellsX);
        int searchH = h - searchStartY;
        int cellH = Mathf.Max(12, searchH / cellsY);

        for (int cy = 0; cy < cellsY; cy++)
        {
            for (int cx = 0; cx < cellsX; cx++)
            {
                int sx = cx * cellW;
                int sy = searchStartY + cy * cellH;

                int ex = Mathf.Min(w, sx + cellW);
                int ey = Mathf.Min(h, sy + cellH);

                AnalyzeCell(pixels, w, h, sx, sy, ex, ey,
                    out float cellMean,
                    out float cellHotRatio,
                    out float cellVeryHotRatio);

                float neighborMean = EstimateNeighborMean(pixels, w, h, sx, sy, ex, ey);
                float cellDelta = cellMean - meanAll;
                float localContrast = cellMean - neighborMean;

                if (cellMean > bestMean)
                {
                    bestMean = cellMean;
                    bestHotRatio = cellHotRatio;
                    bestVeryHotRatio = cellVeryHotRatio;
                    bestDelta = cellDelta;
                    bestLocalContrast = localContrast;
                }

                bool hasEnoughHotPixels =
                    cellHotRatio >= hotRatioOn ||
                    cellVeryHotRatio >= veryHotRatioOn;

                bool standsOutFromScene =
                    cellDelta >= candidateDeltaFromScene;

                bool standsOutLocally =
                    localContrast >= 0.06f;

                bool notJustBrightWall =
                    hasEnoughHotPixels && (standsOutFromScene || standsOutLocally);

                if (notJustBrightWall)
                    return true;
            }
        }

        return false;
    }

    private void AnalyzeCell(
        Color32[] pixels,
        int w,
        int h,
        int sx,
        int sy,
        int ex,
        int ey,
        out float mean,
        out float hotRatio,
        out float veryHotRatio
    )
    {
        double sum = 0;
        long count = 0;
        long hot = 0;
        long veryHot = 0;

        int step = 3;

        for (int y = sy; y < ey; y += step)
        {
            int row = y * w;

            for (int x = sx; x < ex; x += step)
            {
                float l = Luma(pixels[row + x]);

                sum += l;
                count++;

                if (l >= hotLuma) hot++;
                if (l >= veryHotLuma) veryHot++;
            }
        }

        if (count <= 0)
        {
            mean = 0f;
            hotRatio = 0f;
            veryHotRatio = 0f;
            return;
        }

        mean = (float)(sum / count);
        hotRatio = (float)hot / count;
        veryHotRatio = (float)veryHot / count;
    }

    private float EstimateNeighborMean(
        Color32[] pixels,
        int w,
        int h,
        int sx,
        int sy,
        int ex,
        int ey
    )
    {
        int padX = Mathf.Max(8, (ex - sx) / 2);
        int padY = Mathf.Max(8, (ey - sy) / 2);

        int nsx = Mathf.Clamp(sx - padX, 0, w - 1);
        int nsy = Mathf.Clamp(sy - padY, 0, h - 1);
        int nex = Mathf.Clamp(ex + padX, 0, w);
        int ney = Mathf.Clamp(ey + padY, 0, h);

        double sum = 0;
        long count = 0;
        int step = 4;

        for (int y = nsy; y < ney; y += step)
        {
            int row = y * w;

            for (int x = nsx; x < nex; x += step)
            {
                bool insideOriginal = x >= sx && x < ex && y >= sy && y < ey;
                if (insideOriginal) continue;

                float l = Luma(pixels[row + x]);
                sum += l;
                count++;
            }
        }

        if (count <= 0)
            return 0f;

        return (float)(sum / count);
    }

    private static float Luma(Color32 c)
    {
        return (0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b) / 255f;
    }
}