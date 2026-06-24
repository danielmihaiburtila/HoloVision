using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class MenuFlowController : MonoBehaviour
{
    public enum State
    {
        Hidden,
        Onboarding,
        MainMenu,
        FeaturesMenu,
        Listening,      // adaugat - era folosit in VoiceCommandRouter dar lipsea din enum
        Processing,
        DescriptionResult,  // adaugat - pentru ShowDescriptionResult
        TextResult          // adaugat - pentru ShowTextResult
    }

    [Header("Panels")]
    public GameObject panelOnboarding;
    public GameObject panelMainMenu;
    public GameObject panelFeaturesMenu;
    public GameObject panelListening;
    public GameObject panelProcessing;
    public GameObject panelDescriptionResult;
    public DescriptionResultPanelUI descriptionResultUI;
    public GameObject panelTextResult;
    public TextResultPanelUI textResultUI;
    public ProcessingPanelUI processingUI;

    [Header("Extra panels (din Hierarchy - ascunse automat)")]
    // Acestea sunt panelurile pe care le-am vazut in Hierarchy la tine.
    // Daca nu le ai cu exact aceste nume, le poti lasa null - nu crapa.
    public GameObject panelScanResult;
    public GameObject panelSearchResult;
    public GameObject panelLightCheckResult;
    public GameObject panelSOSAlert;

    [Header("Optional")]
    public TextMeshProUGUI statusText;

    public State CurrentState { get; private set; } = State.Hidden;

    private CanvasGroup rootCanvasGroup;

    private void Awake()
    {
        rootCanvasGroup = GetComponent<CanvasGroup>();
        if (rootCanvasGroup == null)
            rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();

        // Auto-find by name daca nu sunt legate in Inspector
        if (panelOnboarding == null) panelOnboarding = FindChild("PanelOnBoarding");
        if (panelMainMenu == null) panelMainMenu = FindChild("PanelMainMenu");
        if (panelFeaturesMenu == null) panelFeaturesMenu = FindChild("PanelFeaturesMenu");
        if (panelListening == null) panelListening = FindChild("PanelListening");
        if (panelProcessing == null) panelProcessing = FindChild("PanelProcessing");
        if (panelDescriptionResult == null) panelDescriptionResult = FindChild("PanelDescriptionResult");
        if (panelTextResult == null) panelTextResult = FindChild("PanelTextResult");

        // Extra panels - auto-find
        if (panelScanResult == null) panelScanResult = FindChild("PanelScanResult");
        if (panelSearchResult == null) panelSearchResult = FindChild("PanelSearchResult");
        if (panelLightCheckResult == null) panelLightCheckResult = FindChild("PanelLightCheckResult");
        if (panelSOSAlert == null) panelSOSAlert = FindChild("PanelSOSAlert");

        // Auto-find UI components daca nu sunt legate
        if (descriptionResultUI == null && panelDescriptionResult != null)
            descriptionResultUI = panelDescriptionResult.GetComponent<DescriptionResultPanelUI>();

        if (textResultUI == null && panelTextResult != null)
            textResultUI = panelTextResult.GetComponent<TextResultPanelUI>();

        if (processingUI == null && panelProcessing != null)
            processingUI = panelProcessing.GetComponent<ProcessingPanelUI>();
    }

    private void Start()
    {
        ShowOnboardingMenu();
    }

    // ─────────────────────────────────────────────────────────────
    // HELPERS PRIVATI
    // ─────────────────────────────────────────────────────────────

    private GameObject FindChild(string childName)
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.name == childName)
                return t.gameObject;
        }
        return null;
    }

    private void ForceRootVisible()
    {
        gameObject.SetActive(true);

        if (rootCanvasGroup == null)
            rootCanvasGroup = GetComponent<CanvasGroup>();

        rootCanvasGroup.alpha = 1f;
        rootCanvasGroup.interactable = true;
        rootCanvasGroup.blocksRaycasts = true;
        rootCanvasGroup.ignoreParentGroups = false;
    }

    private void SetPanel(GameObject panel, bool active)
    {
        if (panel == null) return;

        panel.SetActive(active);

        CanvasGroup cg = panel.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = active ? 1f : 0f;
            cg.interactable = active;
            cg.blocksRaycasts = active;
        }

        if (active)
        {
            panel.transform.SetAsLastSibling();
            panel.transform.localPosition = Vector3.zero;
            panel.transform.localRotation = Quaternion.identity;
            panel.transform.localScale = Vector3.one;
        }
    }

    /// <summary>
    /// Ascunde TOATE panelurile cunoscute. 
    /// Apelat intern inainte de orice Show, ca sa nu ramana doua paneluri vizibile simultan.
    /// </summary>
    private void HideAllPanels()
    {
        SetPanel(panelOnboarding, false);
        SetPanel(panelMainMenu, false);
        SetPanel(panelFeaturesMenu, false);
        SetPanel(panelListening, false);
        SetPanel(panelProcessing, false);
        SetPanel(panelDescriptionResult, false);
        SetPanel(panelTextResult, false);

        // Extra panels - ascunse si ele, ca sa nu ramana vizibile accidental
        SetPanel(panelScanResult, false);
        SetPanel(panelSearchResult, false);
        SetPanel(panelLightCheckResult, false);
        SetPanel(panelSOSAlert, false);
    }

    public void SetStatus(string msg)
    {
        if (statusText != null)
            statusText.text = msg;

        Debug.Log("[MenuFlowController] " + msg);
    }

    // ─────────────────────────────────────────────────────────────
    // SHOW METHODS - ordinea fluxului tau:
    // Onboarding → MainMenu → FeaturesMenu → Listening → Processing → Result
    // ─────────────────────────────────────────────────────────────

    public void ShowOnboardingMenu()
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelOnboarding, true);
        CurrentState = State.Onboarding;
        SetStatus("Onboarding activ.");
    }

    public void ShowMainMenu()
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelMainMenu, true);
        CurrentState = State.MainMenu;
        SetStatus("Meniu principal activ.");
    }

    public void ShowFeaturesMenu()
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelFeaturesMenu, true);
        CurrentState = State.FeaturesMenu;
        SetStatus("Meniu functii activ.");
    }

    /// <summary>
    /// Afiseaza indicatorul de ascultare (PanelListening).
    /// State-ul logic ramine FeaturesMenu - VoiceCommandRouter verifica State == FeaturesMenu.
    /// </summary>
    public void ShowListeningIndicator()
    {
        ForceRootVisible();
        // Ascundem features dar NU schimbam CurrentState,
        // ca VoiceCommandRouter sa continue sa primeasca comenzi.
        SetPanel(panelFeaturesMenu, false);
        SetPanel(panelProcessing, false);
        SetPanel(panelDescriptionResult, false);
        SetPanel(panelTextResult, false);
        SetPanel(panelScanResult, false);
        SetPanel(panelSearchResult, false);
        SetPanel(panelLightCheckResult, false);
        SetPanel(panelSOSAlert, false);

        SetPanel(panelListening, true);

        // State ramine FeaturesMenu intentionat
        CurrentState = State.FeaturesMenu;
        SetStatus("Ascultare vocala activa.");
    }

    /// <summary>
    /// Ascunde doar panelul de listening si readuce panelFeaturesMenu daca era activ.
    /// </summary>
    public void HideListeningIndicator()
    {
        SetPanel(panelListening, false);

        if (CurrentState == State.FeaturesMenu)
            SetPanel(panelFeaturesMenu, true);

        SetStatus("Ascultare vocala ascunsa.");
    }

    /// <summary>
    /// Flux: comanda auzita → Processing afisata imediat, inainte de raspuns Azure.
    /// </summary>
    public void ShowProcessingPanel(string featureName = "")
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelProcessing, true);

        if (processingUI != null)
            processingUI.Show(featureName);

        CurrentState = State.Processing;
        SetStatus("Procesare: " + featureName);
    }

    /// <summary>
    /// Apelata de VoiceCommandRouter.ShowDescriptionResult() dupa ce Azure a returnat descrierea.
    /// Flux: Processing → DescriptionResult panel.
    /// </summary>
    public void ShowDescriptionResult(string description)
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelDescriptionResult, true);

        if (descriptionResultUI != null)
        {
            descriptionResultUI.ShowResult(description);
        }
        else
        {
            Debug.LogError("[MenuFlowController] descriptionResultUI este NULL! " +
                           "Verifica legatura din Inspector pe Canvas_Main_Working → " +
                           "MenuFlowController → Description Result UI.");
        }

        CurrentState = State.DescriptionResult;
        SetStatus("Rezultat descriere afisat.");
    }

    /// <summary>
    /// Apelata de VoiceCommandRouter.ShowTextResult() dupa ce Azure OCR a returnat textul.
    /// Flux: Processing → TextResult panel.
    /// </summary>
    public void ShowTextResult(string text)
    {
        ForceRootVisible();
        HideAllPanels();
        SetPanel(panelTextResult, true);

        if (textResultUI != null)
        {
            textResultUI.ShowResult(text);
        }
        else
        {
            Debug.LogError("[MenuFlowController] textResultUI este NULL! " +
                           "Verifica legatura din Inspector pe Canvas_Main_Working → " +
                           "MenuFlowController → Text Result UI.");
        }

        CurrentState = State.TextResult;
        SetStatus("Rezultat text afisat.");
    }

    /// <summary>
    /// Ascunde canvas-ul complet (ex: cand e activa siguranta la mers si nu vrei UI).
    /// </summary>
    public void HideMenuOverlayNow()
    {
        if (rootCanvasGroup == null)
            rootCanvasGroup = GetComponent<CanvasGroup>();

        HideAllPanels();

        rootCanvasGroup.alpha = 0f;
        rootCanvasGroup.interactable = false;
        rootCanvasGroup.blocksRaycasts = false;

        CurrentState = State.Hidden;
        Debug.Log("[MenuFlowController] Canvas complet ascuns.");
    }

    /// <summary>
    /// Alias folosit de VoiceCommandRouter dupa un rezultat cu eroare.
    /// </summary>
    public void ShowFeaturesMenuAfterError()
    {
        ShowFeaturesMenu();
    }
}