using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;


public class VoiceCommandRouter : MonoBehaviour
{
    [Header("Core systems")]
    public AzureTTS azureTTS;
    public SpeechUIAnimator speechUI;

    [Header("Menu UI")]
    public MenuFlowController menuUI;

    [Header("Features")]
    public ScanCommandHandler scanHandler;
    public AzureSceneDescription sceneAI;
    public AzureReadOCR ocrSystem;

    [Header("Product scan")]
    public ProductScannerHL2 productScanner;

    [Header("Object Finder")]
    public ObjectFinderSystem objectFinder;

    [Header("Messages")]
    [TextArea]
    public string welcomeMessage =
    "Bine ai venit în HoloVision. Alege modul de utilizare pentru a continua.";
    [TextArea] public string goodbyeMessage = "Sistemul se închide. Pe curând!";
    
    [Header("Anti-spam")]
    public float commandCooldown = 0.6f;
    private float lastCommandTime = -999f;
    private string lastCmdNorm = "";

    [Header("Camera settle (reduce 'camera ocupată')")]
    [Tooltip("Mic delay înainte de DESCRIERE/TEXT, ca să se elibereze camera după StopFind/StopScan.")]
    public float cameraSettleDelay = 0.20f;

    [Header("Object commands strictness")]
    public bool requireFindPrefix = true;

    [Header("Debug")]
    public bool logFindParsing = false;

    [Header("Find fallback (doar pentru obiecte)")]
    public bool allowFindWithoutPrefixFallback = true;

    [Range(1, 6)] public int implicitFindMaxTokens = 4;

    [Header("Find - two-step")]
    public bool enableTwoStepFind = true;
    public float twoStepFindTimeout = 5f;

    private bool waitingFindTarget = false;
    private float waitingFindUntil = -1f;

    [Header("SOS")]
    public SosFallSystem sos;

    [Header("Light detection")]
    public LightBillModeHL2 lightBill;

    [Header("Walking safety")]
    public WalkingSafetySystem walkingSafety;

    public string ActiveFeatureName { get; set; } = "";

    [Header("Bulb check")]
    public BulbCheckHL2 bulbCheck;

    [Header("UI - Active Feature")]
    public TextMeshProUGUI activeFeatureText;

    [Header("Result panels")]
    public SearchResultPanelUI searchResultUI;


    // Anti-continue token (STOP gesture cancels delayed coroutines)
    private int _stopGeneration = 0;

    public InteractionMode CurrentMode = InteractionMode.NotSelected;

    [TextArea]
    public string startupIntroMessage =
       "Sunt asistentul tău HoloVision. Sunt aici să te ajut și să te mențin în siguranță. Poți conta pe mine!";

    [TextArea]
    public string vocalModeActivatedMessage =
        "Modul vocal a fost activat. Poți începe prin comenzi vocale.";

    [TextArea]
    public string discreetModeActivatedMessage =
        "Modul discret a fost activat. Poți folosi telefonul pentru a trimite comenzile.";

    [Header("Discreet mode policy")]
    public bool allowCriticalVoiceCommandsInDiscreetMode = true;

    [Header("Speech sequencing")]
    public float featuresAnnouncementDelay = 2.2f;

    [Header("Listening auto-show (doar modul Vocal)")]
    public float listeningShowDelay = 3f;
    private Coroutine _showListeningRoutine;
    private Coroutine _speakFeaturesRoutine;
    [Header("Startup speech sequencing")]
    public float modeSelectionDelay = 4.5f;

    [TextArea]
    public string featuresMessage =
  "Meniul opțiunilor este activ. " +
  "Pe telefon, în colțul din stânga sus se află Descrierea mediului. " +
  "În colțul din dreapta sus se află Citirea textului. " +
  "În zona centrală se află Căutarea unui obiect. " +
  "În colțul din stânga jos se află Scanarea produsului. " +
  "În colțul din dreapta jos se află verificarea pentru factura de curent și lumină. " +
  "În orice moment, poți opri funcția activă prin apăsare continuă pe ecranul telefonului sau prin gestul de stop pe HoloLens. " +
  "Totodată, există gestul rapid pentru timp: ridică mâna stângă și uită-te spre ea ca la un ceas și îți voi spune ziua și ora.";

    [TextArea]
    public string vocalFeaturesMessage =
    "Meniul opțiunilor este activ. " +
    "Îți voi prezenta funcțiile disponibile și comenzile vocale pe care le poți folosi. " +

    "Pentru descrierea mediului, opțiunea se află în colțul din stânga sus al telefonului. " +
    "Poți spune DESCRIERE. " +

    "Pentru citirea textului, opțiunea se află în colțul din dreapta sus. " +
    "Poți spune TEXT. " +

    "Pentru căutarea unui obiect, opțiunea se află în zona centrală. " +
    "Poți spune CAUTĂ, urmat de numele obiectului, de exemplu: CAUTĂ TELEFONUL sau CAUTĂ OCHELARII. " +

    "Pentru scanarea unui produs, opțiunea se află în colțul din stânga jos. " +
    "Poți spune SCANEAZĂ PRODUS. " +

    "Pentru verificarea luminii sau a consumului inutil de curent, opțiunea se află în colțul din dreapta jos. " +
    "Poți spune FACTURA CURENT. " +


    "În orice moment, poți opri funcția activă spunând STOP, prin apăsare continuă pe telefon sau prin gestul de stop cu palma. " +

    "Totodată, există gestul rapid pentru timp: ridică mâna stângă și uită-te spre ea ca la un ceas și îți voi spune ziua și ora.";

    [TextArea]
    public string modeSelectionMessage =
     "Alege modul de utilizare. " +
     "Pe telefon, ecranul este împărțit în două zone. " +
     "În jumătatea de sus se află modul vocal. " +
     "În jumătatea de jos se află modul discret. " +
     "În modul vocal poți folosi atât comenzile vocale, cât și telefonul. " +
     "În modul discret folosești telefonul pentru controlul aplicației. " +
     "Spune MOD VOCAL sau MOD DISCRET, ori apasă pe zona corespunzătoare de pe telefon.";

    [Header("Unknown command feedback")]
    [TextArea]
    public string unknownVocalCommandMessage =
    "Nu am înțeles comanda. Te rog repetă folosind una dintre comenzile disponibile: " +
    "DESCRIERE, TEXT, CAUTĂ urmat de numele obiectului, SCANEAZĂ PRODUS sau FACTURA CURENT.";

    [Tooltip("Cooldown pentru mesajul de comandă neînțeleasă, ca să nu se repete prea des.")]
    public float unknownCommandSpeakCooldown = 3.0f;

    private float lastUnknownCommandSpeakTime = -999f;

    [TextArea]
    public string startupAskNameMessage =
    "Bine ai venit în HoloVision. Spune-mi te rog numele tău.";
    [TextArea]
    public string retryUserNameMessage =
    "Repetă, te rog, numele.";
    [Header("User profile")]
    public bool askUserNameOnce = true;
    public bool askUserNameAfterModeSelection = true;
    public bool includeUserNameInStartupGreeting = true;

    [TextArea]
    public string askUserNameMessage =
        "Cum vrei să îți spun? Spune numele tău. Dacă nu dorești, spune SARI.";

    [TextArea]
    public string skipUserNameMessage =
        "Am înțeles. Continuăm fără nume.";

    [TextArea]
    public string userNameSavedTemplate =
        "Încântat de cunoștință, {0}.";

    [TextArea]
    public string returningUserGreetingTemplate =
        "Bine ai venit, {0}.";

    private bool waitingForUserName = false;
    private bool waitingForUserNameConfirmation = false;
    private string pendingDetectedUserName = "";
    private const string PrefUserName = "HoloVision.UserName";
    [TextArea]
    public string confirmUserNameTemplate =
    "Am detectat numele {0}. Este corect? Spune DA sau NU.";

    [TextArea]
    public string userNameRejectedMessage =
        "Bine. Spune din nou numele tău.";

    [TextArea]
    public string userNameConfirmedMessage =
        "Perfect. Numele a fost confirmat.";
    public HoloVisionVisualStatusUI visualUI;

    [Header("Speech recognizer")]
    public UWPSpeechRecognizer uwpSpeech;

    [Header("Azure name capture")]
    public AzureSpeechNameCapture azureNameCapture;
    public bool useAzureSpeechForUserName = true;
    [Header("Mode selection visual timing")]
    public float modeSelectionConfirmDelay = 1.0f;

    private Coroutine _modeSelectionRoutine;

    public ModeSelectionVisualController modeSelectionVisual;

    [Header("Onboarding policy")]
    public bool phoneOnlyNameOnboarding = true;

    [Header("Onboarding flow")]
    public float onboardingToMainMenuDelay = 2.0f;

    private bool discreetLightNextPressChecksBulb = false;
    private float discreetLightNextPressUntil = -999f;


    public enum InteractionMode
    {
        NotSelected,
        Vocal,
        Discreet
    }

    private void Awake()
    {
        if (azureTTS == null) azureTTS = FindObjectOfType<AzureTTS>(true);
        if (speechUI == null) speechUI = FindObjectOfType<SpeechUIAnimator>(true);
        if (menuUI == null) menuUI = FindObjectOfType<MenuFlowController>(true);

        if (scanHandler == null) scanHandler = FindObjectOfType<ScanCommandHandler>(true);
        if (sceneAI == null) sceneAI = FindObjectOfType<AzureSceneDescription>(true);
        if (ocrSystem == null) ocrSystem = FindObjectOfType<AzureReadOCR>(true);
        if (productScanner == null) productScanner = FindObjectOfType<ProductScannerHL2>(true);
        if (objectFinder == null) objectFinder = FindObjectOfType<ObjectFinderSystem>(true);

        if (sos == null) sos = FindObjectOfType<SosFallSystem>(true);
        if (lightBill == null) lightBill = FindObjectOfType<LightBillModeHL2>(true);
        if (bulbCheck == null) bulbCheck = FindObjectOfType<BulbCheckHL2>(true);
        if (activeFeatureText == null)
            activeFeatureText = GameObject.Find("ActiveFeatureText")?.GetComponent<TextMeshProUGUI>();
        if (walkingSafety == null) walkingSafety = FindObjectOfType<WalkingSafetySystem>(true);
        if (visualUI == null)
            visualUI = FindObjectOfType<HoloVisionVisualStatusUI>(true);
        if (uwpSpeech == null) uwpSpeech = FindObjectOfType<UWPSpeechRecognizer>(true);
        if (azureNameCapture == null) azureNameCapture = FindObjectOfType<AzureSpeechNameCapture>(true);
        if (modeSelectionVisual == null)
            modeSelectionVisual = FindObjectOfType<ModeSelectionVisualController>(true);
        if (searchResultUI == null)
            searchResultUI = FindObjectOfType<SearchResultPanelUI>(true);
    }

    private void Start()
    {
        StartCoroutine(StartupRoutine());
    }

    private IEnumerator StartupRoutine()
    {
 
        yield return null;

        CurrentMode = InteractionMode.NotSelected;

        modeSelectionVisual?.SetNoSelection();

        string savedName = GetSavedUserName();

        waitingForUserName = string.IsNullOrWhiteSpace(savedName);
        waitingForUserNameConfirmation = false;
        pendingDetectedUserName = "";

        uwpSpeech?.SetNameCaptureMode(false);
        azureNameCapture?.CancelCapture();


        menuUI?.ShowOnboardingMenu();

        if (!string.IsNullOrWhiteSpace(savedName))
        {
            StartCoroutine(ShowOnboardingThenMainMenu(savedName));
        }
        else
        {
            visualUI?.ShowWelcomeOnly();
            azureTTS?.Speak("Bine ai venit. HoloVision este pregătit să te ajute. Spune numele pe telefon pentru a continua.");
        }

   
    }

    public void AcceptCommand(string rawCommand) => AcceptCommand(rawCommand, rawCommand);
    public void AcceptPhoneCommand(string normalizedOrRaw, string rawFromPhone)
    {
        ProcessCommandInternal(normalizedOrRaw, rawFromPhone, fromPhone: true);
    }

    public void AcceptCommand(string normalizedOrRaw, string rawFromEngine)
    {
        ProcessCommandInternal(normalizedOrRaw, rawFromEngine, fromPhone: false);
    }



    private void HandleFeaturesMenu(string cmd, string raw)
    {
        CancelScheduledListening();
        // 0) TWO-STEP FIND: dacă așteptăm obiectul
        if (enableTwoStepFind && waitingFindTarget)
        {
            if (Time.time > waitingFindUntil)
            {
                waitingFindTarget = false;
                ActiveFeatureName = "";
                speechUI?.ShowError("Nu am auzit obiectul. Spune: CAUTĂ <obiect>.");
                return;
            }
            else
            {
                bool isOtherCommand =
                 IsBack(cmd) || IsDescribe(cmd) || SnapToDescribe(cmd, raw) || IsOCR(cmd) ||
                 IsSafetyOn(cmd) || IsSafetyOff(cmd) ||
                 IsProductScanStart(cmd, raw) || IsProductScanStop(cmd, raw) ||
                 IsLightBill(cmd, raw) ||
                 IsBulbCheck(cmd, raw) ||
                 (productScanner != null && productScanner.IsRunning) ||
                 (lightBill != null && lightBill.IsRunning) ||
                 (bulbCheck != null && bulbCheck.IsRunning);

                if (!isOtherCommand)
                {
                    waitingFindTarget = false;

                    string tail = CleanupFindTail(cmd);
                    tail = PruneFindTail(tail);

                    string canonical = CanonicalizeFindTarget(tail);
                    if (!string.IsNullOrWhiteSpace(canonical)) tail = canonical;

                    if (string.IsNullOrWhiteSpace(tail))
                    {
                        Feedback("Nu am înțeles obiectul. Spune: CAUTĂ <obiect>.", error: true);
                        return;
                    }

                    if (objectFinder == null)
                    {
                        Feedback("Eroare: ObjectFinderSystem lipsește din scenă.", error: true);
                        return;
                    }

                    // oprește alte sisteme înainte de find
                    ocrSystem?.StopReading();
                    scanHandler?.StopSafetyActive();

                    ActiveFeatureName = $"căutarea {tail}";
                    objectFinder.preferPhoneControlInstructions = CurrentMode == InteractionMode.Discreet;
                    objectFinder.StartFind(tail);
                    return;
                }

                // dacă era altă comandă, anulăm waitingFindTarget
                waitingFindTarget = false;
                ActiveFeatureName = "";
                // NU return aici; lăsăm comanda să fie procesată mai jos
            }
        }

        // 1) PRODUCT SCAN (prioritar)
        if (IsProductScanStart(cmd, raw))
        {
            menuUI?.HideListeningIndicator();

            // conflict cameră
            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);   // AICI

            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();
            sceneAI?.Cancel();

            ActiveFeatureName = "scanarea produsului";
            SetActiveFeatureUI("Funcție activă: SCANEAZĂ PRODUS");
            visualUI?.SetLastCommand("SCANARE PRODUS");

            menuUI?.ShowProcessingPanel("SCANARE PRODUS");

            productScanner?.StartScan();

            speechUI?.ShowSuccess("Scanare produs pornită.");
            return;
        }

        if (IsProductScanStop(cmd, raw))
        {
            productScanner?.StopScan(silent: false);
            ActiveFeatureName = "";
            return;
        }

        if (productScanner != null && productScanner.IsRunning)
        {
            if (string.IsNullOrWhiteSpace(ActiveFeatureName))
                ActiveFeatureName = "scanarea produsului";

            if (IsFoundStop(cmd, raw))
            {
                productScanner.StopScanCelebrate();
                ActiveFeatureName = "";
                return;
            }

            speechUI?.ShowError("Ești în scanare produs. Spune: OPREȘTE SCANAREA sau AM GĂSIT.");
            return;
        }

        // 2) BACK
        if (IsBack(cmd))
        {
            menuUI?.HideListeningIndicator();
            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);
            scanHandler?.StopSafetyActive();
            ActiveFeatureName = "";
            SetActiveFeatureUI("");
            menuUI.ShowMainMenu();
            Feedback(welcomeMessage, success: true);
            return;
        }

        // 3) SAFETY
        if (IsSafetyOn(cmd))
        {
            menuUI?.HideListeningIndicator();
            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);

            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            sceneAI?.Cancel();

            if (productScanner != null)
            {
                productScanner.StopScan(silent: true);
                if (productScanner.IsReadingProductInfo) productScanner.StopProductInfo();
            }

            ActiveFeatureName = "siguranța la mers";
            SetActiveFeatureUI("Funcție activă: SIGURANȚĂ LA MERS");
            menuUI?.HideMenuOverlayNow();

            scanHandler?.StartSafetyActive(); // păstrează doar dacă îți trebuie
            walkingSafety?.StartSafety();

            speechUI?.ShowSuccess("Siguranță activă.");
            return;
        }

        if (IsSafetyOff(cmd))
        {
            scanHandler?.StopSafetyActive();
            walkingSafety?.StopSafety(speak: false);

            ActiveFeatureName = "";
            SetActiveFeatureUI("");
            speechUI?.ShowSuccess("Liniște.");
            azureTTS?.Speak("Liniște.");
            return;
        }

        // 4) FOUND / STOP FIND
        if (IsFoundStop(cmd, raw))
        {
            menuUI?.HideListeningIndicator();
            objectFinder?.StopFind(silent: true);
            ActiveFeatureName = "";
            SetActiveFeatureUI("");
            menuUI?.ShowFeaturesMenu();
            ScheduleListeningIfVocal(3f);
            Feedback("Mă bucur că ai găsit ce căutai. Dacă mai ai nevoie de ceva, sunt aici.", success: true);
            return;
        }

        if (IsStopFind(cmd))
        {
            objectFinder?.StopFind(silent: false);
            ActiveFeatureName = "";
            menuUI?.ShowFeaturesMenu();
            ScheduleListeningIfVocal(2f);
            return;
        }

        // 5) ✅ VERIFICĂ BECUL (bulb check) — înainte de LightBill/DESCRIERE/OCR/FIND
        if (IsBulbCheck(cmd, raw))
        {

            menuUI?.HideListeningIndicator();

            // oprește orice poate folosi camera
            lightBill?.StopMode(speak: false);
            objectFinder?.StopFind(silent: true);
            walkingSafety?.StopSafety(speak: false);   // AICI
            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();
            sceneAI?.Cancel();

            if (productScanner != null)
            {
                productScanner.StopScan(silent: true);
                if (productScanner.IsReadingProductInfo) productScanner.StopProductInfo();
            }

            if (bulbCheck == null)
            {
                Feedback("Eroare: modulul de verificare a becului nu este în scenă.", error: true);
                return;
            }


            ActiveFeatureName = "verificarea becului";
            SetActiveFeatureUI("Funcție activă: VERIFICĂ BECUL");
            visualUI?.SetLastCommand("VERIFICĂ BECUL");

            menuUI?.ShowProcessingPanel("VERIFICARE BEC");
            bulbCheck.preferPhoneControlInstructions = CurrentMode == InteractionMode.Discreet;
            StartCoroutine(BulbCheckAfterSettle());
            return;
        }

        // 6)  FACTURA CURENT / VERIFICĂ LUMINA (mod lumină)
        if (IsLightBill(cmd, raw))
        {
            menuUI?.HideListeningIndicator();

            // Oprim orice altă funcție care poate folosi camera.
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();
            sceneAI?.Cancel();

            if (productScanner != null)
            {
                productScanner.StopScan(silent: true);

                if (productScanner.IsReadingProductInfo)
                    productScanner.StopProductInfo();
            }

            if (lightBill == null)
            {
                Feedback("Eroare: modulul de lumină nu este în scenă.", error: true);
                return;
            }

            ActiveFeatureName = "modul factură/lumină";
            SetActiveFeatureUI("Funcție activă: FACTURA CURENT / LUMINĂ");
            visualUI?.SetLastCommand("FACTURA CURENT");

            menuUI?.ShowProcessingPanel("VERIFICARE LUMINĂ");

            // În modul discret, LightBill va spune:
            // "apasă din nou pe opțiunea Factura curent..." dacă ai modificat mesajele din LightBillModeHL2.
            lightBill.preferPhoneControlInstructions = CurrentMode == InteractionMode.Discreet;
            lightBill.StartMode();

            return;
        }
        // 7) DESCRIERE
        if (IsDescribe(cmd) || SnapToDescribe(cmd, raw))
        {

            menuUI?.HideListeningIndicator();
           

            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);   // AICI
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();


            ActiveFeatureName = "descrierea";
            SetActiveFeatureUI("Funcție activă: DESCRIERE");
            visualUI?.SetLastCommand("DESCRIERE");
            menuUI?.ShowProcessingPanel("DESCRIERE MEDIU");
            StartCoroutine(DescribeAfterSettle());
            return;
        }

        // 8) OCR/TEXT
        if (IsOCR(cmd))
        {
            menuUI?.HideListeningIndicator();

            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);   // AICI
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();

            ActiveFeatureName = "citirea textului";
            SetActiveFeatureUI("Funcție activă: TEXT / CITIRE");
            visualUI?.SetLastCommand("TEXT");
            menuUI?.ShowProcessingPanel("CITIRE TEXT");
            StartCoroutine(OCRAfterSettle());
            return;
        }

        // 9) TWO-STEP START: user zice doar “caută/căutare”
        if (enableTwoStepFind && HasFindPrefix(cmd, raw))
        {
            string tail = ExtractFindTailSmart(cmd);
            if (string.IsNullOrWhiteSpace(tail))
            {
                string rawNorm = Normalize(raw);
                tail = ExtractFindTailSmart(rawNorm);
            }

            if (string.IsNullOrWhiteSpace(tail))
            {
                waitingFindTarget = true;
                waitingFindUntil = Time.time + Mathf.Max(2f, twoStepFindTimeout);

                ActiveFeatureName = "căutarea obiectului";
                Feedback("Ce obiect dorești să caut? Spune-mi doar numele obiectului.", success: true);
                return;
            }
        }

        // 10) CAUTĂ <orice>
        if (IsFind(cmd, raw, out string targetRo))
        {
            menuUI?.HideListeningIndicator();
            if (objectFinder == null)
            {
                Feedback("Eroare: ObjectFinderSystem lipsește din scenă.", error: true);
                return;
            }

            // nu rulează în paralel cu lumină/bec
            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);   // AICI
            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();

            if (logFindParsing) Debug.Log("[Router][Find] targetRo = " + targetRo);

            ActiveFeatureName = $"căutarea {targetRo}";
            SetActiveFeatureUI($"Funcție activă: CAUTĂ {targetRo}");
            visualUI?.SetLastCommand("CAUTĂ " + targetRo);

            menuUI?.ShowProcessingPanel("CĂUTARE OBIECT");

            searchResultUI?.ShowSearchStarted(targetRo);
            objectFinder.preferPhoneControlInstructions = CurrentMode == InteractionMode.Discreet;
            objectFinder.StartFind(targetRo);
            return;
        }

        FeedbackUnknownCommand();
    }
    // ──────────────────────────────────────────────────────────────
    // LISTENING AUTO-SHOW
    // ──────────────────────────────────────────────────────────────
    private void ScheduleListeningIfVocal(float delay = -1f)
    {
        CancelScheduledListening();
        if (CurrentMode != InteractionMode.Vocal) return;
        float d = delay < 0 ? listeningShowDelay : delay;
        _showListeningRoutine = StartCoroutine(ShowListeningDelayedRoutine(d));
    }

    private void CancelScheduledListening()
    {
        if (_showListeningRoutine != null)
        {
            StopCoroutine(_showListeningRoutine);
            _showListeningRoutine = null;
        }
    }

    private IEnumerator ShowListeningDelayedRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (CurrentMode == InteractionMode.Vocal &&
            menuUI != null &&
            menuUI.CurrentState == MenuFlowController.State.FeaturesMenu &&
            string.IsNullOrWhiteSpace(ActiveFeatureName))
        {
            menuUI.ShowListeningIndicator();
        }

        _showListeningRoutine = null;
    }

    private IEnumerator DescribeAfterSettle()
    {
        int gen = _stopGeneration;

        if (menuUI != null)
            menuUI.ShowProcessingPanel("DESCRIERE MEDIU");

        yield return new WaitForSeconds(Mathf.Max(1.0f, cameraSettleDelay));

        if (gen != _stopGeneration)
            yield break;

        sceneAI?.AnalyzeScene();
    }

    private IEnumerator OCRAfterSettle()
    {
        int gen = _stopGeneration;

        if (menuUI != null)
            menuUI.ShowProcessingPanel("CITIRE TEXT");

        yield return new WaitForSeconds(Mathf.Max(0.8f, cameraSettleDelay));

        if (gen != _stopGeneration)
            yield break;

        ocrSystem?.ReadText();
    }

    // =========================
    // CANONICALIZE (fix “descriere” -> “cauta disk/discrete”)
    // =========================
    private string CanonicalizeCommand(string cmdNorm, string raw)
    {
        string r = Normalize(raw);

        if (LooksLikeDescribeMisrecognition(cmdNorm, r))
            return "descriere";

        return cmdNorm;
    }

    private bool LooksLikeDescribeMisrecognition(string cmdNorm, string rawNorm)
    {
        if (rawNorm.Contains("description") || rawNorm.Contains("describe"))
            return true;

        if (rawNorm.Contains("descr") || cmdNorm.Contains("descr"))
            return true;

        bool hasWeird =
            rawNorm.Contains("discrete") || rawNorm.Contains("discreet") ||
            rawNorm.Contains("disk") || rawNorm.Contains("disck") || rawNorm.Contains("disc");

        if (!hasWeird) return false;

        if (rawNorm.Contains("caut") || cmdNorm.Contains("caut"))
            return true;

        var toks = Tokens(rawNorm);
        return toks.Length <= 2;
    }

    // =========================
    // PRODUCT SCAN (robust)
    // =========================
    private bool IsProductScanStart(string c, string raw)
    {
        if (IsProductScanStop(c, raw)) return false;

        bool scanHint =
            HasTokenLike(c, "scane") ||
            HasTokenLike(c, "scan") ||
            HasToken(c, "scan") ||
            c.Contains("scan") ||
            HasTokenLike(c, "identific") ||
            HasTokenLike(c, "recunoast") ||
            HasTokenLike(c, "citest");

        if (!scanHint)
        {
            string rr = (raw ?? "").ToLowerInvariant();
            if (!(rr.Contains("scan") || rr.Contains("scane") || rr.Contains("identific") || rr.Contains("recunoa") || rr.Contains("cit")))
                return false;
        }

        bool productHint = HasProductHint(c, raw) || HasTokenLike(c, "etiche") || HasTokenLike(c, "ambalaj");
        bool codeHint = HasCodeHint(c, raw);

        if (codeHint) return true;
        return productHint;
    }

    private bool IsProductScanStop(string c, string raw)
    {
        bool stopVerb =
            HasTokenLike(c, "opres") || HasToken(c, "stop") || HasToken(c, "gata") || HasTokenLike(c, "termin");

        if (!stopVerb) return false;

        bool scanHint =
            HasTokenLike(c, "scanare") || HasToken(c, "scan") || HasTokenLike(c, "scanner");

        bool productHint =
            HasTokenLike(c, "produs") || HasTokenLike(c, "etiche") || HasTokenLike(c, "cod") ||
            HasToken(c, "qr") || HasTokenLike(c, "barcode");

        if (productScanner != null && productScanner.IsRunning)
            return scanHint || productHint;

        return scanHint && productHint;
    }

    private void Feedback(string msg, bool success = false, bool error = false)
    {
        if (speechUI != null)
        {
            if (error) speechUI.ShowError(msg);
            else if (success) speechUI.ShowSuccess(msg);
            else speechUI.ShowUI(msg);
        }

        azureTTS?.Speak(msg);
    }

    private void QuitApp() => Application.Quit();

    // =========================
    // Normalize / tokens
    // =========================
    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";

        input = input.ToLowerInvariant().Trim();
        string normalized = input.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();

        foreach (char ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch) || ch == ' ')
                sb.Append(ch);
            else
                sb.Append(' ');
        }

        string s = sb.ToString().Normalize(NormalizationForm.FormC);

        while (s.Contains("  "))
            s = s.Replace("  ", " ");

        return s.Trim();
    }

    private static string[] Tokens(string normalized)
    {
        if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<string>();
        return normalized.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool HasToken(string normalized, string token)
    {
        var t = Tokens(normalized);
        for (int i = 0; i < t.Length; i++)
            if (t[i] == token) return true;
        return false;
    }

    private static bool HasTokenLike(string normalized, string baseToken)
    {
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(baseToken)) return false;

        baseToken = baseToken.Trim().ToLowerInvariant();
        var t = Tokens(normalized);

        for (int i = 0; i < t.Length; i++)
        {
            string tok = t[i];
            if (tok == baseToken) return true;

            if (tok.StartsWith(baseToken) && tok.Length >= Math.Max(4, baseToken.Length))
                return true;

            if (baseToken.StartsWith(tok) && tok.Length >= 4)
                return true;
        }

        return false;
    }

    // =========================
    // Commands
    // =========================
    private bool IsStart(string c, string raw)
    {
        // ✅ mai tolerant: aliasuri
        if (HasToken(c, "start") || HasToken(c, "porneste") || HasToken(c, "porneste") || HasToken(c, "incepe") ||
            HasToken(c, "activeaza") || HasToken(c, "activeaza"))
            return true;

        if (!string.IsNullOrWhiteSpace(raw) && raw.Trim().Equals("start", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsExit(string c) =>
     c.Contains("inchide aplicatia") ||
     c.Contains("inchide aplicatie") ||
     c.Contains("parasire aplicatie") ||
     c.Contains("iesi din aplicatie");

    private bool IsBack(string c) =>
        HasToken(c, "inapoi") || HasToken(c, "meniu");

    private bool IsDescribe(string c) =>
        HasToken(c, "descriere") || HasToken(c, "descrie");

    private bool IsOCR(string c) =>
        HasToken(c, "text") || HasToken(c, "citire") || HasToken(c, "citeste");

    private bool IsSafetyOn(string c)
    {
        if (HasToken(c, "siguranta") || HasToken(c, "detectie") || HasToken(c, "protectie"))
            return true;

        if (HasToken(c, "scan"))
        {
            bool looksLikeFind =
                c.Contains("caut") || c.Contains("gasest") || c.StartsWith("unde ") ||
                c.Contains("find") || c.Contains("search") ||
                c.Contains("look for");

            if (looksLikeFind) return false;
            return true;
        }

        return false;
    }

    private bool IsSafetyOff(string c) =>
        HasToken(c, "liniste") || HasToken(c, "tacere");

    private bool IsGlobalStop(string c)
    {
        if (HasToken(c, "start") || HasToken(c, "porneste") || HasToken(c, "incepe"))
            return false;

        return HasToken(c, "stop") || HasToken(c, "gata") || HasToken(c, "opreste") ||
               HasToken(c, "termina") || HasToken(c, "termin");
    }

    private bool IsStopFind(string c) =>
        c.Contains("opreste cautarea") || c.Contains("stop cautarea") || c.Contains("anuleaza cautarea");

    private bool IsFoundStop(string c, string raw)
    {
        if (c.Contains("am gasit")) return true;
        if (c.Contains("l am gasit")) return true;
        if (c.Contains("lam gasit")) return true;

        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("am găsit") || r.Contains("am gasit")) return true;
        if (r.Contains("l-am găsit") || r.Contains("l-am gasit") || r.Contains("l am gasit")) return true;

        return false;
    }

    // =========================
    // SnapToDescribe (agresiv ca să NU pice în FIND)
    // =========================
    private bool SnapToDescribe(string cmdNorm, string raw)
    {
        if (IsOCR(cmdNorm) || IsSafetyOn(cmdNorm) || IsSafetyOff(cmdNorm) || IsBack(cmdNorm) || IsGlobalStop(cmdNorm))
            return false;

        string rawNorm = Normalize(raw);

        if (rawNorm.Contains("description") || rawNorm.Contains("describe"))
            return true;

        if (rawNorm.Contains("descr") || cmdNorm.Contains("descr"))
            return true;

        if (LooksLikeDescribeMisrecognition(cmdNorm, rawNorm))
            return true;

        return false;
    }

    // =========================
    // FIND (tolerant)  — neschimbat
    // =========================
    private static bool LooksLikeFindPrefixToken(string tok)
    {
        if (string.IsNullOrWhiteSpace(tok)) return false;

        if (tok.StartsWith("caut") || tok.StartsWith("gasest"))
            return true;

        if (tok.Length <= 7 && tok.Contains("caut"))
            return true;

        if (tok.Length <= 9 && tok.Contains("gasest"))
            return true;

        if (tok.StartsWith("cautare"))
            return true;

        return false;
    }

    private bool HasFindPrefix(string c, string raw)
    {
        var toks = Tokens(c);

        int start = 0;
        if (toks.Length > 1 && (toks[0] == "v" || toks[0] == "va"))
            start = 1;

        for (int i = start; i < toks.Length; i++)
        {
            string t = toks[i];

            if (LooksLikeFindPrefixToken(t)) return true;

            if (t == "find" || t == "search" || (t == "look" && i + 1 < toks.Length && toks[i + 1] == "for"))
                return true;

            if (t == "where" && i + 1 < toks.Length && toks[i + 1] == "is")
                return true;
        }

        if (c.StartsWith("unde ")) return true;

        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("caut") || r.Contains("caută") || r.Contains("cautare")) return true;
        if (r.Contains("găse") || r.Contains("gase")) return true;

        return false;
    }

    private bool IsFind(string c, string raw, out string targetRo)
    {
        targetRo = null;
        if (string.IsNullOrWhiteSpace(c)) return false;

        if (IsDescribe(c) || SnapToDescribe(c, raw) || IsOCR(c))
            return false;

        bool hasPrefix = HasFindPrefix(c, raw);
        string tail = null;

        if (hasPrefix)
        {
            tail = ExtractFindTailSmart(c);

            if (string.IsNullOrWhiteSpace(tail))
            {
                string rawNorm = Normalize(raw);
                tail = ExtractFindTailSmart(rawNorm);
            }

            if (string.IsNullOrWhiteSpace(tail)) return false;
        }
        else
        {
            if (requireFindPrefix && !allowFindWithoutPrefixFallback)
                return false;

            if (!LooksLikeBareObjectRequest(c))
                return false;

            tail = c;
        }

        tail = CleanupFindTail(tail);
        tail = PruneFindTail(tail);

        if (logFindParsing)
            Debug.Log($"[Router][Find] cmd='{c}' tail='{tail}' raw='{raw}' hasPrefix={hasPrefix}");

        if (string.IsNullOrWhiteSpace(tail)) return false;

        string canonical = CanonicalizeFindTarget(tail);
        if (!string.IsNullOrWhiteSpace(canonical)) tail = canonical;

        targetRo = tail.Trim();
        return true;
    }

    private bool LooksLikeBareObjectRequest(string cmdNorm)
    {
        if (string.IsNullOrWhiteSpace(cmdNorm)) return false;

        if (HasTokenLike(cmdNorm, "produs") || HasTokenLike(cmdNorm, "product") || HasTokenLike(cmdNorm, "produ") || HasTokenLike(cmdNorm, "prod") ||
            HasTokenLike(cmdNorm, "etiche") || HasTokenLike(cmdNorm, "ambalaj") ||
            HasTokenLike(cmdNorm, "barcode") || HasTokenLike(cmdNorm, "cod") || HasToken(cmdNorm, "qr"))
            return false;

        if (HasTokenLike(cmdNorm, "scane") || HasTokenLike(cmdNorm, "scan") || HasTokenLike(cmdNorm, "scanare") ||
            HasTokenLike(cmdNorm, "identific") || HasTokenLike(cmdNorm, "recunoast"))
            return false;

        if (IsDescribe(cmdNorm) || IsOCR(cmdNorm) || IsSafetyOn(cmdNorm) || IsSafetyOff(cmdNorm) ||
      IsBack(cmdNorm) || IsGlobalStop(cmdNorm) || IsStopFind(cmdNorm))
            return false;

        if (HasToken(cmdNorm, "start") || HasToken(cmdNorm, "porneste") || HasToken(cmdNorm, "incepe"))
            return false;

        var toks = Tokens(cmdNorm);
        if (toks.Length == 0 || toks.Length > implicitFindMaxTokens) return false;

        string[] junk = { "da", "nu", "ok", "bine", "multumesc", "merci", "salut" };
        if (toks.All(t => junk.Contains(t))) return false;

        return true;
    }

    private static string PruneFindTail(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail)) return tail;

        var toks = Tokens(tail);
        if (toks.Length <= 1) return tail.Trim();

        var cut = new System.Collections.Generic.HashSet<string>
        {
            "si","iar","apoi","dupa","inca","in","plus",
            "and","then","also",
            "please"
        };

        var kept = new System.Collections.Generic.List<string>(toks.Length);

        for (int i = 0; i < toks.Length; i++)
        {
            string t = toks[i];

            if (kept.Count > 0 && t == "te" && i + 1 < toks.Length && toks[i + 1] == "rog") break;
            if (kept.Count > 0 && t == "va" && i + 1 < toks.Length && toks[i + 1] == "rog") break;

            if (kept.Count > 0 && cut.Contains(t)) break;

            kept.Add(t);
        }

        return string.Join(" ", kept).Trim();
    }

    private static string ExtractFindTailSmart(string c)
    {
        if (string.IsNullOrWhiteSpace(c)) return null;

        var toks = Tokens(c);
        if (toks.Length == 0) return null;

        int start = 0;
        if (toks.Length > 1 && (toks[0] == "v" || toks[0] == "va"))
            start = 1;

        if (toks[start] == "unde")
        {
            int idxE = Array.IndexOf(toks, "e");
            int idxEste = Array.IndexOf(toks, "este");
            int idxI = Array.IndexOf(toks, "i");

            int idx = -1;
            if (idxEste >= 0) idx = idxEste;
            else if (idxE >= 0) idx = idxE;
            else if (idxI >= 0) idx = idxI;

            if (idx >= 0 && idx + 1 < toks.Length)
                return string.Join(" ", toks.Skip(idx + 1));
        }

        int p = -1;
        for (int i = start; i < toks.Length; i++)
        {
            string t = toks[i];
            if (LooksLikeFindPrefixToken(t)) { p = i; break; }
            if (t == "find" || t == "search") { p = i; break; }
            if (t == "look" && i + 1 < toks.Length && toks[i + 1] == "for") { p = i + 1; break; }
        }

        if (p >= 0 && p + 1 < toks.Length)
            return string.Join(" ", toks.Skip(p + 1));

        string compact = c.Replace(" ", "");
        if (compact.StartsWith("vcauta")) compact = compact.Substring(1);
        if (compact.StartsWith("vcaut")) compact = compact.Substring(1);

        if (compact.StartsWith("cautare"))
        {
            string rest = compact.Substring(7);
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        if (compact.StartsWith("cauta"))
        {
            string rest = compact.Substring(5);
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        if (compact.StartsWith("caut"))
        {
            string rest = compact.Substring(4);
            if (rest.StartsWith("a")) rest = rest.Substring(1);
            return string.IsNullOrWhiteSpace(rest) ? null : rest;
        }

        return null;
    }

    private static string CleanupFindTail(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail)) return null;
        tail = tail.Trim();

        string[] drop =
        {
        "v","va",
        "un","o","unui","unei","pe","la","te","rog","sa","imi","mi",
        "de","din",
        "obiect","obiectul","asta","acesta","aceasta",
        "si","iar","inca","mai",
        "the","an","please","to","for","of","and","then"
    };

        var toks = Tokens(tail)
            .Where(t => !drop.Contains(t))
            .ToArray();

        return string.Join(" ", toks).Trim();
    }

    private string CanonicalizeFindTarget(string text)
    {
        string norm = Normalize(text);
        if (string.IsNullOrWhiteSpace(norm)) return null;

        string p = " " + norm + " ";

        bool wantsFree =
            p.Contains(" liber ") ||
            p.Contains(" libera ");

        // 1) Scaun / masă liberă trebuie tratate primele.
        if ((p.Contains(" scaun ") || p.Contains(" scaunul ")) && wantsFree)
            return "scaun liber";

        if ((p.Contains(" masa ") || p.Contains(" birou ") || p.Contains(" biroul ")) && wantsFree)
            return "masa libera";

        // 2) UȘĂ are prioritate înainte de MASĂ și înainte de SEMN IEȘIRE.
        // Important: „ușa de ieșire” = ușă, nu semn de ieșire.
        if (norm == "usa" ||
            norm == "usa fata" ||
            norm == "usa intrare" ||
            norm == "usa de iesire" ||
            norm == "usa iesire" ||
            norm == "usa iesirea" ||
            norm == "usa de la intrare" ||
            norm == "usa din fata" ||
            p.Contains(" usa ") ||
            p.Contains(" usa de iesire ") ||
            p.Contains(" usa iesire ") ||
            p.Contains(" usa iesirea ") ||
            p.Contains(" usa de la intrare ") ||
            p.Contains(" usa din fata ") ||
            p.Contains(" poarta "))
        {
            return "usa";
        }

        // 3) Coș de gunoi
        if (p.Contains(" cos de gunoi ") ||
            p.Contains(" cosul de gunoi ") ||
            p.Contains(" gunoi ") ||
            p.Contains(" tomberon ") ||
            p.Contains(" pubela "))
        {
            return "cos gunoi";
        }

        // 4) Fereastră / geam
        if (norm == "fereastra" ||
            norm == "geam" ||
            norm == "geamul" ||
            p.Contains(" fereastra ") ||
            p.Contains(" geam ") ||
            p.Contains(" geamul "))
        {
            return "fereastra";
        }

        // 5) Semn de ieșire doar dacă apare explicit SEMN / INDICATOR / EXIT.
        // Nu transforma simplul „ușa de ieșire” în semn.
        if ((p.Contains(" semn ") || p.Contains(" semnul ") ||
             p.Contains(" indicator ") || p.Contains(" indicatorul ")) &&
            (p.Contains(" iesire ") || p.Contains(" iesirea ") || p.Contains(" exit ")))
        {
            return "semn iesire";
        }

        if (p.Contains(" semn exit ") ||
            p.Contains(" semnul exit ") ||
            p.Contains(" indicator exit "))
        {
            return "semn iesire";
        }

        // 6) Obiecte simple
        if (norm == "scaunul" || p.Contains(" scaun ") || p.Contains(" scaunul "))
            return "scaun";

        if (norm == "patul" || p.Contains(" pat ") || p.Contains(" patul "))
            return "pat";

        if (norm == "dulapul" || p.Contains(" dulap ") || p.Contains(" dulapul "))
            return "dulap";

        if (norm == "masa" || p.Contains(" masa "))
            return "masa";

        if (norm == "birou" || norm == "biroul" || p.Contains(" birou ") || p.Contains(" biroul "))
            return "masa";

        if (norm == "canapeaua" || p.Contains(" canapea ") || p.Contains(" canapeaua "))
            return "canapea";

        if (norm == "telefonul" || p.Contains(" telefon ") || p.Contains(" mobil "))
            return "telefon";

        if (p.Contains(" laptop ") || p.Contains(" calculator "))
            return "laptop";

        if (p.Contains(" televizor ") || p.Contains(" tv "))
            return "televizor";

        if (p.Contains(" sticla "))
            return "sticla";

        if (p.Contains(" cana "))
            return "cana";
        // Ochelari
        if (norm == "ochelari" ||
            norm == "ochelarii" ||
            norm == "ochelari vedere" ||
            norm == "ochelari de vedere" ||
            norm == "ochelari soare" ||
            norm == "ochelari de soare" ||
            p.Contains(" ochelari ") ||
            p.Contains(" ochelarii "))
        {
            return "ochelari";
        }

        return null;
    }
    private bool HasProductHint(string cmdNorm, string raw)
    {
        if (HasTokenLike(cmdNorm, "produs") || HasTokenLike(cmdNorm, "product") || HasTokenLike(cmdNorm, "produ") || HasTokenLike(cmdNorm, "prod"))
            return true;

        string r = Normalize(raw);
        return HasTokenLike(r, "produs") || HasTokenLike(r, "product") || HasTokenLike(r, "produ") || HasTokenLike(r, "prod");
    }

    private bool HasCodeHint(string cmdNorm, string raw)
    {
        if (HasTokenLike(cmdNorm, "cod") || HasTokenLike(cmdNorm, "barcode") || HasToken(cmdNorm, "qr"))
            return true;

        string r = Normalize(raw);
        return HasTokenLike(r, "cod") || HasTokenLike(r, "barcode") || HasToken(r, "qr");
    }
    /// <summary>
    /// STOP din gest (palmă): oprește feature-ul curent IMEDIAT și revine în FeaturesMenu.
    /// Nu afectează workflow-ul vocal existent.
    /// </summary>
    public void StopAllFromGesture(string confirmation = "Am oprit.", bool speakConfirmation = true)
    {
        // 0) Oprește modul de lumină (nu afectează nimic altceva)
        lightBill?.StopMode(speak: false);
        bulbCheck?.Stop(speak: false);
        walkingSafety?.StopSafety(speak: false);

        // 1) UI instant
        if (speechUI != null)
            speechUI.ShowSuccess(confirmation);

        // 2) oprește two-step find
        waitingFindTarget = false;

        // 3) anulează corutinele întârziate
        _stopGeneration++;

        // 4) barge-in
        azureTTS?.StopNow();

        // 5) oprește ce rulează
        if (productScanner != null)
        {
            if (productScanner.IsReadingProductInfo)
                productScanner.StopProductInfo();

            productScanner.StopScan(silent: true);
        }

        ocrSystem?.StopReading();
        objectFinder?.StopFind(silent: true);
        scanHandler?.StopSafetyActive();
        sceneAI?.Cancel();
        ActiveFeatureName = "";
        SetActiveFeatureUI("");
        visualUI?.SetLastCommand("STOP");
        // 6) UI menu
        menuUI?.ShowFeaturesMenu();
        ScheduleListeningIfVocal(2f);

        // 7) Voce (opțional)
        if (speakConfirmation)
            azureTTS?.Speak(confirmation);

    }
    //SOS
    private bool IsSosManualHelp(string c, string raw)
    {
        string r = Normalize((raw ?? "") + " " + (c ?? ""));

        if (string.IsNullOrWhiteSpace(r))
            return false;

        // Nu transforma expresiile de tip "nu am nevoie de ajutor" în alertă.
        if (r.Contains("nu am nevoie de ajutor")) return false;
        if (r.Contains("n am nevoie de ajutor")) return false;
        if (r.Contains("nu trebuie ajutor")) return false;
        if (r.Contains("fara ajutor")) return false;
        if (r.Contains("fără ajutor")) return false;

        if (r == "ajutor") return true;
        if (r.Contains("ajutor")) return true;
        if (r.Contains("am nevoie de ajutor")) return true;
        if (r.Contains("ajuta ma")) return true;
        if (r.Contains("ajutati ma")) return true;
        if (r.Contains("cheama ajutor")) return true;
        if (r.Contains("cheama pe cineva")) return true;
        if (r.Contains("ambulanta")) return true;
        if (r.Contains("urgenta")) return true;
        if (r.Contains("112")) return true;
        if (r.Contains("suna la")) return true;
        if (r.Contains("nu sunt bine")) return true;
        if (r.Contains("nu ma simt bine")) return true;
        if (r.Contains("nu pot sa ma ridic")) return true;
        if (r.Contains("m am lovit")) return true;
        if (r.Contains("ma doare")) return true;
        if (r.Contains("mi e rau")) return true;
        if (r.Contains("imi este rau")) return true;

        return false;
    }
    //FACTURA LA CURENT
    private bool IsLightBill(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        // cmd e normalizat fara diacritice
        if (c.Contains("factura curent")) return true;
        if (c.Contains("mod factura")) return true;
        if (c.Contains("verifica lumina")) return true;

        if (c.Contains("consum curent")) return true;

        // si din raw (cu diacritice uneori)
        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("factură") && r.Contains("curent")) return true;
        if (r.Contains("verifică") && r.Contains("lumin")) return true;

        if (c.Contains("este lumina")) return true;
        if (c.Contains("e lumina")) return true;
        if (c.Contains("este intuneric")) return true;
        if (c.Contains("e intuneric")) return true;
        if (c.Contains("verifica luminozitatea")) return true;

        return false;
    }
    private bool IsBulbCheck(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        if (c.Contains("bec aprins")) return true;
        if (c.Contains("bec stins")) return true;
        if (c.Contains("becul aprins")) return true;
        if (c.Contains("becul stins")) return true;

        bool hasBec = HasToken(c, "bec") || HasToken(c, "becul");
        bool hasVerb = c.Contains("verifica") || c.Contains("control") || c.Contains("testeaza");

        if (hasBec && hasVerb) return true;

        string r = (raw ?? "").ToLowerInvariant();
        bool rawHasBec = r.Contains("bec");
        bool rawHasVerb = r.Contains("verific") || r.Contains("control") || r.Contains("teste");

        return rawHasBec && rawHasVerb;
    }

    private IEnumerator BulbCheckAfterSettle()
    {
        int gen = _stopGeneration;
        yield return new WaitForSeconds(cameraSettleDelay);
        if (gen != _stopGeneration) yield break;
        bulbCheck?.CheckBulbOnce();
    }
    private void SetActiveFeatureUI(string msg)
    {
        if (activeFeatureText != null)
            activeFeatureText.text = msg;
    }
    private IEnumerator PlayModeSelectionAfterIntro()
    {
        yield return new WaitForSeconds(modeSelectionDelay);

        menuUI?.ShowMainMenu();
        visualUI?.SetLastCommand("ALEGERE MOD");
        visualUI?.SetMode("—");
        modeSelectionVisual?.SetNoSelection();

        Feedback(modeSelectionMessage, success: true);
    }
    public void ActivateVocalMode(bool speakConfirmation = true)
    {
        StartModeSelectionTransition(true, speakConfirmation);
    }

    public void ActivateDiscreetMode(bool speakConfirmation = true)
    {
        StartModeSelectionTransition(false, speakConfirmation);
    }

    private bool IsModeVocalCommand(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        if (c.Contains("mod vocal")) return true;

        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("mod vocal")) return true;

        return false;
    }

    private bool IsModeDiscreetCommand(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        if (c.Contains("mod discret")) return true;

        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("mod discret")) return true;

        return false;
    }

    private bool IsCriticalVoiceCommandAllowedInDiscreetMode(string cmd, string raw)
    {
        if (!allowCriticalVoiceCommandsInDiscreetMode)
            return false;

        if (IsGlobalStop(cmd))
            return true;

        if (IsSosManualHelp(cmd, raw))
            return true;

        // Dacă sistemul SOS a întrebat "Ești bine?",
        // DA / SUNT BINE / NU / AJUTOR trebuie acceptate și în modul discret.
        if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
            return true;

        return false;
    }
    private void SpeakFeaturesMessageDelayed()
    {
        if (_speakFeaturesRoutine != null)
            StopCoroutine(_speakFeaturesRoutine);

        _speakFeaturesRoutine = StartCoroutine(SpeakFeaturesMessageDelayedRoutine());
    }

    private IEnumerator SpeakFeaturesMessageDelayedRoutine()
    {
        yield return new WaitForSeconds(featuresAnnouncementDelay);
        Feedback(GetFeaturesMessageForCurrentMode(), success: true);
        _speakFeaturesRoutine = null;
    }
    public string GetModeStatus()
    {
        switch (CurrentMode)
        {
            case InteractionMode.Vocal:
                return "VOCAL";
            case InteractionMode.Discreet:
                return "DISCREET";
            default:
                return "NOT_SELECTED";
        }
    }
    private string BuildStartupIntroMessage()
    {
        if (!includeUserNameInStartupGreeting)
            return startupIntroMessage;

        string savedName = GetSavedUserName();
        if (string.IsNullOrWhiteSpace(savedName))
            return startupIntroMessage;

        return $"{startupIntroMessage} {string.Format(returningUserGreetingTemplate, savedName)}";
    }

    private string GetSavedUserName()
    {
        string value = PlayerPrefs.GetString(PrefUserName, "").Trim();
        return string.IsNullOrWhiteSpace(value) ? "" : value;
    }

    private bool HasSavedUserName()
    {
        return !string.IsNullOrWhiteSpace(GetSavedUserName());
    }

    private void SaveUserName(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName)) return;

        PlayerPrefs.SetString(PrefUserName, userName.Trim());
        PlayerPrefs.Save();
    }

    public void ClearSavedUserName()
    {
        PlayerPrefs.DeleteKey(PrefUserName);
        PlayerPrefs.Save();
    }

    private bool ShouldAskUserNameNow()
    {
        if (!askUserNameOnce) return false;
        if (!askUserNameAfterModeSelection) return false;
        if (HasSavedUserName()) return false;
        return true;
    }

    private bool IsSkipUserNameCommand(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return false;

        if (c.Contains("sari")) return true;
        if (c.Contains("fara nume")) return true;
        if (c.Contains("continua")) return true;
        if (c.Contains("nu vreau nume")) return true;

        string r = (raw ?? "").ToLowerInvariant();
        if (r.Contains("fără nume")) return true;
        if (r.Contains("nu vreau nume")) return true;

        return false;
    }

    private bool IsLikelyCommandInsteadOfName(string c, string raw)
    {
        if (string.IsNullOrWhiteSpace(c)) return true;

        if (IsModeVocalCommand(c, raw)) return true;
        if (IsModeDiscreetCommand(c, raw)) return true;
        if (IsStart(c, raw)) return true;
        if (IsBack(c)) return true;
        if (IsExit(c)) return true;
        if (IsDescribe(c)) return true;
        if (IsOCR(c)) return true;
        if (IsSafetyOn(c)) return true;
        if (IsSafetyOff(c)) return true;
        if (IsGlobalStop(c)) return true;
        if (IsProductScanStart(c, raw)) return true;
        if (IsProductScanStop(c, raw)) return true;
        if (IsLightBill(c, raw)) return true;
        if (IsBulbCheck(c, raw)) return true;
        if (IsSosManualHelp(c, raw)) return true;

        if (HasFindPrefix(c, raw)) return true;
        if (IsFind(c, raw, out _)) return true;

        return false;
    }

    private string ExtractCandidateUserName(string normalizedOrRaw, string rawFromEngine)
    {
        string raw = string.IsNullOrWhiteSpace(rawFromEngine) ? normalizedOrRaw : rawFromEngine;
        string s = Normalize(raw);

        if (string.IsNullOrWhiteSpace(s))
            return null;

        string[] exactReject =
        {
        "da",
        "nu",
        "sari",
        "fara nume",
        "continua",
        "start",
        "stop",
        "mod vocal",
        "mod discret",
        "descriere",
        "text",
        "citire",
        "ajutor"
    };

        foreach (var bad in exactReject)
        {
            if (s == bad)
                return null;
        }

        string[] prefixes =
        {
        "numele meu este",
        "ma cheama",
        "eu sunt",
        "sunt",
        "ma numesc",
        "numele este"
    };

        foreach (var p in prefixes)
        {
            if (s.StartsWith(p + " "))
            {
                s = s.Substring(p.Length).Trim();
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(s))
            return null;

        var toks = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t =>
                        t != "te" &&
                        t != "rog" &&
                        t != "bine" &&
                        t != "salut" &&
                        t != "ok" &&
                        t != "numele" &&
                        t != "meu" &&
                        t != "este" &&
                        t != "ma" &&
                        t != "numesc" &&
                        t != "cheama" &&
                        t != "eu" &&
                        t != "sunt")
                    .ToArray();

        if (toks.Length == 0)
            return null;

        // dacă Azure a întors o propoziție lungă,
        // luăm ultimele 1-2 token-uri, fiindcă de obicei acolo e numele
        if (toks.Length >= 3)
        {
            toks = toks.Skip(Mathf.Max(0, toks.Length - 2)).ToArray();
        }

        s = string.Join(" ", toks).Trim();

        if (string.IsNullOrWhiteSpace(s))
            return null;

        if (s.Length < 2)
            return null;

        if (s.Length > 24)
            s = s.Substring(0, 24).Trim();

        // respingem doar comenzi foarte clare
        if (s == "mod vocal" || s == "mod discret" || s == "start" || s == "stop")
            return null;

        try
        {
            var ti = new CultureInfo("ro-RO").TextInfo;
            s = ti.ToTitleCase(s.ToLowerInvariant());
        }
        catch
        {
            s = char.ToUpper(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        return s;
    }

    private bool TryHandleUserNameCapture(string cmd, string raw)
    {
        if (!waitingForUserName && !waitingForUserNameConfirmation)
            return false;

        if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
            return true;

        if (IsSosManualHelp(cmd, raw))
        {
            sos?.TriggerImmediateHelp("voice_help");
            return true;
        }

        if (IsGlobalStop(cmd))
        {
            StopAllFromGesture("Am oprit.", speakConfirmation: true);
            return true;
        }

        // PAS 2: confirmare nume
        if (waitingForUserNameConfirmation)
        {
            if (cmd == "da")
            {
                azureNameCapture?.CancelCapture();
                uwpSpeech?.SetNameCaptureMode(false);
                SaveUserName(pendingDetectedUserName);

                waitingForUserName = false;
                waitingForUserNameConfirmation = false;
                uwpSpeech?.SetNameCaptureMode(false);

                visualUI?.SetDetectedName(pendingDetectedUserName);
                visualUI?.SetLastCommand("NUME CONFIRMAT");

                Feedback(userNameConfirmedMessage, success: true);

                StartCoroutine(PlayModeSelectionAfterNameSaved());
                return true;
            }
            if (cmd == "nu")
            {
                pendingDetectedUserName = "";
                waitingForUserName = true;
                waitingForUserNameConfirmation = false;

                visualUI?.SetDetectedName("—");
                visualUI?.SetLastCommand("NUME RESPINS");

                Feedback(userNameRejectedMessage, success: true);

                if (useAzureSpeechForUserName)
                    azureNameCapture?.BeginUserNameCapture();

                return true;
            }

            Feedback("Spune DA dacă numele este corect sau NU pentru a repeta.", error: true);
            return true;
        }

        // PAS 1: captură nume
        string userName = ExtractCandidateUserName(cmd, raw);
        if (string.IsNullOrWhiteSpace(userName))
        {
            visualUI?.SetLastCommand("NUME NERECUNOSCUT");
            Feedback(retryUserNameMessage, error: true);
            return true;
        }

        pendingDetectedUserName = userName;
        waitingForUserName = false;
        waitingForUserNameConfirmation = true;

        visualUI?.SetDetectedName(userName);
        visualUI?.SetLastCommand("NUME DETECTAT");

        string confirmMsg = string.Format(confirmUserNameTemplate, userName);
        Feedback(confirmMsg, success: true);
        return true;
    }
    public void SubmitUserNameFromPhone(string typedName)
    {
        string cleaned = ExtractCandidateUserName(typedName, typedName);

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            Feedback("Numele primit de pe telefon nu este valid.", error: true);
            return;
        }

        azureNameCapture?.CancelCapture();
        uwpSpeech?.SetNameCaptureMode(false);

        waitingForUserName = false;
        waitingForUserNameConfirmation = false;
        pendingDetectedUserName = "";

        SaveUserName(cleaned);

        menuUI?.ShowOnboardingMenu();
        visualUI?.ShowOnboardingWithName(cleaned);
        visualUI?.SetMode("—");
        visualUI?.SetLastCommand("NUME PRIMIT DE PE TELEFON");

        StartCoroutine(ShowOnboardingThenMainMenu(cleaned));
    }
    private IEnumerator PlayModeSelectionAfterNameSaved()
    {
        yield return new WaitForSeconds(1.2f);

        menuUI?.ShowMainMenu();
        visualUI?.SetLastCommand("ALEGERE MOD");
        visualUI?.SetMode("—");
        modeSelectionVisual?.SetNoSelection();

        Feedback(modeSelectionMessage, success: true);
    }
    public bool IsWaitingForUserNameCapture()
    {
        return waitingForUserName;
    }
    private void ProcessCommandInternal(string normalizedOrRaw, string rawFromEngine, bool fromPhone)
    {
        if (string.IsNullOrWhiteSpace(normalizedOrRaw))
            return;

        string raw = rawFromEngine ?? normalizedOrRaw;

        string cmd = Normalize(normalizedOrRaw);
        cmd = CanonicalizeCommand(cmd, raw);
        // =======================================================
        // SOS PRIORITAR GLOBAL
        // Funcționează în orice mod: Vocal, Discret, NotSelected.
        // 1) Dacă SOS a întrebat "Ești bine?", răspunsurile sunt tratate primele.
        // 2) Dacă utilizatorul spune AJUTOR fără întrebare activă, trimitem alertă direct.
        // =======================================================
        if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
            return;

        if (sos != null && IsSosManualHelp(cmd, raw))
        {
            sos.TriggerImmediateHelp("voice_help");
            return;
        }

        // IMPORTANT SOS:
        // Dacă sistemul SOS a întrebat "Ești bine?",
        // răspunsurile DA / SUNT BINE / NU / AJUTOR trebuie tratate PRIMELE,
        // înainte de nume, meniu, mod vocal/discret sau alte comenzi.
        if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
            return;

        if (fromPhone && TryHandlePhoneNamePayload(raw))
            return;
        if (ShouldIgnoreUwpDuringAzureNameCapture(fromPhone))
        {
            // lăsăm doar comenzile critice
            if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
                return;

            if (IsSosManualHelp(cmd, raw))
            {
                sos?.TriggerImmediateHelp("voice_help");
                return;
            }

            if (IsGlobalStop(cmd))
            {
                StopAllFromGesture("Am oprit.", speakConfirmation: true);
                return;
            }

            // IGNORĂM restul rezultatelor UWP în timpul capturii Azure
            return;
        }
        if (waitingForUserName)
        {
            if (TryHandleUserNameCapture(cmd, raw))
                return;
        }

        // ===== MODE SELECTION =====
        if (IsModeVocalCommand(cmd, raw))
        {
            ActivateVocalMode(speakConfirmation: true);
            return;
        }

        if (IsModeDiscreetCommand(cmd, raw))
        {
            ActivateDiscreetMode(speakConfirmation: true);
            return;
        }

        // Dacă nu a fost ales încă niciun mod, permitem doar alegerea modului și comenzile critice.
        if (CurrentMode == InteractionMode.NotSelected)
        {
            if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
                return;

            if (sos != null && IsSosManualHelp(cmd, raw))
            {
                sos.TriggerImmediateHelp("voice_help");
                return;
            }

            if (IsGlobalStop(cmd))
            {
                Feedback("Am oprit.", success: true);
                menuUI?.ShowFeaturesMenu();
                ScheduleListeningIfVocal(2f);
                return;
            
        }

            Feedback("Alege mai întâi un mod de utilizare. Spune MOD VOCAL sau folosește telefonul pentru MOD DISCRET.", error: true);
            return;
        }

        // IMPORTANT:
        // În modul discret, blocăm doar comenzile VOCALE de pe HoloLens,
        // NU și comenzile venite de pe telefon.
        if (!fromPhone && CurrentMode == InteractionMode.Discreet)
        {
            if (!IsCriticalVoiceCommandAllowedInDiscreetMode(cmd, raw))
                return;
        }

        if (sos != null && sos.TryHandleVoiceResponse(cmd, raw))
            return;

        if (sos != null && IsSosManualHelp(cmd, raw))
        {
            sos.TriggerImmediateHelp("voice_help");
            return;
        }

        if (Time.time - lastCommandTime < commandCooldown && cmd == lastCmdNorm)
            return;

        lastCommandTime = Time.time;
        lastCmdNorm = cmd;

        azureTTS?.StopNow();

        Debug.Log("[Router] Heard(norm): " + cmd + " | raw: " + raw + " | fromPhone=" + fromPhone);

        if (speechUI != null)
            speechUI.ShowUI("Am auzit: " + raw);

        if (menuUI == null)
        {
            Feedback("Eroare: MenuFlowController nu este legat.", error: true);
            return;
        }

        if (menuUI.CurrentState == MenuFlowController.State.MainMenu)
        {
            if (IsStart(cmd, raw))
            {
                menuUI.ShowFeaturesMenu();
                SetActiveFeatureUI("Alege o funcție...");
                Feedback(GetFeaturesMessageForCurrentMode(), success: true);
                ScheduleListeningIfVocal(18f);
                return;
            }

            if (IsExit(cmd))
            {
                Feedback(goodbyeMessage, success: true);
                Invoke(nameof(QuitApp), 1.5f);
                return;
            }

            Feedback("Nu am înțeles comanda. Alege modul vocal sau modul discret pentru a continua.", error: true);
            return;
        }

        if (menuUI.CurrentState == MenuFlowController.State.FeaturesMenu)
        {
            if (productScanner != null && productScanner.IsRunning && IsProductScanStop(cmd, raw))
            {
                productScanner.StopScan(silent: false);
                return;
            }
        }

        if (IsGlobalStop(cmd))
        {
            _stopGeneration++;

            lightBill?.StopMode(speak: false);
            bulbCheck?.Stop(speak: false);
            walkingSafety?.StopSafety(speak: false);

            if (productScanner != null && productScanner.IsReadingProductInfo)
            {
                productScanner.StopProductInfo();
                menuUI?.ShowFeaturesMenu(); // ← ADAUGĂ
                return;
            }

            productScanner?.StopScan(silent: true);

            ocrSystem?.StopReading();
            objectFinder?.StopFind(silent: false);
            scanHandler?.StopSafetyActive();
            sceneAI?.Cancel();

            waitingFindTarget = false;
            ActiveFeatureName = "";
            SetActiveFeatureUI("");
            visualUI?.SetLastCommand("STOP");

            Feedback("Am oprit.", success: true);
            menuUI?.ShowFeaturesMenu(); // ← ADAUGĂ

            return;
        }

        // Acceptăm comenzile de funcții și dacă UI-ul a rămas accidental în Processing/Hidden.
        // Asta repară cazul în care apeși DESCRIERE și nu se mai întâmplă nimic.
        if (menuUI.CurrentState == MenuFlowController.State.FeaturesMenu ||
            menuUI.CurrentState == MenuFlowController.State.Processing ||
            menuUI.CurrentState == MenuFlowController.State.Hidden)
        {
            HandleFeaturesMenu(cmd, raw);
            return;
        }

        // Fallback sigur: dacă modul este ales și nu suntem în MainMenu,
        // nu ignorăm comanda. O tratăm ca o comandă de funcție.
        HandleFeaturesMenu(cmd, raw);
        return;
    }
    public void PrepareForPhoneFind()
    {
        // oprește orice voce în desfășurare
        azureTTS?.StopNow();

        // oprește eventualul anunț întârziat
        if (_speakFeaturesRoutine != null)
        {
            StopCoroutine(_speakFeaturesRoutine);
            _speakFeaturesRoutine = null;
        }

        // anulează corutinele întârziate (descriere/text după settle delay)
        _stopGeneration++;

        // oprește stările intermediare
        waitingFindTarget = false;

        // oprește toate feature-urile care pot rula
        lightBill?.StopMode(speak: false);
        bulbCheck?.Stop(speak: false);
        walkingSafety?.StopSafety(speak: false);

        if (productScanner != null)
        {
            if (productScanner.IsReadingProductInfo)
                productScanner.StopProductInfo();

            productScanner.StopScan(silent: true);
        }

        ocrSystem?.StopReading();
        objectFinder?.StopFind(silent: true);
        scanHandler?.StopSafetyActive();
        sceneAI?.Cancel();

        ActiveFeatureName = "";
        SetActiveFeatureUI("");
        visualUI?.SetLastCommand("PREGĂTIRE CĂUTARE TELEFON");

        // doar feedback vizual, fără voce
        speechUI?.ShowUI("Spune obiectul pe telefon...");
    }
    public void OnAzureUserNameRecognized(string rawRecognizedText)
    {
        if (!waitingForUserName || waitingForUserNameConfirmation)
            return;

        Debug.Log("[Router][AzureName] rawRecognizedText = " + rawRecognizedText);

        string userName = ExtractCandidateUserName(rawRecognizedText, rawRecognizedText);

        Debug.Log("[Router][AzureName] parsedUserName = " + (userName ?? "<null>"));

        if (string.IsNullOrWhiteSpace(userName))
        {
            visualUI?.SetLastCommand("NUME NERECUNOSCUT");
            Feedback("Nu am înțeles numele. Spune doar numele, de exemplu Daniel.", error: true);
            StartCoroutine(RestartAzureNameCaptureAfterDelay());
            return;
        }

        azureNameCapture?.CancelCapture();

        pendingDetectedUserName = userName;
        waitingForUserName = false;
        waitingForUserNameConfirmation = true;

        visualUI?.SetDetectedName(userName);
        visualUI?.SetLastCommand("NUME DETECTAT AZURE");

        string confirmMsg = string.Format(confirmUserNameTemplate, userName);
        Feedback(confirmMsg, success: true);
    }

    public void OnAzureUserNameCaptureFailed()
    {
        if (!waitingForUserName || waitingForUserNameConfirmation)
            return;

        visualUI?.SetLastCommand("CAPTURĂ NUME EȘUATĂ");
        Feedback("Nu am înțeles numele. Spune-l din nou după semnal.", error: true);

        StartCoroutine(RestartAzureNameCaptureAfterDelay());
    }
    private bool ShouldIgnoreUwpDuringAzureNameCapture(bool fromPhone)
    {
        if (fromPhone) return false;
        if (!useAzureSpeechForUserName) return false;
        if (!waitingForUserName) return false;
        if (waitingForUserNameConfirmation) return false;

        // cât timp suntem în pasul 1 de captură nume,
        // ignorăm complet rezultatele UWP
        return true;
    }
    private IEnumerator RestartAzureNameCaptureAfterDelay()
    {
        yield return new WaitForSeconds(1.0f);

        if (useAzureSpeechForUserName && waitingForUserName && !waitingForUserNameConfirmation)
            azureNameCapture?.BeginUserNameCapture();
    }
    private bool TryHandlePhoneNamePayload(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        const string prefix = "PHONE_NAME:";

        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string name = raw.Substring(prefix.Length).Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            Feedback("Nu am primit numele de pe telefon.", error: true);
            return true;
        }

        SubmitUserNameFromPhone(name);
        return true;
    }
    public bool HasSavedUserNameForPhone()
    {
        return !string.IsNullOrWhiteSpace(GetSavedUserName());
    }
    public string GetSavedUserNameForPhone()
    {
        return GetSavedUserName();
    }
    public void PrepareForPhoneNameCapture()
    {
        if (HasSavedUserName())
        {
            waitingForUserName = false;
            waitingForUserNameConfirmation = false;
            pendingDetectedUserName = "";

            uwpSpeech?.SetNameCaptureMode(false);
            azureNameCapture?.CancelCapture();

            azureTTS?.StopNow();

            string savedName = GetSavedUserName();
            menuUI?.ShowOnboardingMenu();
            visualUI?.ShowOnboardingWithName(savedName);
            visualUI?.SetDetectedName("Nume: " + savedName);
            visualUI?.SetMode("—");
            visualUI?.SetLastCommand("NUME DEJA SALVAT");
            StartCoroutine(ShowOnboardingThenMainMenu(savedName));
            return;
        }

        azureTTS?.StopNow();

        if (_speakFeaturesRoutine != null)
        {
            StopCoroutine(_speakFeaturesRoutine);
            _speakFeaturesRoutine = null;
        }

        _stopGeneration++;

        lightBill?.StopMode(speak: false);
        bulbCheck?.Stop(speak: false);
        walkingSafety?.StopSafety(speak: false);

        if (productScanner != null)
        {
            if (productScanner.IsReadingProductInfo)
                productScanner.StopProductInfo();

            productScanner.StopScan(silent: true);
        }

        ocrSystem?.StopReading();
        objectFinder?.StopFind(silent: true);
        scanHandler?.StopSafetyActive();
        sceneAI?.Cancel();

        ActiveFeatureName = "";
        SetActiveFeatureUI("");
        visualUI?.SetLastCommand("CAPTURĂ NUME TELEFON");

        waitingForUserName = true;
        waitingForUserNameConfirmation = false;
        pendingDetectedUserName = "";

        uwpSpeech?.SetNameCaptureMode(false);
        azureNameCapture?.CancelCapture();

        menuUI?.ShowOnboardingMenu();
        visualUI?.ShowWelcome("Introdu numele pe telefon pentru a continua.");
        visualUI?.SetDetectedName("Nume: —");
        visualUI?.SetMode("—");
        visualUI?.SetLastCommand("Aștept numele de pe telefon");
    }
    private void StartModeSelectionTransition(bool isVocal, bool speakConfirmation)
    {
        if (_modeSelectionRoutine != null)
        {
            StopCoroutine(_modeSelectionRoutine);
            _modeSelectionRoutine = null;
        }

        _modeSelectionRoutine = StartCoroutine(ModeSelectionTransitionRoutine(isVocal, speakConfirmation));
    }

    private IEnumerator ModeSelectionTransitionRoutine(
    bool isVocal,
    bool speakConfirmation)
    {
       
        menuUI?.ShowMainMenu();

      
        azureTTS?.StopNow();

        
        if (isVocal)
        {
            CurrentMode = InteractionMode.Vocal;
            modeSelectionVisual?.SetVocalSelected();
            visualUI?.SetMode("VOCAL");
        }
        else
        {
            CurrentMode = InteractionMode.Discreet;
            modeSelectionVisual?.SetDiscreetSelected();
            visualUI?.SetMode("DISCRET");
        }

    
        string activationText = isVocal
            ? vocalModeActivatedMessage
            : discreetModeActivatedMessage;

      
        if (speakConfirmation &&
            !string.IsNullOrWhiteSpace(activationText))
        {
            azureTTS?.Speak(activationText);
        }


        yield return new WaitForSeconds(
            Mathf.Clamp(modeSelectionConfirmDelay, 0.05f, 0.35f)
        );


        menuUI?.ShowFeaturesMenu();
        visualUI?.SetLastCommand("MENIU OPȚIUNI");
        SetActiveFeatureUI("");
        menuUI?.HideListeningIndicator();

       
        yield return null;

        string featuresText = GetFeaturesMessageForCurrentMode();

       
        speechUI?.ShowSuccess(featuresText);

        
        if (!string.IsNullOrWhiteSpace(featuresText))
        {
            azureTTS?.Speak(featuresText);
        }

      
        if (isVocal)
            ScheduleListeningIfVocal(24f);
        else
            ScheduleListeningIfVocal(2f);

        _modeSelectionRoutine = null;
    }

    private IEnumerator ShowOnboardingThenMainMenu(string userName)
    {
        menuUI?.ShowOnboardingMenu();
        visualUI?.ShowOnboardingWithName(userName);
        visualUI?.SetMode("—");
        visualUI?.SetLastCommand("—");

        azureTTS?.StopNow();
        azureTTS?.Speak($"Bine ai venit, {userName}. HoloVision este pregătit să te ajute.");

        yield return new WaitForSeconds(onboardingToMainMenuDelay);

        menuUI?.ShowMainMenu();
        visualUI?.SetMode("—");
        visualUI?.SetLastCommand("ALEGERE MOD");
        modeSelectionVisual?.SetNoSelection();

        yield return new WaitForSeconds(0.3f);

        azureTTS?.Speak(modeSelectionMessage);
    }
    private string GetStartupWelcomeOnly()
    {
        string savedName = GetSavedUserName();

        if (!string.IsNullOrWhiteSpace(savedName))
            return string.Format(returningUserGreetingTemplate, savedName);

        return "Bine ai venit!";
    }
    public void ReturnToFeaturesMenu()
    {
        ActiveFeatureName = "";
        SetActiveFeatureUI("");

        if (menuUI != null)
        {
            menuUI.ShowFeaturesMenu();
        }
        else
        {
            Debug.LogError("[VoiceCommandRouter] menuUI este NULL. Nu pot reveni la meniul funcțiilor.");
        }

        ScheduleListeningIfVocal(3f);
    }
    public void ShowDescriptionResult(string finalText)
    {
        menuUI?.ShowDescriptionResult(finalText);
        azureTTS?.Speak(finalText);

        ActiveFeatureName = "";
        SetActiveFeatureUI("");

        ScheduleListeningIfVocal(6f);
    }
    public void ShowTextResult(string finalText)
    {
        menuUI?.ShowTextResult(finalText);

        ActiveFeatureName = "";
        SetActiveFeatureUI("");

        ScheduleListeningIfVocal(6f);
    }
    private string GetFeaturesMessageForCurrentMode()
    {
        if (CurrentMode == InteractionMode.Vocal &&
            !string.IsNullOrWhiteSpace(vocalFeaturesMessage))
        {
            return vocalFeaturesMessage;
        }

        return featuresMessage;
    }
    private void FeedbackUnknownCommand()
    {
        if (Time.time - lastUnknownCommandSpeakTime < unknownCommandSpeakCooldown)
            return;

        lastUnknownCommandSpeakTime = Time.time;

        string msg;

        if (CurrentMode == InteractionMode.Vocal)
        {
            msg = unknownVocalCommandMessage;
        }
        else
        {
            msg =
                "Nu am înțeles comanda. Poți folosi opțiunile de pe telefon: " +
                "Descriere, Text, Caută, Scanează produs sau Factura curent.";
        }

        Feedback(msg, error: true);
    }




}