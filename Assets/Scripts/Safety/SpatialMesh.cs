using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class SpatialMesh : MonoBehaviour
{
    [Header("Setup")]
    [Tooltip("Layer-ul pe care va fi pus mesh-ul pentru a putea fi detectat de WalkingSafetySystem.")]
    public string obstacleLayerName = "SpatialObstacle";

    [Tooltip("Dacă e true, colliderul se actualizează automat când mesh-ul se schimbă.")]
    public bool autoSyncCollider = true;

    [Tooltip("Dacă e true, încearcă să aplice layer-ul și copiilor.")]
    public bool applyLayerRecursively = true;

    [Header("Debug")]
    public bool debugLogs = true;

    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private MeshRenderer _meshRenderer;

    private Mesh _lastMesh;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();
        _meshRenderer = GetComponent<MeshRenderer>();

        EnsureLayer();
        SyncColliderWithMesh(force: true);
    }

    private void OnEnable()
    {
        EnsureLayer();
        SyncColliderWithMesh(force: true);
    }

    private void Update()
    {
        if (!autoSyncCollider)
            return;

        SyncColliderWithMesh(force: false);
    }

    /// <summary>
    /// Permite altor sisteme să seteze mesh-ul runtime.
    /// </summary>
    public void SetMesh(Mesh mesh)
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null) _meshCollider = GetComponent<MeshCollider>();

        _meshFilter.sharedMesh = mesh;
        SyncColliderWithMesh(force: true);
    }

    /// <summary>
    /// Ține MeshCollider-ul sincronizat cu MeshFilter-ul.
    /// </summary>
    public void SyncColliderWithMesh(bool force)
    {
        if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
        if (_meshCollider == null) _meshCollider = GetComponent<MeshCollider>();

        Mesh currentMesh = _meshFilter.sharedMesh;

        if (!force && currentMesh == _lastMesh)
            return;

        _lastMesh = currentMesh;

        // Reset înainte de reasignare ca să forțăm refresh-ul colliderului
        _meshCollider.sharedMesh = null;

        if (currentMesh != null)
        {
            _meshCollider.sharedMesh = currentMesh;

            if (debugLogs)
            {
                Debug.Log($"[SpatialMesh] Collider sincronizat cu mesh-ul pe {gameObject.name}");
            }
        }
        else
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[SpatialMesh] {gameObject.name} nu are încă sharedMesh.");
            }
        }
    }

    private void EnsureLayer()
    {
        int layer = LayerMask.NameToLayer(obstacleLayerName);

        if (layer < 0)
        {
            if (debugLogs)
            {
                Debug.LogWarning($"[SpatialMesh] Layer-ul '{obstacleLayerName}' nu există. Creează-l în Tags and Layers.");
            }
            return;
        }

        gameObject.layer = layer;

        if (applyLayerRecursively)
            ApplyLayerToChildren(transform, layer);
    }

    private void ApplyLayerToChildren(Transform root, int layer)
    {
        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            child.gameObject.layer = layer;
            ApplyLayerToChildren(child, layer);
        }
    }
}