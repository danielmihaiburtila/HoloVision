using UnityEngine;
using TMPro;

public class FollowSpeechUI : MonoBehaviour
{
    public Transform head;
    public float distance = 1.2f;
    public float height = -0.05f;
    public float smooth = 8f;

    void Start()
    {
        if (head == null)
            head = Camera.main.transform;
    }

    void LateUpdate()
    {
        // poziție în fața camerei
        Vector3 targetPos = head.position + head.forward * distance;
        targetPos.y += height;

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * smooth);

        // să se orienteze spre cameră
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            Quaternion.LookRotation(transform.position - head.position),
            Time.deltaTime * smooth
        );
    }
}

