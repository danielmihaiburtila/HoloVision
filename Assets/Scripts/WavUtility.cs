using System;
using System.IO;
using UnityEngine;

public static class WavUtility
{
    public static byte[] FromAudioClip(AudioClip clip, int targetSampleRate = 16000, int targetChannels = 1)
    {
        if (clip == null) return null;

        float[] src = new float[clip.samples * clip.channels];
        clip.GetData(src, 0);

        float[] mono;
        if (clip.channels == 1)
        {
            mono = src;
        }
        else
        {
            mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++)
            {
                float sum = 0f;
                for (int c = 0; c < clip.channels; c++)
                    sum += src[i * clip.channels + c];
                mono[i] = sum / clip.channels;
            }
        }

        float[] resampled = Resample(mono, clip.frequency, targetSampleRate);
        return EncodeWav16(resampled, targetSampleRate, targetChannels);
    }

    private static float[] Resample(float[] input, int srcRate, int dstRate)
    {
        if (srcRate == dstRate) return input;

        int newLength = Mathf.RoundToInt(input.Length * (dstRate / (float)srcRate));
        float[] output = new float[newLength];

        for (int i = 0; i < newLength; i++)
        {
            float pos = i * (input.Length - 1f) / (newLength - 1f);
            int left = Mathf.FloorToInt(pos);
            int right = Mathf.Min(left + 1, input.Length - 1);
            float t = pos - left;
            output[i] = Mathf.Lerp(input[left], input[right], t);
        }

        return output;
    }

    private static byte[] EncodeWav16(float[] samples, int sampleRate, int channels)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            int byteRate = sampleRate * channels * 2;
            int dataLength = samples.Length * 2;

            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);

            writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            for (int i = 0; i < samples.Length; i++)
            {
                short pcm = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
                writer.Write(pcm);
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}