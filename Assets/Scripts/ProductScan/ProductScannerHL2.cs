using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;

public class ProductScannerHL2 : MonoBehaviour
{
    [Header("Deps (din proiectul tău)")]
    public PhotoCaptureManager captureManager;
    public AzureTTSInterruptible tts;
    public SpeechUIAnimator ui;
    public OpenFoodFactsLookupHL2 offLookup;
    public BeepGuide beep;

    [Header("Flow")]
    public bool useOpenFoodFacts = true;
    public bool useOcrFallbackIfNoBarcode = true;

    [Header("Azure OCR (reuse)")]
    public string azureVisionKey = "PUT_VISION_KEY_HERE";
    public string azureVisionEndpoint = "https://nume.cognitiveservices.azure.com/";
    public string apiVersion = "2024-02-01";
    public string language = "ro";
    public int ocrTimeoutSeconds = 18;

    [Header("Scanning")]
    public float maxScanSeconds = 12f;
    public float scanInterval = 0.55f;

    [Header("Capture override while scanning")]
    public bool overrideCaptureResolution = true;
    public int scanMaxWidth = 1280;
    public int scanMaxHeight = 720;

    public bool overrideJpgQuality = true;
    [Range(30, 95)] public int scanJpgQuality = 80;

    // internal
    private Coroutine runner;
    private bool running;
    public bool IsRunning => running;

    private int prevW, prevH, prevQ;
    private string analyzeUrl;
    private Texture2D cachedTex;

    private float lastGuidanceSpeak;
    private string lastGuidance;

#if USE_ZXING
    private ZxingDecoder zxing;
    [Header("ZXing formats")]
    public bool enableQr = true;
    public bool enableCode128 = true;
#endif

    [Serializable] private class OcrRoot { public ReadResult readResult; }
    [Serializable] private class ReadResult { public Block[] blocks; }
    [Serializable] private class Block { public Line[] lines; }
    [Serializable] private class Line { public string text; }

    private void Awake()
    {
        if (captureManager == null) captureManager = FindObjectOfType<PhotoCaptureManager>(true);
        if (tts == null) tts = FindObjectOfType<AzureTTSInterruptible>(true);
        if (ui == null) ui = FindObjectOfType<SpeechUIAnimator>(true);
        if (beep == null) beep = FindObjectOfType<BeepGuide>(true);
        if (offLookup == null) offLookup = FindObjectOfType<OpenFoodFactsLookupHL2>(true);

        BuildOcrUrl();

#if USE_ZXING
        zxing = new ZxingDecoder(enableQr, enableCode128);
#endif
    }

    private void BuildOcrUrl()
    {
        if (string.IsNullOrWhiteSpace(azureVisionEndpoint))
        {
            analyzeUrl = null;
            return;
        }

        analyzeUrl =
            $"{azureVisionEndpoint.TrimEnd('/')}/computervision/imageanalysis:analyze" +
            $"?api-version={apiVersion}" +
            $"&features=read" +
            $"&overload=stream" +
            $"&language={language}";
    }

    public void StartScan()
    {
        if (running) return;
        if (captureManager == null || tts == null) return;

        BuildOcrUrl();

        // override capture (temporar)
        if (overrideCaptureResolution)
        {
            prevW = captureManager.maxWidth;
            prevH = captureManager.maxHeight;
            captureManager.maxWidth = scanMaxWidth;
            captureManager.maxHeight = scanMaxHeight;
        }

        if (overrideJpgQuality)
        {
            prevQ = captureManager.jpgQuality;
            captureManager.jpgQuality = scanJpgQuality;
        }

        running = true;
        runner = StartCoroutine(ScanRoutine());
    }

    public void StopScan(bool silent = false)
    {
        if (!running) return;

        running = false;
        if (runner != null) { StopCoroutine(runner); runner = null; }

        beep?.StopBeeping();

        // restore
        if (overrideCaptureResolution)
        {
            if (prevW > 0) captureManager.maxWidth = prevW;
            if (prevH > 0) captureManager.maxHeight = prevH;
        }
        if (overrideJpgQuality)
        {
            if (prevQ > 0) captureManager.jpgQuality = prevQ;
        }

        if (!silent)
        {
            ui?.ShowSuccess("Am oprit scanarea produsului.");
            tts?.Speak("Am oprit scanarea produsului.");
        }
    }

    public void StopScanCelebrate()
    {
        StopScan(silent: true);
        ui?.ShowSuccess("Mă bucur că ai găsit ce căutai. Dacă te pot ajuta cu ceva, te rog.");
        tts?.Speak("Mă bucur că ai găsit ce căutai. Dacă te pot ajuta cu ceva, te rog.");
    }

    private IEnumerator ScanRoutine()
    {
        ui?.ShowProcessing("Ține produsul în față. Caut codul de bare...");
        tts?.Speak("Ține produsul în față. Caut codul de bare.");
        beep?.StartBeeping(0.9f);

        float start = Time.time;
        string barcode = null;
        byte[] lastFrame = null;

        int centeredHits = 0;

        // Faza 1: încercăm ZXing local cât timp avem timp
        while (running && (Time.time - start) < maxScanSeconds)
        {
            bool got = false;
            lastFrame = null;

            captureManager.TakePhoto(bytes => { lastFrame = bytes; got = true; });

            while (running && !got) yield return null;
            if (!running) yield break;

            if (lastFrame != null && lastFrame.Length > 2000)
            {
#if USE_ZXING
                if (TryDecodeWithZxing(lastFrame, out var decoded, out var pts, out int w, out int h))
                {
                    barcode = decoded;

                    // dacă e QR cu URL (sau text), încercăm să extragem un cod valid (EAN/UPC) din el
                    if (!string.IsNullOrWhiteSpace(barcode) && TryExtractBarcodeFromText(barcode, out var normalized))
                        barcode = normalized;

                    // ghidaj audio
                    var g = ComputeGuidance(pts, w, h);
                    ApplyGuidance(g);

                    if (g.isCentered) centeredHits++;
                    else centeredHits = 0;

                    // cerem 2 “hit-uri” centrate ca să fie stabil
                    if (centeredHits >= 2)
                        break;
                }
                else
                {
                    centeredHits = 0;
                    ApplyGuidance(Guidance.Searching());
                }
#else
                // dacă nu ai ZXing, doar stăm în “search” și trecem la OCR fallback mai jos
                ApplyGuidance(Guidance.Searching());
#endif
            }

            yield return new WaitForSeconds(scanInterval);
        }

        if (!running) yield break;

        // Faza 2: dacă NU avem barcode, încercăm fallback prin OCR (online)
        if (string.IsNullOrWhiteSpace(barcode) && useOcrFallbackIfNoBarcode)
        {
            ui?.ShowProcessing("Nu am citit codul local. Încerc citirea prin OCR...");
            tts?.Speak("Nu am citit codul local. Încerc citirea prin OCR.");
            beep?.StartBeeping(0.7f);

            string ocrText = null;
            if (lastFrame != null && lastFrame.Length > 2000)
                yield return StartCoroutine(OcrReadText(lastFrame, t => ocrText = t));

            if (!string.IsNullOrWhiteSpace(ocrText) && TryExtractBarcodeFromText(ocrText, out var fromOcr))
                barcode = fromOcr;
        }

        // Dacă tot nu avem barcode, măcar citim eticheta (OCR) și spunem ce vedem
        string labelText = null;
        if (lastFrame != null && lastFrame.Length > 2000)
            yield return StartCoroutine(OcrReadText(lastFrame, t => labelText = t));

        // Lookup produs (dacă avem cod)
        string offName = null, offBrand = null, offQty = null, offIng = null, offAll = null;

        if (!string.IsNullOrWhiteSpace(barcode) && useOpenFoodFacts && offLookup != null)
        {
            ui?.ShowProcessing("Am cod. Caut produsul...");
            tts?.Speak("Am cod. Caut produsul.");

            OpenFoodFactsLookupHL2.OffProductResponse resp = null;
            yield return StartCoroutine(offLookup.GetProductByBarcode(barcode, r => resp = r));

            if (resp != null && resp.status == 1 && resp.product != null)
            {
                offName = Safe(resp.product.product_name);
                offBrand = Safe(resp.product.brands);
                offQty = Safe(resp.product.quantity);
                offIng = Safe(resp.product.ingredients_text);
                offAll = Safe(resp.product.allergens);
            }
        }

        beep?.StopBeeping();

        string final = BuildSpokenResult(barcode, offName, offBrand, offQty, offIng, offAll, labelText);
        ui?.ShowSuccess(final);
        tts?.Speak(final);

        StopScan(silent: true);
    }

#if USE_ZXING
    private bool TryDecodeWithZxing(byte[] jpg, out string text, out Vector2[] points, out int w, out int h)
    {
        text = null; points = null; w = 0; h = 0;

        if (cachedTex == null) cachedTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (!cachedTex.LoadImage(jpg, false))
            return false;

        w = cachedTex.width;
        h = cachedTex.height;

        return zxing.TryDecode(cachedTex, out text, out points);
    }
#endif

    // ===== GUIDANCE =====
    private struct Guidance
    {
        public bool hasPoints;
        public bool isCentered;
        public float beepInterval;
        public string speak;

        public static Guidance Searching()
        {
            return new Guidance
            {
                hasPoints = false,
                isCentered = false,
                beepInterval = 0.9f,
                speak = null
            };
        }
    }

    private Guidance ComputeGuidance(Vector2[] pts, int w, int h)
    {
        if (pts == null || pts.Length == 0 || w <= 0 || h <= 0)
            return Guidance.Searching();

        // centru barcode în imagine
        Vector2 c = Vector2.zero;
        for (int i = 0; i < pts.Length; i++) c += pts[i];
        c /= pts.Length;

        float nx = (c.x - (w * 0.5f)) / (w * 0.5f); // -1..1
        float ny = (c.y - (h * 0.5f)) / (h * 0.5f);

        float dist = Mathf.Clamp01(new Vector2(nx, ny).magnitude); // 0 = centru

        bool centered = dist < 0.18f;

        string dir = null;
        if (!centered)
        {
            if (Mathf.Abs(nx) > Mathf.Abs(ny))
                dir = nx < 0 ? "Mută puțin spre stânga." : "Mută puțin spre dreapta.";
            else
                dir = ny < 0 ? "Mută puțin în jos." : "Mută puțin în sus.";
        }
        else
        {
            dir = "Ține fix.";
        }

        // beep mai rapid când e mai centrat
        float interval = Mathf.Lerp(0.85f, 0.12f, 1f - dist);

        return new Guidance
        {
            hasPoints = true,
            isCentered = centered,
            beepInterval = interval,
            speak = dir
        };
    }

    private void ApplyGuidance(Guidance g)
    {
        // beep
        beep?.StartBeeping(g.beepInterval);

        // nu vorbim continuu, doar la schimbare + cooldown
        if (string.IsNullOrWhiteSpace(g.speak)) return;

        float now = Time.time;
        if (now - lastGuidanceSpeak < 1.1f) return; // cooldown

        if (g.speak == lastGuidance) return;

        lastGuidance = g.speak;
        lastGuidanceSpeak = now;

        tts?.Speak(g.speak);
    }

    // ===== OCR =====
    private IEnumerator OcrReadText(byte[] img, Action<string> onDone)
    {
        onDone?.Invoke(null);

        if (string.IsNullOrWhiteSpace(azureVisionKey) || azureVisionKey.Contains("PUT_")) yield break;
        if (string.IsNullOrWhiteSpace(analyzeUrl)) yield break;

        using (var req = new UnityWebRequest(analyzeUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(img);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.timeout = ocrTimeoutSeconds;

            req.SetRequestHeader("Content-Type", "application/octet-stream");
            req.SetRequestHeader("Ocp-Apim-Subscription-Key", azureVisionKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success) yield break;

            string json = req.downloadHandler.text;
            string text = ExtractPlainTextFromOcrJson(json);
            onDone?.Invoke(text);
        }
    }

    private string ExtractPlainTextFromOcrJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var root = JsonUtility.FromJson<OcrRoot>(json);
            if (root?.readResult?.blocks == null) return null;

            var sb = new StringBuilder();
            foreach (var b in root.readResult.blocks)
            {
                if (b?.lines == null) continue;
                foreach (var l in b.lines)
                {
                    if (!string.IsNullOrWhiteSpace(l?.text))
                        sb.AppendLine(l.text.Trim());
                }
                sb.AppendLine();
            }

            string t = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(t) ? null : t;
        }
        catch { return null; }
    }

    // ===== BARCODE FROM OCR TEXT (cu verificare checksum) =====
    private bool TryExtractBarcodeFromText(string text, out string code)
    {
        code = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // scoate tot ce nu e cifră și spațiu ca să găsim grupuri
        // căutăm EAN-13 / UPC-A / EAN-8
        var candidates = Regex.Matches(text, @"\b(\d[\d\s]{6,}\d)\b");
        foreach (Match m in candidates)
        {
            string digits = Regex.Replace(m.Groups[1].Value, @"\s+", "");
            if (digits.Length == 13 && IsValidEan13(digits)) { code = digits; return true; }
            if (digits.Length == 12 && IsValidUpcA(digits)) { code = digits; return true; }
            if (digits.Length == 8 && IsValidEan8(digits)) { code = digits; return true; }
        }

        // fallback: găsește direct 13 cifre consecutive
        var m13 = Regex.Match(text, @"\b\d{13}\b");
        if (m13.Success && IsValidEan13(m13.Value)) { code = m13.Value; return true; }

        return false;
    }

    private bool IsValidEan13(string s)
    {
        if (s.Length != 13) return false;

        int sum = 0;
        for (int i = 0; i < 12; i++)
        {
            int d = s[i] - '0';
            if (d < 0 || d > 9) return false;
            sum += (i % 2 == 0) ? d : d * 3;
        }
        int check = (10 - (sum % 10)) % 10;
        return (s[12] - '0') == check;
    }

    private bool IsValidUpcA(string s)
    {
        if (s.Length != 12) return false;

        int sumOdd = 0, sumEven = 0;
        for (int i = 0; i < 11; i++)
        {
            int d = s[i] - '0';
            if (d < 0 || d > 9) return false;
            if (i % 2 == 0) sumOdd += d; else sumEven += d;
        }
        int total = sumOdd * 3 + sumEven;
        int check = (10 - (total % 10)) % 10;
        return (s[11] - '0') == check;
    }

    private bool IsValidEan8(string s)
    {
        if (s.Length != 8) return false;

        int sum = 0;
        for (int i = 0; i < 7; i++)
        {
            int d = s[i] - '0';
            if (d < 0 || d > 9) return false;
            sum += (i % 2 == 0) ? d * 3 : d;
        }
        int check = (10 - (sum % 10)) % 10;
        return (s[7] - '0') == check;
    }

    // ===== FINAL MESSAGE =====
    private string BuildSpokenResult(string barcode, string offName, string offBrand, string offQty,
                                     string offIngredients, string offAllergens, string ocrText)
    {
        string name = FirstNonEmpty(offName, GuessNameFromOcr(ocrText));
        string qty = FirstNonEmpty(offQty, GuessQuantityFromText(ocrText));
        string ing = FirstNonEmpty(offIngredients, GuessIngredientsFromOcr(ocrText));

        var sb = new StringBuilder();
        sb.Append("Produs detectat. ");

        if (!string.IsNullOrWhiteSpace(name)) sb.Append("Nume: ").Append(name).Append(". ");
        else sb.Append("Numele nu este clar. ");

        if (!string.IsNullOrWhiteSpace(offBrand)) sb.Append("Brand: ").Append(offBrand).Append(". ");
        if (!string.IsNullOrWhiteSpace(qty)) sb.Append("Cantitate: ").Append(qty).Append(". ");
        if (!string.IsNullOrWhiteSpace(offAllergens)) sb.Append("Alergeni: ").Append(offAllergens).Append(". ");

        if (!string.IsNullOrWhiteSpace(ing))
        {
            string shortIng = ing.Trim();
            if (shortIng.Length > 260) shortIng = shortIng.Substring(0, 260) + "...";
            sb.Append("Ingrediente (parțial): ").Append(shortIng).Append(" ");
        }

        if (!string.IsNullOrWhiteSpace(barcode)) sb.Append("Cod: ").Append(barcode).Append(".");

        return sb.ToString().Trim();
    }

    private string GuessNameFromOcr(string ocr)
    {
        if (string.IsNullOrWhiteSpace(ocr)) return null;

        var lines = ocr.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in lines)
        {
            string l = raw.Trim();
            if (l.Length < 4 || l.Length > 60) continue;

            string low = l.ToLowerInvariant();
            if (low.Contains("ingrediente") || low.Contains("ingredients") || low.Contains("alerg")) continue;
            if (Regex.IsMatch(low, @"\b(www|http|\d{13})\b")) continue;

            return l;
        }
        return null;
    }

    private string GuessQuantityFromText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var m = Regex.Match(text, @"\b(\d+(?:[.,]\d+)?\s?(?:ml|l|g|kg))\b", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.Replace(",", ".").Trim();

        m = Regex.Match(text, @"\b(\d+)\s*x\s*(\d+(?:[.,]\d+)?\s?(?:ml|l|g|kg))\b", RegexOptions.IgnoreCase);
        if (m.Success) return $"{m.Groups[1].Value} x {m.Groups[2].Value.Replace(",", ".").Trim()}";

        return null;
    }

    private string GuessIngredientsFromOcr(string ocr)
    {
        if (string.IsNullOrWhiteSpace(ocr)) return null;

        string low = ocr.ToLowerInvariant();
        int idx = low.IndexOf("ingrediente");
        if (idx < 0) idx = low.IndexOf("ingredients");
        if (idx < 0) return null;

        string sub = ocr.Substring(idx);
        int colon = sub.IndexOf(':');
        if (colon >= 0 && colon + 1 < sub.Length) sub = sub.Substring(colon + 1);

        sub = sub.Replace("\r", " ").Replace("\n", " ");
        while (sub.Contains("  ")) sub = sub.Replace("  ", " ");
        return sub.Trim();
    }

    private string FirstNonEmpty(params string[] v)
    {
        foreach (var s in v)
            if (!string.IsNullOrWhiteSpace(s))
                return s.Trim();
        return null;
    }

    private string Safe(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}