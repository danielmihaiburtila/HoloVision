using UnityEngine;

public class ProcessingSpinnerUI : MonoBehaviour
{
    public float rotationSpeed = 120f;

    private void Update()
    {
        transform.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
    }
}