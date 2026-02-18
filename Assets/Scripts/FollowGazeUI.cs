using UnityEngine;

public class FollowGazeUI : MonoBehaviour
{
    public float distance = 1.5f;   // cât de departe în fața camerei
    public float smooth = 8f;       // cât de rapid se mișcă după cap

    void LateUpdate()
    {
        if (Camera.main == null) return;

        Transform cam = Camera.main.transform;

        // poziția dorită: în fața camerei, pe direcția privirii
        Vector3 targetPos = cam.position + cam.forward * distance;

        // mișcare lină
        transform.position = Vector3.Lerp(transform.position, targetPos,
                                          Time.deltaTime * smooth);

        // UI-ul e mereu orientat spre cameră
        transform.rotation = Quaternion.LookRotation(transform.position - cam.position);
    }
}
