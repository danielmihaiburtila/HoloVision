using UnityEngine;

public class SpatialMeshDebug : MonoBehaviour
{
    public LayerMask spatialLayer;

    void Start()
    {
        if (spatialLayer.value == 0)
        {
            int layer = LayerMask.NameToLayer("Spatial Awareness");
            if (layer >= 0) spatialLayer = 1 << layer;
        }
        InvokeRepeating(nameof(Check), 1f, 2f);
    }

    void Check()
    {
        int count = 0;
        foreach (var mc in FindObjectsOfType<MeshCollider>(true))
        {
            if (((1 << mc.gameObject.layer) & spatialLayer.value) != 0)
                count++;
        }
        Debug.Log("[SpatialMeshDebug] MeshColliders on Spatial layer = " + count);
    }
}