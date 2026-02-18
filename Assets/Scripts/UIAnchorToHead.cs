using UnityEngine;

public class UIAnchorToHead : MonoBehaviour
{
    public float distance = 1.2f;
    public float height = -0.05f;
    public float smooth = 10f;

    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;

        Vector3 targetPos = cam.transform.position + cam.transform.forward * distance;
        targetPos.y += height;

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smooth);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.LookRotation(transform.position - cam.transform.position),
            Time.deltaTime * smooth
        );
    }
}