using System;
using UnityEngine;

public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] wavFile, string clipName = "wavClip")
    {
        if (wavFile == null || wavFile.Length < 44)
        {
            Debug.LogError("WavUtility Error: WAV file too small.");
            return null;
        }

        int channels = BitConverter.ToInt16(wavFile, 22);
        int sampleRate = BitConverter.ToInt32(wavFile, 24);
        int subchunk2 = BitConverter.ToInt32(wavFile, 40);

        int samples = subchunk2 / 2;
        float[] data = new float[samples];

        int offset = 44;
        for (int i = 0; i < samples; i++)
        {
            short sample = BitConverter.ToInt16(wavFile, offset);
            data[i] = sample / 32768f;
            offset += 2;
        }

        AudioClip audioClip = AudioClip.Create(clipName, samples, channels, sampleRate, false);
        audioClip.SetData(data, 0);
        return audioClip;
    }
}
