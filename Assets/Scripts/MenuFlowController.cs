using TMPro;
using UnityEngine;

public class MenuFlowController : MonoBehaviour
{
    public enum State { MainMenu, FeaturesMenu }

    [Header("Panels (drag from Canvas)")]
    public GameObject panelMainMenu;
    public GameObject panelFeaturesMenu;

    [Header("Optional text on UI")]
    public TextMeshProUGUI statusText;

    [Header("Safety")]
    public bool autoFindIfMissing = false;
    public bool enableWatchdog = true;
    public float watchdogInterval = 0.5f;

    public State CurrentState { get; private set; } = State.MainMenu;

    private State lastRequestedState = State.MainMenu;
    private float nextWatchdogTime;

    private void Awake()
    {
        if (autoFindIfMissing)
            TryAutoWireFromChildren();
    }

    private void Start()
    {
        SafeShowMainMenu();
    }

    private void Update()
    {
        if (!enableWatchdog) return;
        if (Time.unscaledTime < nextWatchdogTime) return;
        nextWatchdogTime = Time.unscaledTime + watchdogInterval;

        if (panelMainMenu == null || panelFeaturesMenu == null)
            return;

        if (!panelMainMenu.activeSelf && !panelFeaturesMenu.activeSelf)
        {
            SetStatus("RECOVERY: panouri OFF → refac meniul.");
            if (lastRequestedState == State.FeaturesMenu) SafeShowFeaturesMenu();
            else SafeShowMainMenu();
        }
    }

    private void TryAutoWireFromChildren()
    {
        if (panelMainMenu == null)
            panelMainMenu = FindInChildrenByName(transform, "PanelMainMenu");

        if (panelFeaturesMenu == null)
            panelFeaturesMenu = FindInChildrenByName(transform, "PanelFeaturesMenu");
    }

    private static GameObject FindInChildrenByName(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }

    [ContextMenu("DEBUG/Show Main Menu")]
    public void ShowMainMenu() => SafeShowMainMenu();

    [ContextMenu("DEBUG/Show Features Menu")]
    public void ShowFeaturesMenu() => SafeShowFeaturesMenu();

    private void SafeShowMainMenu()
    {
        lastRequestedState = State.MainMenu;
        CurrentState = State.MainMenu;

        if (!ValidatePanels()) return;

        SetActiveSafe(panelMainMenu, true);
        SetActiveSafe(panelFeaturesMenu, false);

        SetStatus("Meniu principal: START / EXIT");
    }

    private void SafeShowFeaturesMenu()
    {
        lastRequestedState = State.FeaturesMenu;
        CurrentState = State.FeaturesMenu;

        if (!ValidatePanels()) return;

        SetActiveSafe(panelFeaturesMenu, true);
        SetActiveSafe(panelMainMenu, false);

        SetStatus("Funcții: DESCRIERE / CITIRE / ÎNAPOI");
    }

    private bool ValidatePanels()
    {
        if (panelMainMenu == null || panelFeaturesMenu == null)
        {
            SetStatus("EROARE UI: setează PanelMainMenu și PanelFeaturesMenu în Inspector.");
            return false;
        }
        return true;
    }

    private static void SetActiveSafe(GameObject go, bool active)
    {
        if (go == null) return;

        if (go.activeSelf != active)
            go.SetActive(active);

        var cg = go.GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = active;
            cg.blocksRaycasts = active;
        }
    }

    public void SetStatus(string msg)
    {
        if (statusText != null) statusText.text = msg;
        Debug.Log("[Menu] " + msg);
    }
}