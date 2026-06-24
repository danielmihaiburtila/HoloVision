using System;
using UnityEngine;
using ZXing;
using ZXing.Common;

public class BarcodeDecoderService : MonoBehaviour
{
    [Header("Decoder")]
    public bool tryHarder = true;
    public bool autoRotate = true;

    [Header("Preprocessing")]
    public bool tryEnhancedBinaryPass = true;

    public bool TryDecode(byte[] jpgBytes, out string barcode)
    {
        barcode = null;

        if (jpgBytes == null || jpgBytes.Length == 0)
            return false;

        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        try
        {
            if (!tex.LoadImage(jpgBytes))
            {
                Destroy(tex);
                return false;
            }

            bool ok = TryDecode(tex, out barcode);
            Destroy(tex);
            return ok;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BarcodeDecoderService] TryDecode(byte[]) exception: " + e.Message);
            Destroy(tex);
            return false;
        }
    }

    public bool TryDecode(Texture2D tex, out string barcode)
    {
        barcode = null;
        if (tex == null) return false;

        try
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = autoRotate,
                Options = new DecodingOptions
                {
                    TryHarder = tryHarder,
                    TryInverted = true
                }
            };

            Result result;

            // 1) imagine completã
            result = DecodeTexture(reader, tex);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            int w = tex.width;
            int h = tex.height;

            // 2) centru
            Texture2D center = Crop(tex, w / 4, h / 4, w / 2, h / 2);
            result = DecodeTexture(reader, center);
            Destroy(center);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 3) jumãtatea de jos
            Texture2D lower = Crop(tex, 0, 0, w, h / 2);
            result = DecodeTexture(reader, lower);
            Destroy(lower);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 4) jumãtatea de sus
            Texture2D upper = Crop(tex, 0, h / 2, w, h / 2);
            result = DecodeTexture(reader, upper);
            Destroy(upper);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 5) jumãtatea stângã
            Texture2D left = Crop(tex, 0, 0, w / 2, h);
            result = DecodeTexture(reader, left);
            Destroy(left);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 6) jumãtatea dreaptã
            Texture2D right = Crop(tex, w / 2, 0, w / 2, h);
            result = DecodeTexture(reader, right);
            Destroy(right);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 7) bandã verticalã centralã
            Texture2D verticalBand = Crop(tex, w / 3, 0, Mathf.Max(1, w / 3), h);
            result = DecodeTexture(reader, verticalBand);
            Destroy(verticalBand);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 8) bandã orizontalã centralã
            Texture2D horizontalBand = Crop(tex, 0, h / 3, w, Mathf.Max(1, h / 3));
            result = DecodeTexture(reader, horizontalBand);
            Destroy(horizontalBand);
            if (IsValid(result))
            {
                barcode = result.Text.Trim();
                return true;
            }

            // 9) imagine binarizatã pentru contrast puternic
            if (tryEnhancedBinaryPass)
            {
                Texture2D enhanced = EnhanceForBarcode(tex);
                result = DecodeTexture(reader, enhanced);
                Destroy(enhanced);

                if (IsValid(result))
                {
                    barcode = result.Text.Trim();
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BarcodeDecoderService] TryDecode(Texture2D) exception: " + e.Message);
        }

        return false;
    }

    private Result DecodeTexture(BarcodeReaderGeneric reader, Texture2D tex)
    {
        if (reader == null || tex == null) return null;

        try
        {
            Color32[] pixels = tex.GetPixels32();
            byte[] raw = ConvertColor32ToRGBA(pixels);

            var source = new RGBLuminanceSource(
                raw,
                tex.width,
                tex.height,
                RGBLuminanceSource.BitmapFormat.RGBA32
            );

            return reader.Decode(source);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[BarcodeDecoderService] DecodeTexture exception: " + e.Message);
            return null;
        }
    }

    private byte[] ConvertColor32ToRGBA(Color32[] pixels)
    {
        if (pixels == null || pixels.Length == 0)
            return Array.Empty<byte>();

        byte[] raw = new byte[pixels.Length * 4];
        int j = 0;

        for (int i = 0; i < pixels.Length; i++)
        {
            raw[j++] = pixels[i].r;
            raw[j++] = pixels[i].g;
            raw[j++] = pixels[i].b;
            raw[j++] = pixels[i].a;
        }

        return raw;
    }

    private bool IsValid(Result result)
    {
        return result != null && !string.IsNullOrWhiteSpace(result.Text);
    }

    private Texture2D Crop(Texture2D src, int x, int y, int w, int h)
    {
        w = Mathf.Clamp(w, 1, src.width);
        h = Mathf.Clamp(h, 1, src.height);
        x = Mathf.Clamp(x, 0, src.width - w);
        y = Mathf.Clamp(y, 0, src.height - h);

        Texture2D dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(src.GetPixels(x, y, w, h));
        dst.Apply(false);
        return dst;
    }

    private Texture2D EnhanceForBarcode(Texture2D src)
    {
        Texture2D dst = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        Color32[] px = src.GetPixels32();

        for (int i = 0; i < px.Length; i++)
        {
            byte gray = (byte)((px[i].r * 30 + px[i].g * 59 + px[i].b * 11) / 100);
            byte boosted = gray > 140 ? (byte)255 : (byte)0;
            px[i] = new Color32(boosted, boosted, boosted, 255);
        }

        dst.SetPixels32(px);
        dst.Apply(false);
        return dst;
    }
}