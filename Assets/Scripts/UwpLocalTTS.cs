using System;
using System.Collections;
using System.IO;
using UnityEngine;

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
using Windows.Media.SpeechSynthesis;
using System.Threading.Tasks;
#endif

[RequireComponent(typeof(AudioSource))]
public class UwpLocalTTS : MonoBehaviour
{
    private AudioSource _audio;

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.loop = false;
    }

    public void Speak(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        Stop();

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
        StartCoroutine(SpeakUwp(text));
#else
        // În Editor nu avem UWP synth. Nu facem nimic.
#endif
    }

    public void Stop()
    {
        if (_audio == null) return;
        if (_audio.isPlaying) _audio.Stop();
        if (_audio.clip != null)
        {
            Destroy(_audio.clip);
            _audio.clip = null;
        }
    }

#if (WINDOWS_UWP || UNITY_WSA || UNITY_WSA_10_0) && !UNITY_EDITOR
    private IEnumerator SpeakUwp(string text)
    {
        SpeechSynthesizer synth = null;
        SpeechSynthesisStream stream = null;

        Task<SpeechSynthesisStream> task = null;

        try
        {
            synth = new SpeechSynthesizer();
            task = synth.SynthesizeTextToStreamAsync(text).AsTask();
        }
        catch
        {
            // Nu putem porni sintetizarea
            yield break;
        }

        // IMPORTANT: yield NU e în try/catch
        while (task != null && !task.IsCompleted)
            yield return null;

        if (task == null || task.IsCanceled || task.IsFaulted)
            yield break;

        try
        {
            stream = task.Result;
        }
        catch
        {
            yield break;
        }

        if (stream == null) yield break;

        byte[] bytes;
        try
        {
            using (var input = stream.AsStreamForRead())
            using (var ms = new MemoryStream())
            {
                input.CopyTo(ms);
                bytes = ms.ToArray();
            }
        }
        catch
        {
            yield break;
        }

        if (bytes == null || bytes.Length < 44) yield break;

        int dataOffset = FindWavDataOffset(bytes, out int dataSize, out int sampleRate, out int channels);
        if (dataOffset < 0 || dataSize <= 0) yield break;

        int samples16 = dataSize / 2; // 16-bit PCM
        if (samples16 <= 0) yield break;

        float[] samples = new float[samples16];

        int bi = dataOffset;
        int end = Mathf.Min(bytes.Length, dataOffset + dataSize);

        int iSample = 0;
        while (bi + 1 < end && iSample < samples16)
        {
            short s = (short)(bytes[bi] | (bytes[bi + 1] << 8));
            samples[iSample] = s / 32768f;
            bi += 2;
            iSample++;
        }

        int safeChannels = Math.Max(1, channels);
        int totalFrames = iSample / safeChannels;
        if (totalFrames <= 0) yield break;

        var clip = AudioClip.Create("UwpTTS", totalFrames, safeChannels, Math.Max(8000, sampleRate), false);
        clip.SetData(samples, 0);

        if (_audio == null) yield break;

        if (_audio.clip != null)
        {
            Destroy(_audio.clip);
            _audio.clip = null;
        }

        _audio.clip = clip;
        _audio.Play();
    }

    private int FindWavDataOffset(byte[] wav, out int dataSize, out int sampleRate, out int channels)
    {
        dataSize = 0;
        sampleRate = 16000;
        channels = 1;

        if (wav == null || wav.Length < 44) return -1;

        // WAV basic parser: caută chunk "fmt " și "data"
        int pos = 12;

        while (pos + 8 <= wav.Length)
        {
            if (pos + 8 > wav.Length) break;

            string chunkId = System.Text.Encoding.ASCII.GetString(wav, pos, 4);
            int chunkSize = BitConverter.ToInt32(wav, pos + 4);
            pos += 8;

            if (chunkSize < 0) return -1;
            if (pos + chunkSize > wav.Length) break;

            if (chunkId == "fmt ")
            {
                // fmt: AudioFormat(2), NumChannels(2), SampleRate(4) ...
                if (chunkSize >= 16)
                {
                    channels = BitConverter.ToInt16(wav, pos + 2);
                    sampleRate = BitConverter.ToInt32(wav, pos + 4);
                }
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                return pos;
            }

            pos += chunkSize;
        }

        return -1;
    }
#endif
}