using UnityEngine;

#if WINDOWS_UWP
using Windows.Media.SpeechSynthesis;
using Windows.Media.Playback;
using Windows.Media.Core;
#endif

public class HoloLensTTS : MonoBehaviour
{
#if WINDOWS_UWP
    private SpeechSynthesizer synthesizer;
    private MediaPlayer mediaPlayer;
#endif

    [Header("Safety")]
    public float describeCooldown = 3f;
    private float describeTimer = 0f;

    void Start()
    {
#if WINDOWS_UWP
        synthesizer = new SpeechSynthesizer();
        mediaPlayer = new MediaPlayer();
#endif
    }

    public void Speak(string text)
    {
#if WINDOWS_UWP
        var operation = synthesizer.SynthesizeTextToStreamAsync(text);

        operation.Completed = (info, status) =>
        {
            if (status == Windows.Foundation.AsyncStatus.Completed)
            {
                var stream = info.GetResults();

                UnityEngine.WSA.Application.InvokeOnAppThread(() =>
                {
                    mediaPlayer.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                    mediaPlayer.Play();
                }, false);
            }
        };
#else
        Debug.Log("TTS works only on device.");
#endif
    }

    void Update()
    {
        describeTimer -= Time.deltaTime;

        if (Camera.main == null) return;

        Ray ray = new Ray(Camera.main.transform.position, Camera.main.transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, 5f))
        {
            if (describeTimer <= 0f)
            {
#if WINDOWS_UWP
                Speak($"In front of you is {hit.collider.name}");
#endif
#if !WINDOWS_UWP
                Debug.Log($"Would say: In front of you is {hit.collider.name}");
#endif
                describeTimer = describeCooldown;
            }
        }
    }
}