using UnityEngine;

#if USE_ZXING
using System.Collections.Generic;
using ZXing;
using ZXing.Common;

public sealed class ZxingDecoder
{
    private readonly BarcodeReaderGeneric reader;
    private byte[] rgbaBuffer;

    public ZxingDecoder(bool enableQr, bool enableCode128)
    {
        var formats = new List<BarcodeFormat>
        {
            BarcodeFormat.EAN_13,
            BarcodeFormat.EAN_8,
            BarcodeFormat.UPC_A,
            BarcodeFormat.UPC_E
        };

        if (enableCode128) formats.Add(BarcodeFormat.CODE_128);
        if (enableQr) formats.Add(BarcodeFormat.QR_CODE);

        var opts = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = formats
        };

        reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = opts
        };
    }

    public bool TryDecode(Texture2D tex, out string text, out Vector2[] points)
    {
        text = null;
        points = null;

        if (tex == null || tex.width <= 0 || tex.height <= 0) return false;

        Color32[] pixels;
        try { pixels = tex.GetPixels32(); }
        catch { return false; }

        int needed = pixels.Length * 4;
        if (rgbaBuffer == null || rgbaBuffer.Length != needed)
            rgbaBuffer = new byte[needed];

        int j = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            var c = pixels[i];
            rgbaBuffer[j++] = c.r;
            rgbaBuffer[j++] = c.g;
            rgbaBuffer[j++] = c.b;
            rgbaBuffer[j++] = c.a;
        }

        Result res;
        try
        {
            res = reader.Decode(rgbaBuffer, tex.width, tex.height, RGBLuminanceSource.BitmapFormat.RGBA32);
        }
        catch { return false; }

        if (res == null || string.IsNullOrWhiteSpace(res.Text)) return false;

        text = res.Text.Trim();

        if (res.ResultPoints != null && res.ResultPoints.Length > 0)
        {
            points = new Vector2[res.ResultPoints.Length];
            for (int i = 0; i < res.ResultPoints.Length; i++)
                points[i] = new Vector2(res.ResultPoints[i].X, res.ResultPoints[i].Y);
        }

        return true;
    }
}
#else
// Fallback stub: proiectul compilează chiar dacă ZXing nu e instalat.
// Decoderul "nu găsește nimic", iar sistemul tău cade pe OCR/alte metode.
public sealed class ZxingDecoder
{
    public ZxingDecoder(bool enableQr, bool enableCode128) { }
    public bool TryDecode(Texture2D tex, out string text, out Vector2[] points)
    {
        text = null;
        points = null;
        return false;
    }
}
#endif