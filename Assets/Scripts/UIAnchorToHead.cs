using UnityEngine;

public class UIAnchorToHead : MonoBehaviour
{
    public float distance = 1.2f;
    public float height = -0.05f;
    public float smooth = 10f;

    private Transform head;

    private void Start()
    {
        var cam = Camera.main;
        head = cam != null ? cam.transform : null;
    }

    private void LateUpdate()
    {
        if (head == null)
        {
            var cam = Camera.main;
            head = cam != null ? cam.transform : null;
            if (head == null) return;
        }

        Vector3 targetPos = head.position + head.forward * distance + head.up * height;

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smooth);

        Quaternion targetRot = Quaternion.LookRotation(transform.position - head.position);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * smooth);
    }
}