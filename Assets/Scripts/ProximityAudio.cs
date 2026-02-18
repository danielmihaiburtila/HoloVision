using UnityEngine;

public class ProximityAudio : MonoBehaviour
{
    public AudioSource audioSource;
    public float maxDistance = 5f;

    void Update()
    {
        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, maxDistance))
        {
            // dacă vedem un obiect, îl highlightăm și redăm sunet
            if (!audioSource.isPlaying)
                audioSource.Play();

            // mutăm sunetul la poziția obiectului
            audioSource.transform.position = hit.point;
        }
        else
        {
            if (audioSource.isPlaying)
                audioSource.Stop();
        }
    }
}
