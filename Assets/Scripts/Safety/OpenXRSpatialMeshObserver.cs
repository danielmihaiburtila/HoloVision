using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

public class OpenXRSpatialMeshObserver : MonoBehaviour
{
    [Header("Prefab")]
    [Tooltip("Prefab care conține MeshFilter + MeshRenderer + MeshCollider (non-convex).")]
    public GameObject meshPrefab; // SpatialMeshChunk prefab

    [Header("Center")]
    [Tooltip("Centru pentru bounding volume. Setează Main Camera.")]
    public Transform center;

    [Header("Bounds (meters)")]
    [Tooltip("Extents pentru bounding box (x,y,z). Ex: 4 => box 8m x 8m x 8m în jurul userului.")]
    public float extents = 4.0f;

    [Header("Update")]
    [Tooltip("Cât de des reîmprospătează lista de mesh-uri (secunde).")]
    public float updateInterval = 2.0f;

    [Header("Debug")]
    public bool log = true;

    private XRMeshSubsystem _meshSubsystem;
    private readonly Dictionary<MeshId, GameObject> _meshObjects = new();
    private readonly List<MeshInfo> _meshInfos = new();
    private float _nextUpdateTime;

    private void Start()
    {
        if (center == null)
        {
            var cam = Camera.main;
            if (cam != null) center = cam.transform;
        }

        // 1) Găsește XRMeshSubsystem (UnityEngine.XR)
        var subsystems = new List<XRMeshSubsystem>();
        SubsystemManager.GetInstances(subsystems);

        if (subsystems.Count == 0)
        {
            Debug.LogError("[OpenXRSpatialMeshObserver] No XRMeshSubsystem found. Meshing is not available/enabled.");
            enabled = false;
            return;
        }

        _meshSubsystem = subsystems[0];

        if (log)
            Debug.Log("[OpenXRSpatialMeshObserver] XRMeshSubsystem found: " + _meshSubsystem.SubsystemDescriptor.id);

        _nextUpdateTime = Time.time + 0.5f;
    }

    private void Update()
    {
        if (_meshSubsystem == null || center == null) return;
        if (Time.time < _nextUpdateTime) return;

        _nextUpdateTime = Time.time + updateInterval;

        TrySetBoundingVolume();

        _meshInfos.Clear();
        if (!_meshSubsystem.TryGetMeshInfos(_meshInfos))
        {
            if (log) Debug.Log("[OpenXRSpatialMeshObserver] TryGetMeshInfos returned false (no data yet).");
            return;
        }

        if (log)
            Debug.Log($"[OpenXRSpatialMeshObserver] MeshInfos count: {_meshInfos.Count}");

        foreach (var info in _meshInfos)
        {
            if (info.ChangeState == MeshChangeState.Removed)
                RemoveMesh(info.MeshId);
            else
                UpdateOrCreateMesh(info.MeshId);
        }
    }

    private void TrySetBoundingVolume()
    {
        // XRMeshSubsystem.SetBoundingVolume(Vector3 origin, Vector3 extents)
        Vector3 boundsExtents = new Vector3(extents, extents, extents);
        _meshSubsystem.SetBoundingVolume(center.position, boundsExtents);
    }

    private void UpdateOrCreateMesh(MeshId meshId)
    {
        if (meshPrefab == null)
        {
            Debug.LogError("[OpenXRSpatialMeshObserver] meshPrefab is NULL. Assign SpatialMeshChunk prefab.");
            enabled = false;
            return;
        }

        if (!_meshObjects.TryGetValue(meshId, out var go) || go == null)
        {
            go = Instantiate(meshPrefab, transform);
            go.name = "SpatialMesh_" + meshId;
            _meshObjects[meshId] = go;
        }

        var meshFilter = go.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            Debug.LogError("[OpenXRSpatialMeshObserver] meshPrefab missing MeshFilter.");
            return;
        }

        if (meshFilter.sharedMesh == null)
            meshFilter.sharedMesh = new Mesh();

        var meshCollider = go.GetComponent<MeshCollider>();
        var mesh = meshFilter.sharedMesh;

        // 5) Cere generare/refresh mesh async
        _meshSubsystem.GenerateMeshAsync(
            meshId,
            mesh,
            meshCollider,
            MeshVertexAttributes.Normals,
            (MeshGenerationResult result) =>
            {
                if (result.Status != MeshGenerationStatus.Success)
                {
                    if (log)
                        Debug.LogWarning("[OpenXRSpatialMeshObserver] Mesh generation failed: " + result.Status);
                    return;
                }

                // Refresh collider
                if (meshCollider != null)
                {
                    meshCollider.sharedMesh = null;
                    meshCollider.sharedMesh = mesh;
                }
            }
        );
    }

    private void RemoveMesh(MeshId meshId)
    {
        if (_meshObjects.TryGetValue(meshId, out var go) && go != null)
        {
            Destroy(go);
        }
        _meshObjects.Remove(meshId);
    }
}