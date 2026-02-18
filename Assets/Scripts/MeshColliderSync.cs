using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class MeshColliderSync : MonoBehaviour
{
    MeshFilter mf;
    MeshCollider mc;
    Mesh last;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mc = GetComponent<MeshCollider>();
    }

    void LateUpdate()
    {
        var m = mf.sharedMesh;
        if (m == null) return;

        if (m != last || mc.sharedMesh != m)
        {
            mc.sharedMesh = null;
            mc.sharedMesh = m;
            last = m;
        }
    }
}