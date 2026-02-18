using UnityEngine;
using UnityEngine.UI;

public class WebcamTest : MonoBehaviour
{
    void Start()
    {
        WebCamTexture webcamTexture = new WebCamTexture();
        // Aplicăm textura pe un obiect din scenă sau un RawImage din UI
        // Dacă ai un Quad în fața camerei, pune-l acolo.
        GetComponent<Renderer>().material.mainTexture = webcamTexture;
        webcamTexture.Play();
    }
}