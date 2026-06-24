using System.Collections;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class BeepGuide : MonoBehaviour
{
    public enum Mode { Searching, GettingCloser, Found }

    [Header("Audio")]
    [Range(0f, 1f)] public float volume = 0.12f;
    public float beepFreqHz = 880f;
    public float beepDuration = 0.06f;

    [Header("Cadence (sec)")]
    public float intervalSearching = 0.65f;
    public float intervalGettingCloser = 0.20f;
    public float intervalFoundBurst = 0.09f;
    public int foundBurstCount = 3;

    [Header("Safety")]
    public bool lockTickDuringBurst = true;

    private AudioSource src;
    private AudioClip clip;
    private int sampleRate;

    private float nextBeepTime = 0f;
    private Mode mode = Mode.Searching;
    private bool burstRunning = false;

    private float lastFreq;
    private float lastDur;
    private float lastVol;

    private void Awake()
    {
        src = GetComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f;
        src.volume = volume;

        sampleRate = AudioSettings.outputSampleRate;

        lastFreq = beepFreqHz;
        lastDur = beepDuration;
        lastVol = volume;

        clip = CreateSineClip(beepFreqHz, beepDuration, sampleRate);
    }

    private void OnValidate()
    {
        beepFreqHz = Mathf.Max(40f, beepFreqHz);
        beepDuration = Mathf.Clamp(beepDuration, 0.02f, 0.25f);

        intervalSearching = Mathf.Clamp(intervalSearching, 0.10f, 2.00f);
        intervalGettingCloser = Mathf.Clamp(intervalGettingCloser, 0.05f, 1.00f);
        intervalFoundBurst = Mathf.Clamp(intervalFoundBurst, 0.03f, 0.30f);

        foundBurstCount = Mathf.Clamp(foundBurstCount, 1, 8);
    }

    private void Update()
    {
        if (!Mathf.Approximately(lastFreq, beepFreqHz) || !Mathf.Approximately(lastDur, beepDuration))
        {
            lastFreq = beepFreqHz;
            lastDur = beepDuration;

            if (clip != null)
            {
                Destroy(clip);
                clip = null;
            }

            clip = CreateSineClip(beepFreqHz, beepDuration, sampleRate);
        }

        if (!Mathf.Approximately(lastVol, volume))
        {
            lastVol = volume;
            if (src != null) src.volume = volume;
        }
    }

    public void SetEnabled(bool on)
    {
        enabled = on;
        nextBeepTime = 0f;

        if (!on)
        {
            burstRunning = false;
            StopAllCoroutines();
        }
    }

    public void SetMode(Mode m) => mode = m;

    public void Tick()
    {
        if (!enabled) return;
        if (src == null || clip == null) return;
        if (lockTickDuringBurst && burstRunning) return;

        float interval = (mode == Mode.GettingCloser) ? intervalGettingCloser : intervalSearching;
        if (Time.time < nextBeepTime) return;

        nextBeepTime = Time.time + interval;
        PlayBeep();
    }

    public void PlayConfirm()
    {
        if (!enabled) return;
        StopAllCoroutines();
        StartCoroutine(FoundBurstRoutine());
    }

    private IEnumerator FoundBurstRoutine()
    {
        burstRunning = true;

        var prevMode = mode;
        mode = Mode.Found;

        int n = Mathf.Max(1, foundBurstCount);

        for (int i = 0; i < n; i++)
        {
            PlayBeep();
            yield return new WaitForSeconds(intervalFoundBurst);
        }

        mode = prevMode == Mode.Found ? Mode.GettingCloser : prevMode;
        burstRunning = false;
        nextBeepTime = 0f;
    }

    private void PlayBeep()
    {
        if (src == null || clip == null) return;
        src.PlayOneShot(clip, volume);
    }

    private static AudioClip CreateSineClip(float freqHz, float durationSec, int sr)
    {
        int samples = Mathf.Max(64, Mathf.RoundToInt(sr * durationSec));
        var data = new float[samples];

        float inc = 2f * Mathf.PI * freqHz / sr;
        float phase = 0f;

        int fade = Mathf.Max(4, samples / 10);

        for (int i = 0; i < samples; i++)
        {
            float env = 1f;
            if (i < fade) env = i / (float)fade;
            else if (i > samples - fade) env = (samples - i) / (float)fade;

            data[i] = Mathf.Sin(phase) * env;
            phase += inc;
        }

        var c = AudioClip.Create("beep", samples, 1, sr, false);
        c.SetData(data, 0);
        return c;
    }
}