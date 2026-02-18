using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
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
    [TextArea] public string welcomeMessage = "Bine ai venit! Spune START sau EXIT.";
    [TextArea] public string goodbyeMessage = "Sistemul se închide. Pe curând!";
    [TextArea]
    public string featuresMessage =
        "Ai intrat în meniul funcțiilor. Spune DESCRIERE, TEXT sau CITIRE, SCANEAZĂ PRODUSUL, SIGURANȚĂ, ori: CAUTĂ <obiect>. Spune ÎNAPOI.";

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
    }

    private void Start()
    {
        menuUI?.ShowMainMenu();
        Feedback(welcomeMessage, success: true);
    }

    public void AcceptCommand(string rawCommand) => AcceptCommand(rawCommand, rawCommand);

    public void AcceptCommand(string normalizedOrRaw, string rawFromEngine)
    {
        if (string.IsNullOrWhiteSpace(normalizedOrRaw)) return;

        string raw = rawFromEngine ?? normalizedOrRaw;

        // Normalizare + canonicalizare (fix DESCRIERE recunoscut ca „cauta disk/discrete…”)
        string cmd = Normalize(normalizedOrRaw);
        cmd = CanonicalizeCommand(cmd, raw);

        // anti-spam doar la repetare identică rapid
        if (Time.time - lastCommandTime < commandCooldown && cmd == lastCmdNorm) return;
        lastCommandTime = Time.time;
        lastCmdNorm = cmd;

        // barge-in
        azureTTS?.StopNow();

        Debug.Log("[Router] Heard(norm): " + cmd + " | raw: " + raw);
        speechUI?.ShowUI("Am auzit: " + raw);

        if (menuUI == null)
        {
            Feedback("Eroare: MenuFlowController nu este legat.", error: true);
            return;
        }

        // ===== MAIN MENU =====
        if (menuUI.CurrentState == MenuFlowController.State.MainMenu)
        {
            if (IsStart(cmd, raw))
            {
                menuUI.ShowFeaturesMenu();
                Feedback(featuresMessage, success: true);
                return;
            }

            if (IsExit(cmd))
            {
                Feedback(goodbyeMessage, success: true);
                Invoke(nameof(QuitApp), 1.5f);
                return;
            }

            // QoL: dacă user zice direct o funcție, intrăm în Features și executăm
            if (IsDirectFeatureCommand(cmd, raw))
            {
                menuUI.ShowFeaturesMenu();
                speechUI?.ShowSuccess("Meniu funcții.");
                HandleFeaturesMenu(cmd, raw);
                return;
            }

            Feedback("Nu am înțeles. Spune START sau EXIT.", error: true);
            return;
        }

        // ===== IMPORTANT: Product scan STOP înainte de Global STOP =====
        if (menuUI.CurrentState == MenuFlowController.State.FeaturesMenu)
        {
            if (productScanner != null && productScanner.IsRunning && IsProductScanStop(cmd, raw))
            {
                productScanner.StopScan(silent: false);
                return;
            }
        }

        // ===== GLOBAL STOP =====
        if (IsGlobalStop(cmd))
        {
            productScanner?.StopScan(silent: true);

            ocrSystem?.StopReading();
            objectFinder?.StopFind(silent: false);
            scanHandler?.StopSafetyActive();

            waitingFindTarget = false;

            Feedback("Am oprit.", success: true);
            return;
        }

        // ===== FEATURES MENU =====
        if (menuUI.CurrentState == MenuFlowController.State.FeaturesMenu)
        {
            HandleFeaturesMenu(cmd, raw);
            return;
        }
    }

    private void HandleFeaturesMenu(string cmd, string raw)
    {
        // 0) TWO-STEP FIND: dacă așteptăm obiectul
        if (enableTwoStepFind && waitingFindTarget)
        {
            if (Time.time > waitingFindUntil)
            {
                waitingFindTarget = false;
                speechUI?.ShowError("Nu am auzit obiectul. Spune: CAUTĂ <obiect>.");
                // continuăm să procesăm comanda curentă normal
            }
            else
            {
                // IMPORTANT: dacă user spune DESCRIERE/TEXT etc, NU consumăm ca target
                bool isOtherCommand =
                    IsBack(cmd) || IsDescribe(cmd) || SnapToDescribe(cmd, raw) || IsOCR(cmd) ||
                    IsSafetyOn(cmd) || IsSafetyOff(cmd) ||
                    IsProductScanStart(cmd, raw) || IsProductScanStop(cmd, raw) ||
                    IsExit(cmd) ||
                    (productScanner != null && productScanner.IsRunning);

                if (!isOtherCommand)
                {
                    waitingFindTarget = false;

                    string tail = CleanupFindTail(cmd);
                    tail = PruneFindTail(tail);

                    // NU forțăm mapping dacă e random; CanonicalizeFindTarget doar ajută la obiecte uzuale
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

                    ocrSystem?.StopReading();
                    scanHandler?.StopSafetyActive();

                    objectFinder.StartFind(tail);
                    return;
                }

                waitingFindTarget = false;
            }
        }

        // PRODUCT SCAN (prioritar)
        if (IsProductScanStart(cmd, raw))
        {
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();

            productScanner?.StartScan();
            speechUI?.ShowSuccess("Scanare produs pornită.");
            return;
        }

        if (IsProductScanStop(cmd, raw))
        {
            productScanner?.StopScan(silent: false);
            return;
        }

        if (productScanner != null && productScanner.IsRunning)
        {
            if (IsFoundStop(cmd, raw))
            {
                productScanner.StopScanCelebrate();
                return;
            }

            speechUI?.ShowError("Ești în scanare produs. Spune: OPREȘTE SCANAREA sau AM GĂSIT.");
            return;
        }

        if (IsBack(cmd))
        {
            menuUI.ShowMainMenu();
            Feedback(welcomeMessage, success: true);
            return;
        }

        if (IsSafetyOn(cmd))
        {
            scanHandler?.StartSafetyActive();
            speechUI?.ShowSuccess("Siguranță activă.");
            return;
        }

        if (IsSafetyOff(cmd))
        {
            scanHandler?.StopSafetyActive();
            speechUI?.ShowSuccess("Liniște.");
            return;
        }

        if (IsFoundStop(cmd, raw))
        {
            objectFinder?.StopFind(silent: true);
            Feedback("Mă bucur că ai găsit ce căutai. Dacă mai ai nevoie de ceva, sunt aici.", success: true);
            return;
        }

        if (IsStopFind(cmd))
        {
            objectFinder?.StopFind(silent: false);
            return;
        }

        // ✅ DESCRIERE (înainte de FIND, cu delay ca să nu prindă camera ocupată)
        if (IsDescribe(cmd) || SnapToDescribe(cmd, raw))
        {
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            StartCoroutine(DescribeAfterSettle());
            return;
        }

        // ✅ OCR/TEXT (NU îl schimbăm ca logică; doar delay mic)
        if (IsOCR(cmd))
        {
            objectFinder?.StopFind(silent: true);
            ocrSystem?.StopReading();
            StartCoroutine(OCRAfterSettle());
            return;
        }

        // TWO-STEP START: user zice doar “caută/căutare”
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
                Feedback("Ce obiect dorești să caut? Spune-mi doar numele obiectului.", success: true);
                return;
            }
        }

        // CAUTĂ <orice>
        if (IsFind(cmd, raw, out string targetRo))
        {
            if (objectFinder == null)
            {
                Feedback("Eroare: ObjectFinderSystem lipsește din scenă.", error: true);
                return;
            }

            ocrSystem?.StopReading();
            scanHandler?.StopSafetyActive();

            if (logFindParsing) Debug.Log("[Router][Find] targetRo = " + targetRo);

            objectFinder.StartFind(targetRo);
            return;
        }

        Feedback("Nu am înțeles. Spune DESCRIERE, TEXT/CITIRE, SCANEAZĂ PRODUSUL, SIGURANȚĂ, CAUTĂ (...), ori ÎNAPOI.", error: true);
    }

    private IEnumerator DescribeAfterSettle()
    {
        yield return new WaitForSeconds(cameraSettleDelay);
        sceneAI?.AnalyzeScene();
    }

    private IEnumerator OCRAfterSettle()
    {
        yield return new WaitForSeconds(cameraSettleDelay);
        ocrSystem?.ReadText();
    }

    private bool IsDirectFeatureCommand(string c, string raw)
    {
        // IMPORTANT: DESCRIERE/TEXT au prioritate (ca să nu pice în FIND)
        if (IsDescribe(c) || SnapToDescribe(c, raw)) return true;
        if (IsOCR(c)) return true;

        if (IsSafetyOn(c) || IsSafetyOff(c)) return true;
        if (IsProductScanStart(c, raw) || IsProductScanStop(c, raw)) return true;
        if (HasFindPrefix(c, raw)) return true;
        if (allowFindWithoutPrefixFallback && LooksLikeBareObjectRequest(c)) return true;
        return false;
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

        // combinație clasică: “caut … disk/discrete …” pentru DESCRIERE
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
            HasTokenLike(c, "produs") || HasTokenLike(c, "etiche") || HasTokenLike(c, "cod") || HasToken(c, "qr") || HasTokenLike(c, "barcode");

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
        while (s.Contains("  ")) s = s.Replace("  ", " ");
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
        if (HasToken(c, "start") || HasToken(c, "porneste") || HasToken(c, "incepe"))
            return true;

        if (!string.IsNullOrWhiteSpace(raw) && raw.Trim().Equals("start", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private bool IsExit(string c) =>
        HasToken(c, "exit") || HasToken(c, "iesire") || HasToken(c, "inchide") || HasToken(c, "parasire");

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

        return HasToken(c, "stop") || HasToken(c, "gata") || HasToken(c, "opreste") || HasToken(c, "termina") || HasToken(c, "termin");
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
    // FIND (tolerant)
    // =========================
    private static bool LooksLikeFindPrefixToken(string tok)
    {
        if (string.IsNullOrWhiteSpace(tok)) return false;

        if (tok.StartsWith("caut") || tok.StartsWith("gasest"))
            return true;

        // “vcaut”, “xcauta” etc
        if (tok.Length <= 7 && tok.Contains("caut"))
            return true;

        if (tok.Length <= 9 && tok.Contains("gasest"))
            return true;

        // “cautare”
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

        // ✅ blocăm FIND dacă e DESCRIERE/TEXT (inclusiv misrecognition)
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

        // BONUS mapping pentru obiecte uzuale; dacă nu se potrivește, rămâne random
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
            IsBack(cmdNorm) || IsGlobalStop(cmdNorm) || IsStopFind(cmdNorm) || IsExit(cmdNorm))
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

        // cazuri "lipite": vcautaX / cautaX / cautareX
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
            "obiect","obiectul","asta","acesta","aceasta",
            "si","iar","inca","mai",
            "the","a","an","please","to","for","of","and","then"
        };

        var toks = Tokens(tail)
            .Where(t => !drop.Contains(t))
            .ToArray();

        return string.Join(" ", toks).Trim();
    }

    private static string CanonicalizeFindTarget(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail)) return tail;

        string norm = Normalize(tail);
        if (string.IsNullOrWhiteSpace(norm)) return tail.Trim();

        string p = " " + norm + " ";

        if (p.Contains(" scaun ") || p.Contains(" scaunul ")) return "scaun";
        if (p.Contains(" birou ") || p.Contains(" masa ")) return "masa";
        if (p.Contains(" usa ")) return "usa";
        if (p.Contains(" telefon ") || p.Contains(" mobil ")) return "telefon";
        if (p.Contains(" laptop ") || p.Contains(" calculator ")) return "laptop";
        if (p.Contains(" televizor ") || p.Contains(" tv ")) return "televizor";
        if (p.Contains(" pat ")) return "pat";
        if (p.Contains(" canapea ")) return "canapea";
        if (p.Contains(" sticla ")) return "sticla";
        if (p.Contains(" cana ")) return "cana";
        if (p.Contains(" dulap ")) return "dulap";

        // dacă nu e obiect uzual, întoarcem null ca să păstrăm random-ul
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
}