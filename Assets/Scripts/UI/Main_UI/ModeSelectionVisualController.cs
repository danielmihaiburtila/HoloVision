using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ModeSelectionVisualController : MonoBehaviour
{
    [Header("Vocal Card")]
    public Image vocalBase;
    public GameObject vocalSelectedGlow;
    public TextMeshProUGUI vocalTitle;
    public TextMeshProUGUI vocalSubtitle;
    public GameObject vocalBadge;

    [Header("Discreet Card")]
    public Image discreetBase;
    public GameObject discreetSelectedGlow;
    public TextMeshProUGUI discreetTitle;
    public TextMeshProUGUI discreetSubtitle;
    public GameObject discreetBadge;

    [Header("Colors")]
    public Color normalCardColor = new Color32(20, 33, 49, 255);
    public Color selectedCardColor = new Color32(27, 43, 61, 255);

    public Color normalTitleColor = new Color32(220, 228, 235, 255);
    public Color selectedTitleColor = new Color32(255, 255, 255, 255);

    public Color normalSubtitleColor = new Color32(142, 162, 178, 255);
    public Color selectedSubtitleColor = new Color32(199, 246, 255, 255);

    private void Start()
    {
        SetNoSelection();
    }

    public void SetNoSelection()
    {
        ApplyVocal(false);
        ApplyDiscreet(false);
    }

    public void SetVocalSelected()
    {
        ApplyVocal(true);
        ApplyDiscreet(false);
    }

    public void SetDiscreetSelected()
    {
        ApplyVocal(false);
        ApplyDiscreet(true);
    }

    private void ApplyVocal(bool selected)
    {
        if (vocalBase != null)
            vocalBase.color = selected ? selectedCardColor : normalCardColor;

        if (vocalSelectedGlow != null)
            vocalSelectedGlow.SetActive(selected);

        if (vocalTitle != null)
            vocalTitle.color = selected ? selectedTitleColor : normalTitleColor;

        if (vocalSubtitle != null)
            vocalSubtitle.color = selected ? selectedSubtitleColor : normalSubtitleColor;

        if (vocalBadge != null)
            vocalBadge.SetActive(selected);
    }

    private void ApplyDiscreet(bool selected)
    {
        if (discreetBase != null)
            discreetBase.color = selected ? selectedCardColor : normalCardColor;

        if (discreetSelectedGlow != null)
            discreetSelectedGlow.SetActive(selected);

        if (discreetTitle != null)
            discreetTitle.color = selected ? selectedTitleColor : normalTitleColor;

        if (discreetSubtitle != null)
            discreetSubtitle.color = selected ? selectedSubtitleColor : normalSubtitleColor;

        if (discreetBadge != null)
            discreetBadge.SetActive(selected);
    }
}