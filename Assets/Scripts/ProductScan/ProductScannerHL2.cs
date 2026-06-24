using System;
using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

[DisallowMultipleComponent]
public class ProductScannerHL2 : MonoBehaviour
{
    [Header("Core")]
    public AzureTTS azureTTS;
    public SpeechUIAnimator speechUI;
    public PhotoCaptureManager photoCapture;

    [Header("Services")]
    public BarcodeDecoderService barcodeDecoder;
    public OpenFoodFactsService productDb;
    public SimpleProductOcrParser ocrParser;
    public ProductOcrBridge ocrBridge;

    [Header("Audio guidance")]
    public BeepGuide beepGuide;

    [Header("OCR first")]
    public float settleDelay = 0.05f;
    public float ocrSearchDuration = 0.65f;
    [Range(1, 4)] public int maxOcrFrames = 2;
    public float ocrFrameDelay = 0.02f;
    public bool speakOcrHints = false;
    public float ocrHintInterval = 0.8f;

    [Header("OCR quality")]
    [Range(4, 40)] public int minimumUsefulOcrLength = 8;
    [Range(1, 8)] public int frontMinUsefulFrames = 1;
    [Range(1, 8)] public int backMinUsefulFrames = 2;
    public bool mergeMultipleOcrFrames = true;

    [Header("Optional barcode confirmation")]
    public bool tryBarcodeAfterOcr = true;
    public float barcodeSearchDuration = 0.45f;
    public float delayBetweenAttempts = 0.03f;
    public bool runOcrEvenIfBarcodeFound = false;

    [Header("Response tuning")]
    public bool speakDetailedIngredientsAutomatically = true;
    public bool speakFastIdentityWhenDatabaseFound = false;
    public int fallbackRawTextMaxLength = 700;
    public int detailedRawTextMaxLength = 500;

    [Header("Stability / timeouts")]
    public float backSidePrepareDelay = 1.10f;
    public float photoCaptureTimeoutSeconds = 2.80f;
    public float ocrReadTimeoutSeconds = 3.20f;
    public float extraBackSideSearchSeconds = 1.20f;

    public float barcodeDecodeTimeoutSeconds = 1.20f;
    public float databaseLookupTimeoutSeconds = 3.50f;

    public bool IsRunning { get; private set; }
    public bool IsReadingProductInfo { get; private set; }

    private Coroutine _scanRoutine;
    private ProductScanResult _lastResult;

    private enum ProductFlowStep
    {
        None,
        FrontSide,
        BackSide,
        FinalSummary
    }

    private ProductFlowStep currentStep = ProductFlowStep.None;

    private void Awake()
    {
        if (azureTTS == null) azureTTS = FindObjectOfType<AzureTTS>(true);
        if (speechUI == null) speechUI = FindObjectOfType<SpeechUIAnimator>(true);
        if (photoCapture == null) photoCapture = FindObjectOfType<PhotoCaptureManager>(true);
        if (barcodeDecoder == null) barcodeDecoder = FindObjectOfType<BarcodeDecoderService>(true);
        if (productDb == null) productDb = FindObjectOfType<OpenFoodFactsService>(true);
        if (ocrParser == null) ocrParser = FindObjectOfType<SimpleProductOcrParser>(true);
        if (ocrBridge == null) ocrBridge = FindObjectOfType<ProductOcrBridge>(true);
        if (beepGuide == null) beepGuide = FindObjectOfType<BeepGuide>(true);
    }

    public void StartScan()
    {
        if (IsRunning) return;

        if (photoCapture == null)
        {
            Fail("Eroare: modulul camerei nu este disponibil.");
            return;
        }

        if (ocrBridge == null)
        {
            Fail("Eroare: modulul OCR pentru produs lipsește.");
            return;
        }

        IsRunning = true;
        IsReadingProductInfo = false;
        _lastResult = null;

        _scanRoutine = StartCoroutine(ScanRoutine());
    }

    public void StopScan(bool silent = false)
    {
        if (_scanRoutine != null)
        {
            StopCoroutine(_scanRoutine);
            _scanRoutine = null;
        }

        IsReadingProductInfo = false;
        ResetRunState();

        if (!silent)
        {
            speechUI?.ShowSuccess("Am oprit scanarea produsului.");
            azureTTS?.StopNow();
            azureTTS?.Speak("Am oprit scanarea produsului.");
        }
    }

    public void StopProductInfo()
    {
        IsReadingProductInfo = false;
        azureTTS?.StopNow();
        speechUI?.ShowSuccess("Am oprit citirea informațiilor despre produs.");
    }

    public void StopScanCelebrate()
    {
        StopScan(silent: true);
        speechUI?.ShowSuccess("Perfect. Mă bucur că ai găsit produsul.");
        azureTTS?.StopNow();
        azureTTS?.Speak("Perfect. Mă bucur că ai găsit produsul.");
    }

    public void SpeakMoreDetails()
    {
        if (_lastResult == null || !_lastResult.HasUsefulInfo())
        {
            azureTTS?.StopNow();
            azureTTS?.Speak("Nu am încă suficiente informații despre produs.");
            return;
        }

        string details = BuildDetailedSummary(_lastResult);
        IsReadingProductInfo = true;
        azureTTS?.StopNow();
        azureTTS?.Speak(details);
    }

    private void ResetRunState()
    {
        beepGuide?.SetEnabled(false);
        _scanRoutine = null;
        IsRunning = false;
        currentStep = ProductFlowStep.None;
    }

    private IEnumerator ScanRoutine()
    {
        azureTTS?.StopNow();

        ProductScanResult result = new ProductScanResult();
        _lastResult = result;

        yield return new WaitForSeconds(settleDelay);
        if (!IsRunning) yield break;

        currentStep = ProductFlowStep.FrontSide;

        speechUI?.ShowSuccess("Ține produsul aproape de ochelari. Scanez fața produsului.");
        azureTTS?.Speak("Ține produsul aproape de ochelari. Scanez fața produsului.");

        yield return new WaitForSeconds(0.10f);
        if (!IsRunning) yield break;

        string frontMergedText = null;
        byte[] bestFrontPhoto = null;

        yield return StartCoroutine(CaptureAndParseOcrPhase(
            result,
            null,
            text => frontMergedText = text,
            photoBytes => bestFrontPhoto = photoBytes
        ));

        if (!IsRunning) yield break;

        result.frontRawText = frontMergedText;

        speechUI?.ShowUI("Întoarce produsul pe verso.");
        azureTTS?.StopNow();
        azureTTS?.Speak("Întoarce produsul pe verso.");

        yield return new WaitForSeconds(2.2f);
        if (!IsRunning) yield break;

        speechUI?.ShowUI("Ține produsul nemișcat. Încep scanarea produsului de pe verso.");
        azureTTS?.StopNow();
        azureTTS?.Speak("Ține produsul nemișcat. Încep scanarea produsului de pe verso.");

      
        yield return new WaitForSeconds(2.0f);
        if (!IsRunning) yield break;

        currentStep = ProductFlowStep.BackSide;

       

        yield return new WaitForSeconds(0.12f);
        if (!IsRunning) yield break;

        string backMergedText = null;
        byte[] bestBackPhoto = null;

        yield return StartCoroutine(CaptureAndParseOcrPhase(
            result,
            null,
            text => backMergedText = text,
            photoBytes => bestBackPhoto = photoBytes
        ));

        if (!IsRunning) yield break;
        result.backRawText = backMergedText;
        if (string.IsNullOrWhiteSpace(result.frontRawText) && string.IsNullOrWhiteSpace(result.backRawText))
        {
            string quickFail = "Nu am reușit să citesc clar ambalajul. Ține produsul mai aproape, drept și nemișcat.";
            speechUI?.ShowError(quickFail);
            IsReadingProductInfo = true;
            azureTTS?.StopNow();
            azureTTS?.Speak(quickFail);
            ResetRunState();
            yield break;
        }

        string combinedRawText = BuildCombinedRawText(result.frontRawText, result.backRawText);

        if (!string.IsNullOrWhiteSpace(combinedRawText))
        {
            result.rawOcrText = combinedRawText;

            if (ocrParser != null)
                ocrParser.ParseIntoResult(combinedRawText, result);
        }

        // ADAUGAT: Re-parsează explicit textul de pe verso pentru ingrediente și alergeni
        // Verso-ul conține aproape sigur aceste informații — nu lăsăm parsarea combinată să le rateze
        if (!string.IsNullOrWhiteSpace(backMergedText) && ocrParser != null)
        {
            if (string.IsNullOrWhiteSpace(result.ingredients))
            {
                string backIngredients = ocrParser.ExtractIngredients(backMergedText);
                if (!string.IsNullOrWhiteSpace(backIngredients))
                    result.ingredients = backIngredients;
            }

            if (string.IsNullOrWhiteSpace(result.allergens))
            {
                string backAllergens = ocrParser.ExtractAllergens(backMergedText);
                if (!string.IsNullOrWhiteSpace(backAllergens))
                    result.allergens = backAllergens;
            }

            if (string.IsNullOrWhiteSpace(result.expiryDate))
            {
                string backExpiry = ocrParser.ExtractExpiry(backMergedText);
                if (!string.IsNullOrWhiteSpace(backExpiry))
                    result.expiryDate = backExpiry;
            }
        }


        int structuredCount = 0;
        if (!string.IsNullOrWhiteSpace(result.productName)) structuredCount++;
        if (!string.IsNullOrWhiteSpace(result.quantity)) structuredCount++;
        if (!string.IsNullOrWhiteSpace(result.ingredients)) structuredCount++;
        if (!string.IsNullOrWhiteSpace(result.allergens)) structuredCount++;
        if (!string.IsNullOrWhiteSpace(result.expiryDate)) structuredCount++;

        bool hasAnyRawText =
            !string.IsNullOrWhiteSpace(result.frontRawText) ||
            !string.IsNullOrWhiteSpace(result.backRawText) ||
            !string.IsNullOrWhiteSpace(result.rawOcrText);

        bool needsBarcodeFallback =
    string.IsNullOrWhiteSpace(result.productName) &&
    string.IsNullOrWhiteSpace(result.quantity) &&
    string.IsNullOrWhiteSpace(result.ingredients) &&
    string.IsNullOrWhiteSpace(result.expiryDate);

        if (tryBarcodeAfterOcr && needsBarcodeFallback)
        {
            byte[] barcodeSeed = bestBackPhoto ?? bestFrontPhoto;
            yield return StartCoroutine(TryBarcodeConfirmation(result, barcodeSeed));
            if (!IsRunning) yield break;
        }

        currentStep = ProductFlowStep.FinalSummary;

        string finalSummary = BuildFinalProductSummary(result);
        result.finalSummary = finalSummary;

        if (!string.IsNullOrWhiteSpace(finalSummary))
        {
            speechUI?.ShowSuccess(finalSummary);
            IsReadingProductInfo = true;
            azureTTS?.StopNow();
            azureTTS?.Speak(finalSummary);
        }
        else if (hasAnyRawText)
        {
            string fallback = BuildFallbackFromRawTexts(result);
            speechUI?.ShowSuccess(fallback);
            IsReadingProductInfo = true;
            azureTTS?.StopNow();
            azureTTS?.Speak(fallback);
        }
        else
        {
            Fail("Nu am reușit să citesc suficient de clar produsul. Ține ambalajul mai aproape și mai fix.");
            yield break;
        }

        ResetRunState();
    }

    private IEnumerator CaptureAndParseOcrPhase(
     ProductScanResult result,
     byte[] seedPhoto,
     Action<string> onMergedTextReady,
     Action<byte[]> onBestPhotoReady)
    {
        if (ocrBridge == null)
        {
            onMergedTextReady?.Invoke(null);
            yield break;
        }

        beepGuide?.SetEnabled(true);
        beepGuide?.SetMode(BeepGuide.Mode.Searching);

        byte[] bestCapturedPhoto = seedPhoto;
        string mergedText = null;
        string bestCandidateText = null;
        int bestCandidateScore = -1;

        StringBuilder cumulativeText = new StringBuilder();
        int usefulFrameCount = 0;

        if (currentStep == ProductFlowStep.FrontSide)
            speechUI?.ShowUI("Scanez fața produsului...");
        else if (currentStep == ProductFlowStep.BackSide)
            speechUI?.ShowUI("Scanez verso...");
        else
            speechUI?.ShowUI("Scanez produsul...");

        int attempts = Mathf.Clamp(maxOcrFrames, 1, 6);

        if (currentStep == ProductFlowStep.BackSide)
            attempts = Mathf.Max(attempts, 4);
        else
            attempts = Mathf.Max(attempts, 3);

        int targetUsefulFrames =
            currentStep == ProductFlowStep.BackSide
            ? Mathf.Max(1, backMinUsefulFrames)
            : Mathf.Max(1, frontMinUsefulFrames);

        float duration = Mathf.Max(0.6f, ocrSearchDuration);
        if (currentStep == ProductFlowStep.BackSide)
            duration += Mathf.Max(0f, extraBackSideSearchSeconds);

        float deadline = Time.time + duration;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            if (!IsRunning || Time.time > deadline)
                break;

            byte[] photoBytes = null;
            bool photoDone = false;

            photoCapture.TakePhoto(bytes =>
            {
                photoBytes = bytes;
                photoDone = true;
            });

            float captureStart = Time.time;

            while (!photoDone && Time.time - captureStart < photoCaptureTimeoutSeconds)
            {
                if (!IsRunning)
                {
                    beepGuide?.SetEnabled(false);
                    yield break;
                }

                beepGuide?.Tick();
                yield return null;
            }

            if (!photoDone)
            {
                if (attempt < attempts - 1)
                    yield return new WaitForSeconds(Mathf.Max(0.03f, ocrFrameDelay));
                continue;
            }

            if (!IsRunning)
            {
                beepGuide?.SetEnabled(false);
                yield break;
            }

            if (photoBytes == null || photoBytes.Length == 0)
            {
                if (attempt < attempts - 1)
                    yield return new WaitForSeconds(Mathf.Max(0.03f, ocrFrameDelay));
                continue;
            }

            bestCapturedPhoto = photoBytes;

            string rawOcr = null;
            bool ocrDone = false;

            Coroutine ocrRoutine = StartCoroutine(ocrBridge.ReadTextFromJpg(photoBytes, text =>
            {
                rawOcr = text;
                ocrDone = true;
            }));

            float ocrStart = Time.time;

            while (!ocrDone && Time.time - ocrStart < ocrReadTimeoutSeconds)
            {
                if (!IsRunning)
                {
                    if (ocrRoutine != null) StopCoroutine(ocrRoutine);
                    beepGuide?.SetEnabled(false);
                    yield break;
                }

                yield return null;
            }

            if (!ocrDone)
            {
                if (ocrRoutine != null) StopCoroutine(ocrRoutine);

                if (attempt < attempts - 1)
                    yield return new WaitForSeconds(Mathf.Max(0.03f, ocrFrameDelay));

                continue;
            }

            if (!IsRunning)
            {
                beepGuide?.SetEnabled(false);
                yield break;
            }

            string candidate = NormalizeMergedOcr(rawOcr);

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                int score = ScoreOcrCandidate(candidate);

                if (score > bestCandidateScore)
                {
                    bestCandidateScore = score;
                    bestCandidateText = candidate;
                }

                if (IsUsefulOcrCandidate(candidate))
                {
                    usefulFrameCount++;

                    if (mergeMultipleOcrFrames)
                        cumulativeText = new StringBuilder(MergeUniqueOcrText(cumulativeText.ToString(), candidate));
                    else
                        cumulativeText = new StringBuilder(candidate);
                }

                string currentMerged = NormalizeMergedOcr(cumulativeText.ToString());

                if (!string.IsNullOrWhiteSpace(currentMerged))
                    mergedText = currentMerged;

                if (usefulFrameCount >= targetUsefulFrames && !string.IsNullOrWhiteSpace(mergedText))
                {
                    // Pe fața produsului putem opri mai repede.
                    // Pe verso NU oprim până nu vedem indicii reale de etichetă:
                    // ingrediente, alergeni, valori nutriționale, termen etc.
                    if (currentStep != ProductFlowStep.BackSide ||
                        LooksLikeBackSideProductInfo(mergedText) ||
                        Time.time > deadline - 0.20f)
                    {
                        break;
                    }
                }
            }

            if (attempt < attempts - 1)
                yield return new WaitForSeconds(Mathf.Max(0.03f, ocrFrameDelay));
        }

        beepGuide?.SetEnabled(false);

        if (string.IsNullOrWhiteSpace(mergedText))
            mergedText = bestCandidateText;

        if (!string.IsNullOrWhiteSpace(mergedText))
        {
            result.rawOcrText = mergedText;

            if (ocrParser != null)
                ocrParser.ParseIntoResult(mergedText, result);
        }

        onBestPhotoReady?.Invoke(bestCapturedPhoto);
        onMergedTextReady?.Invoke(mergedText);
    }

    private IEnumerator TryBarcodeConfirmation(ProductScanResult result, byte[] seedPhoto)
    {
        if (barcodeDecoder == null || productDb == null)
            yield break;

        string barcode = null;
        bool barcodeFound = false;
        byte[] bestPhoto = seedPhoto;

        float searchEndTime = Time.time + Mathf.Max(0.2f, barcodeSearchDuration);
        float hardBarcodeDeadline = Time.time + Mathf.Max(0.5f, barcodeDecodeTimeoutSeconds);

        while (IsRunning && Time.time < searchEndTime && Time.time < hardBarcodeDeadline && !barcodeFound)
        {
            byte[] photoBytes = null;
            bool photoDone = false;

            if (bestPhoto == null)
            {
                photoCapture.TakeBestPhotoForOCR(bytes =>
                {
                    photoBytes = bytes;
                    photoDone = true;
                });

                float captureStart = Time.time;

                while (!photoDone && Time.time - captureStart < photoCaptureTimeoutSeconds)
                {
                    if (!IsRunning)
                        yield break;

                    yield return null;
                }

                if (!photoDone)
                    break;
            }
            else
            {
                photoBytes = bestPhoto;
                bestPhoto = null;
                photoDone = true;
            }

            if (!IsRunning)
                yield break;

            if (photoBytes == null || photoBytes.Length == 0)
            {
                yield return new WaitForSeconds(delayBetweenAttempts);
                continue;
            }

            try
            {
                if (barcodeDecoder.TryDecode(photoBytes, out barcode) && !string.IsNullOrWhiteSpace(barcode))
                {
                    barcodeFound = true;
                    break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ProductScannerHL2] Barcode decode exception: " + e.Message);
                break;
            }

            yield return new WaitForSeconds(delayBetweenAttempts);
        }

        if (!IsRunning || !barcodeFound)
            yield break;

        result.barcodeFound = true;
        result.barcodeValue = barcode;

        OpenFoodFactsProductResponse dbResponse = null;
        bool dbDone = false;

        Coroutine dbRoutine = StartCoroutine(productDb.LookupBarcode(result.barcodeValue, r =>
        {
            dbResponse = r;
            dbDone = true;
        }));

        float dbStart = Time.time;
        float dbTimeout = Mathf.Max(1.0f, databaseLookupTimeoutSeconds);

        while (!dbDone && Time.time - dbStart < dbTimeout)
        {
            if (!IsRunning)
            {
                if (dbRoutine != null)
                    StopCoroutine(dbRoutine);
                yield break;
            }

            yield return null;
        }

        if (!dbDone)
        {
            if (dbRoutine != null)
                StopCoroutine(dbRoutine);

            Debug.LogWarning("[ProductScannerHL2] Database lookup timeout for barcode: " + result.barcodeValue);
            yield break;
        }

        if (!IsRunning)
            yield break;

        if (dbResponse != null && dbResponse.status == 1 && dbResponse.product != null)
        {
            result.hasDatabaseMatch = true;
            FillMissingFromDatabase(result, dbResponse.product);
            _lastResult = result;

            speechUI?.ShowSuccess("Produs confirmat în baza de date.");
        }
    }

    private void FillMissingFromDatabase(ProductScanResult result, OpenFoodFactsProduct product)
    {
        if (result == null || product == null) return;

        if (string.IsNullOrWhiteSpace(result.productName))
            result.productName = Safe(product.product_name, product.generic_name);

        if (string.IsNullOrWhiteSpace(result.brand))
            result.brand = Safe(product.brands);

        if (string.IsNullOrWhiteSpace(result.quantity))
            result.quantity = Safe(product.quantity);

        if (string.IsNullOrWhiteSpace(result.ingredients))
            result.ingredients = Safe(product.ingredients_text);

        if (string.IsNullOrWhiteSpace(result.allergens))
            result.allergens = Safe(product.allergens_from_ingredients, product.allergens);

        result.sourceSummary = "ocr + barcode + baza de date";
    }

    private string NormalizeMergedOcr(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return s;

        s = s.Replace("\r", "\n");

        while (s.Contains("\n\n\n"))
            s = s.Replace("\n\n\n", "\n\n");

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    private string BuildDetailedSummary(ProductScanResult r)
    {
        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(r.productName))
            sb.Append("Produs: ").Append(r.productName).Append(". ");

        if (!string.IsNullOrWhiteSpace(r.brand))
            sb.Append("Brand: ").Append(r.brand).Append(". ");

        if (!string.IsNullOrWhiteSpace(r.quantity))
            sb.Append("Cantitate: ").Append(r.quantity).Append(". ");

        if (!string.IsNullOrWhiteSpace(r.expiryDate))
            sb.Append("Data expirării: ").Append(r.expiryDate).Append(". ");

        //if (!string.IsNullOrWhiteSpace(r.allergens))
        //    sb.Append("Alergeni: ").Append(r.allergens).Append(". ");

        if (!string.IsNullOrWhiteSpace(r.ingredients))
            sb.Append("Ingrediente complete: ").Append(LimitForSpeech(r.ingredients, 900)).Append(". ");

        // Dacă nu avem ingrediente structurate, citim din raw
        if (string.IsNullOrWhiteSpace(r.ingredients) &&
            !string.IsNullOrWhiteSpace(r.rawOcrText))
        {
            sb.Append("Text complet citit de pe ambalaj: ")
              .Append(LimitForSpeech(r.rawOcrText, detailedRawTextMaxLength))
              .Append(". ");
        }

        string msg = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(msg)
            ? "Nu am găsit mai multe informații despre produs."
            : msg;
    }
    private void Fail(string msg)
    {
        IsReadingProductInfo = false;
        ResetRunState();

        speechUI?.ShowError(msg);
        azureTTS?.StopNow();
        azureTTS?.Speak(msg);
    }

    private string Safe(params string[] values)
    {
        if (values == null) return null;

        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    private string LimitForSpeech(string s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return s;

        s = s.Replace("\r", " ").Replace("\n", ". ");

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        s = s.Trim();
        return s.Length <= max ? s : s.Substring(0, max) + "...";
    }

    private string BuildFinalProductSummary(ProductScanResult r)
    {
        StringBuilder sb = new StringBuilder();
        bool hasUsefulInfo = false;

        sb.Append("Am terminat scanarea produsului. ");

        if (!string.IsNullOrWhiteSpace(r.productName))
        {
            sb.Append("Produsul este ").Append(r.productName).Append(". ");
            hasUsefulInfo = true;
        }

        if (!string.IsNullOrWhiteSpace(r.brand))
        {
            sb.Append("Brand: ").Append(r.brand).Append(". ");
            hasUsefulInfo = true;
        }

        if (!string.IsNullOrWhiteSpace(r.quantity))
        {
            sb.Append("Cantitate: ").Append(r.quantity).Append(". ");
            hasUsefulInfo = true;
        }
        string frontHighlights = ExtractFrontLabelHighlights(r);
        if (!string.IsNullOrWhiteSpace(frontHighlights))
        {
            sb.Append("Pe fața etichetei am citit: ")
              .Append(frontHighlights)
              .Append(". ");
            hasUsefulInfo = true;
        }

        if (!string.IsNullOrWhiteSpace(r.expiryDate))
        {
            sb.Append("Data expirării: ").Append(r.expiryDate).Append(". ");
            hasUsefulInfo = true;
        }

        //if (!string.IsNullOrWhiteSpace(r.allergens))
        //{
        //    sb.Append("Alergeni detectați: ").Append(LimitForSpeech(r.allergens, 350)).Append(". ");
        //    hasUsefulInfo = true;
        //}

        if (!string.IsNullOrWhiteSpace(r.ingredients))
        {
            sb.Append("Ingrediente: ").Append(LimitForSpeech(r.ingredients, 800)).Append(". ");
            hasUsefulInfo = true;
        }

        if (string.IsNullOrWhiteSpace(r.ingredients) && !string.IsNullOrWhiteSpace(r.backRawText))
        {
            string ingredientsFromBack = ExtractSectionByMarkers(
                r.backRawText,
                new[] { "ingrediente", "ingredients" },
                new[] { "valori nutritionale", "valori nutriționale", "nutrition facts",
                    "depozitare", "mod de pastrare", "a se pastra",
                    "fabricat", "distribuit", "lot nr" },
                800);

            if (!string.IsNullOrWhiteSpace(ingredientsFromBack))
            {
                sb.Append("Ingrediente: ").Append(ingredientsFromBack).Append(". ");
                hasUsefulInfo = true;
            }
        }

        //if (string.IsNullOrWhiteSpace(r.allergens) && !string.IsNullOrWhiteSpace(r.backRawText))
        //{
        //    string allergensFromBack = ExtractSectionByMarkers(
        //        r.backRawText,
        //        new[] { "alergeni", "allergens", "conține", "contine",
        //            "poate conține", "poate contine" },
        //        new[] { "valori nutritionale", "valori nutriționale", "nutrition facts",
        //            "depozitare", "fabricat", "distribuit", "lot nr" },
        //        350);

        //    if (!string.IsNullOrWhiteSpace(allergensFromBack))
        //    {
        //        sb.Append("Alergeni detectați: ").Append(allergensFromBack).Append(". ");
        //        hasUsefulInfo = true;
        //    }
        //}

        if (string.IsNullOrWhiteSpace(r.expiryDate) && !string.IsNullOrWhiteSpace(r.backRawText))
        {
            string expiryFromBack = ExtractExpiryHintFromRaw(r.backRawText);
            if (!string.IsNullOrWhiteSpace(expiryFromBack))
            {
                sb.Append("Data expirării: ").Append(expiryFromBack).Append(". ");
                hasUsefulInfo = true;
            }
        }

        if (!hasUsefulInfo)
        {
            string combined = BuildCombinedRawText(r.frontRawText, r.backRawText);
            if (!string.IsNullOrWhiteSpace(combined))
            {
                sb.Append("Am găsit următorul text pe ambalaj: ")
                  .Append(LimitForSpeech(combined, fallbackRawTextMaxLength))
                  .Append(". ");
                hasUsefulInfo = true;
            }
        }

        if (r.hasDatabaseMatch)
            sb.Append("Informațiile au fost confirmate din baza de date. ");

        return hasUsefulInfo ? sb.ToString().Trim() : null;
    }
    private string BuildFallbackFromRawTexts(ProductScanResult r)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Am scanat produsul și îți spun ce am reușit să citesc de pe ambalaj. ");

        string combined = BuildCombinedRawText(r.frontRawText, r.backRawText);
        combined = LimitForSpeech(combined, fallbackRawTextMaxLength);

        if (!string.IsNullOrWhiteSpace(combined))
            sb.Append(combined).Append(". ");
        else
            sb.Append("Nu am reușit să citesc suficient de clar textul de pe ambalaj. ");

        return sb.ToString().Trim();
    }

    private string BuildCombinedRawText(string front, string back)
    {
        StringBuilder sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(front))
            sb.AppendLine(front.Trim());

        if (!string.IsNullOrWhiteSpace(back))
        {
            if (sb.Length > 0)
                sb.AppendLine();

            sb.AppendLine(back.Trim());
        }

        return NormalizeMergedOcr(sb.ToString());
    }

    private string BuildEssentialExtras(ProductScanResult r)
    {
        if (string.IsNullOrWhiteSpace(r.rawOcrText) && string.IsNullOrWhiteSpace(r.backRawText))
            return null;

        // Preferăm textul de pe verso pentru extras, că acolo sunt ingredientele
        string sourceText = !string.IsNullOrWhiteSpace(r.backRawText)
            ? r.backRawText
            : r.rawOcrText;

        StringBuilder sb = new StringBuilder();

        if (string.IsNullOrWhiteSpace(r.ingredients))
        {
            // Stop markers fără "alergeni" — ingredientele pot fi urmate de alergeni pe ambalaj
            string ingredientsHint = ExtractSectionByMarkers(
                sourceText,
                new[] { "ingrediente", "ingredients" },
                new[] { "valori nutritionale", "valori nutriționale", "nutrition facts",
                    "depozitare", "mod de pastrare", "mod de păstrare",
                    "expira", "expirare", "lot nr", "fabricat", "distribuit" },
                600);

            if (!string.IsNullOrWhiteSpace(ingredientsHint))
                sb.Append("Ingrediente: ").Append(ingredientsHint).Append(". ");
        }

        //if (string.IsNullOrWhiteSpace(r.allergens))
        //{
        //    string allergensHint = ExtractSectionByMarkers(
        //        sourceText,
        //        new[] { "alergeni", "allergens", "conține", "contine",
        //            "poate conține", "poate contine" },
        //        new[] { "valori nutritionale", "valori nutriționale", "nutrition facts",
        //            "depozitare", "mod de pastrare", "mod de păstrare",
        //            "expira", "expirare", "lot nr", "fabricat", "distribuit" },
        //        300);

        //    if (!string.IsNullOrWhiteSpace(allergensHint))
        //        sb.Append("Alergeni detectați: ").Append(allergensHint).Append(". ");
        //}

        if (string.IsNullOrWhiteSpace(r.expiryDate))
        {
            string expiryHint = ExtractExpiryHintFromRaw(sourceText);
            if (!string.IsNullOrWhiteSpace(expiryHint))
                sb.Append("Data expirării: ").Append(expiryHint).Append(". ");
        }

        return sb.Length > 0 ? sb.ToString() : null;
    }

    private string ExtractSectionByMarkers(string rawText, string[] markers, string[] stopMarkers, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(rawText) || markers == null || markers.Length == 0)
            return null;

        string normalized = rawText.Replace("\r\n", "\n").Replace("\r", "\n");
        string lower = normalized.ToLowerInvariant();

        string best = null;
        int bestScore = -1;

        for (int m = 0; m < markers.Length; m++)
        {
            string marker = markers[m];
            int searchStart = 0;

            while (searchStart < lower.Length)
            {
                int idx = lower.IndexOf(marker, searchStart, StringComparison.Ordinal);
                if (idx < 0)
                    break;

                int start = idx + marker.Length;

                while (start < normalized.Length &&
                       (normalized[start] == ':' || normalized[start] == '-' || normalized[start] == ' ' || normalized[start] == '\n'))
                {
                    start++;
                }

                int end = normalized.Length;

                if (stopMarkers != null)
                {
                    for (int s = 0; s < stopMarkers.Length; s++)
                    {
                        int p = lower.IndexOf(stopMarkers[s], start, StringComparison.Ordinal);
                        if (p >= 0 && p < end)
                            end = p;
                    }
                }

                if (end > start)
                {
                    string section = normalized.Substring(start, end - start).Trim();
                    section = section.Replace("\n", " ").Replace("\r", " ").Trim();

                    while (section.Contains("  "))
                        section = section.Replace("  ", " ");

                    section = section.Trim(':', '-', '.', ',', ';', ' ');

                    if (!string.IsNullOrWhiteSpace(section) && section.Length >= 5)
                    {
                        int score = ScoreSpeechSection(section);

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = section;
                        }
                    }
                }

                searchStart = idx + marker.Length;
            }
        }

        if (string.IsNullOrWhiteSpace(best))
            return null;

        return best.Length <= maxLen ? best : best.Substring(0, maxLen) + "...";
    }

    private int ScoreSpeechSection(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        int letters = 0;
        int digits = 0;
        int punctuation = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetter(c)) letters++;
            if (char.IsDigit(c)) digits++;
            if (c == ',' || c == ';' || c == ':' || c == '%' || c == '(' || c == ')')
                punctuation++;
        }

        return letters * 2 + digits + punctuation;
    }

    private string ExtractExpiryHintFromRaw(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return null;

        string normalized = rawText.Replace("\r\n", "\n").Replace("\r", "\n");

        string[] patterns =
        {
            @"(expira|expirare|exp|best before|best by|a se consuma de preferinta inainte de|a se consuma înainte de|a se consuma inainte de|valabil pana la|valabil până la|valabil pana|valabil până)\s*[:\-]?\s*([^\n]{4,40})",
            @"\b\d{1,2}[./\-]\d{1,2}[./\-]\d{2,4}\b",
            @"\b\d{1,2}[./\-]\d{2,4}\b"
        };

        for (int i = 0; i < patterns.Length; i++)
        {
            Match m = Regex.Match(normalized, patterns[i], RegexOptions.IgnoreCase);
            if (m.Success)
                return m.Value.Trim();
        }

        return null;
    }
    private bool IsUsefulOcrCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.Trim();
        if (t.Length < minimumUsefulOcrLength)
            return false;

        int letters = 0;
        int digits = 0;

        for (int i = 0; i < t.Length; i++)
        {
            if (char.IsLetter(t[i])) letters++;
            if (char.IsDigit(t[i])) digits++;
        }

        return letters >= 4 || (letters >= 2 && digits >= 2);
    }

    private int ScoreOcrCandidate(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        int letters = 0;
        int digits = 0;
        int separators = 0;
        int lines = 0;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsLetter(c)) letters++;
            if (char.IsDigit(c)) digits++;
            if (c == ',' || c == '.' || c == ':' || c == '-' || c == '/' || c == '%')
                separators++;
        }

        lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

        return letters * 3 + digits * 2 + separators + lines * 4;
    }

    private string MergeUniqueOcrText(string existing, string incoming)
    {
        if (string.IsNullOrWhiteSpace(existing))
            return NormalizeMergedOcr(incoming);

        if (string.IsNullOrWhiteSpace(incoming))
            return NormalizeMergedOcr(existing);

        string[] existingLines = NormalizeMergedOcr(existing).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        string[] incomingLines = NormalizeMergedOcr(incoming).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        StringBuilder sb = new StringBuilder();

        for (int i = 0; i < existingLines.Length; i++)
        {
            string line = existingLines[i].Trim();
            if (line.Length >= 2)
                sb.AppendLine(line);
        }

        for (int i = 0; i < incomingLines.Length; i++)
        {
            string line = incomingLines[i].Trim();
            if (line.Length < 2)
                continue;

            string low = line.ToLowerInvariant();
            bool alreadyExists = false;

            for (int j = 0; j < existingLines.Length; j++)
            {
                string ex = existingLines[j].Trim();
                string exLow = ex.ToLowerInvariant();

                if (exLow == low || exLow.Contains(low) || low.Contains(exLow))
                {
                    alreadyExists = true;
                    break;
                }
            }

            if (!alreadyExists)
                sb.AppendLine(line);
        }

        return NormalizeMergedOcr(sb.ToString());
    }
    private bool LooksLikeBackSideProductInfo(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string t = text.ToLowerInvariant();

        return
            t.Contains("ingred") ||
            t.Contains("ingr") ||
            t.Contains("ingredient") ||
            t.Contains("ingredients") ||
            t.Contains("valori nutritionale") ||
            t.Contains("valori nutriționale") ||
            t.Contains("nutrition") ||
            t.Contains("energie") ||
            t.Contains("kcal") ||
            t.Contains("kj") ||
            t.Contains("grasimi") ||
            t.Contains("grăsimi") ||
            t.Contains("zaharuri") ||
            t.Contains("proteine") ||
            t.Contains("sare") ||
            t.Contains("expira") ||
            t.Contains("expirare") ||
            t.Contains("best before") ||
            t.Contains("lot");
    }
    private string ExtractFrontLabelHighlights(ProductScanResult r)
    {
        if (r == null || string.IsNullOrWhiteSpace(r.frontRawText))
            return null;

        string text = r.frontRawText.Replace("\r", "\n");
        string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        StringBuilder sb = new StringBuilder();
        int added = 0;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();

            while (line.Contains("  "))
                line = line.Replace("  ", " ");

            if (line.Length < 3 || line.Length > 70)
                continue;

            string low = line.ToLowerInvariant();

            if (low.Contains("ingrediente")) continue;
            if (low.Contains("ingredients")) continue;
            if (low.Contains("alergeni")) continue;
            if (low.Contains("allergens")) continue;
            if (low.Contains("valori nutritionale")) continue;
            if (low.Contains("valori nutriționale")) continue;
            if (low.Contains("nutrition")) continue;
            if (low.Contains("kcal")) continue;
            if (low.Contains("kj")) continue;
            if (low.Contains("lot")) continue;
            if (low.Contains("expira")) continue;
            if (low.Contains("www")) continue;
            if (low.Contains("http")) continue;

            // Evită să repete exact produsul/cantitatea deja spuse.
            if (!string.IsNullOrWhiteSpace(r.productName) &&
                low == r.productName.ToLowerInvariant())
                continue;

            if (!string.IsNullOrWhiteSpace(r.quantity) &&
                low == r.quantity.ToLowerInvariant())
                continue;

            bool looksRelevant =
                Regex.IsMatch(line, @"[A-Za-zĂÂÎȘȚăâîșț]") &&
                (
                    Regex.IsMatch(line, @"\b\d+([.,]\d+)?\s?(g|kg|ml|l)\b", RegexOptions.IgnoreCase) ||
                    line.Length <= 45 ||
                    low.Contains("fara") ||
                    low.Contains("fără") ||
                    low.Contains("bio") ||
                    low.Contains("eco") ||
                    low.Contains("natural") ||
                    low.Contains("proteine") ||
                    low.Contains("vitamina") ||
                    low.Contains("calciu") ||
                    low.Contains("lapte") ||
                    low.Contains("iaurt") ||
                    low.Contains("ciocolata") ||
                    low.Contains("cereale") ||
                    low.Contains("biscuit") ||
                    low.Contains("suc") ||
                    low.Contains("apa") ||
                    low.Contains("apă")
                );

            if (!looksRelevant)
                continue;

            if (sb.Length > 0)
                sb.Append("; ");

            sb.Append(line);
            added++;

            if (added >= 4)
                break;
        }

        string result = sb.ToString().Trim();
        return string.IsNullOrWhiteSpace(result) ? null : LimitForSpeech(result, 300);
    }
}