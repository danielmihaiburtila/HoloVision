using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BeepGuide : MonoBehaviour
{
    [Range(0f, 1f)] public float volume = 0.25f;
    public float beepFrequencyHz = 950f;
    public float beepDuration = 0.06f;

    private AudioSource src;
    private AudioClip beepClip;
    private Coroutine loop;

    private void Awake()
    {
        src = GetComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D (în cap)
        src.loop = false;

        beepClip = CreateSineBeep(beepFrequencyHz, beepDuration);
    }

    public void StartBeeping(float intervalSeconds)
    {
        intervalSeconds = Mathf.Clamp(intervalSeconds, 0.08f, 1.2f);

        StopBeeping();
        loop = StartCoroutine(BeepLoop(intervalSeconds));
    }

    public void StopBeeping()
    {
        if (loop != null)
        {
            StopCoroutine(loop);
            loop = null;
        }
    }

    private IEnumerator BeepLoop(float interval)
    {
        while (true)
        {
            if (beepClip != null)
                src.PlayOneShot(beepClip, volume);

            yield return new WaitForSeconds(interval);
        }
    }

    private static AudioClip CreateSineBeep(float freq, float duration)
    {
        int sampleRate = 44100;
        int samples = Mathf.CeilToInt(sampleRate * duration);
        var clip = AudioClip.Create("beep", samples, 1, sampleRate, false);

        float[] data = new float[samples];
        float inc = 2f * Mathf.PI * freq / sampleRate;
        float phase = 0f;

        for (int i = 0; i < samples; i++)
        {
            data[i] = Mathf.Sin(phase) * 0.9f;
            phase += inc;
        }

        clip.SetData(data, 0);
        return clip;
    }
}