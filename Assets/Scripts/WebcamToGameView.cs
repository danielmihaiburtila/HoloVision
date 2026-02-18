using UnityEngine;
using UnityEngine.UI;

public class WebcamManager : MonoBehaviour
{
    public RawImage display;
    private WebCamTexture camTexture;

    void Start()
    {
#if UNITY_EDITOR
        camTexture = new WebCamTexture();
        if (display != null)
        {
            display.texture = camTexture;
            camTexture.Play();
        }
#endif
        // Pe ochelari, camera este gestionatã automat de sistem, deci lãsãm scriptul inactiv acolo.
    }
}