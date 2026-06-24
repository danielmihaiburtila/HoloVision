using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class HL2CanvasFixup : MonoBehaviour
{
    public bool forceDefaultLayerForTest = false; // doar test
    private void Awake()
    {
        var canvas = GetComponent<Canvas>();

        // Leagã camera realã
        var cam = Camera.main;
        if (cam == null) cam = FindObjectOfType<Camera>(true);

        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && cam != null)
            canvas.worldCamera = cam;

        // Asigurã cã root-ul nu rãmâne invizibil
        var cg = GetComponent<CanvasGroup>();
        if (cg != null)
        {
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        // Test extrem: mutã tot UI pe Default ca sã excluzi culling mask
        if (forceDefaultLayerForTest)
        {
            int def = LayerMask.NameToLayer("Default");
            if (def >= 0) SetLayerRecursively(gameObject, def);
        }
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        foreach (Transform t in root.transform)
            SetLayerRecursively(t.gameObject, layer);
    }
}