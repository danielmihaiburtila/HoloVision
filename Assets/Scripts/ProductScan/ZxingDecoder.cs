// ZxingDecoder.cs
using UnityEngine;

#if USE_ZXING
using System.Collections.Generic;
using Unity.Collections;
using ZXing;
using ZXing.Common;
#endif

public sealed class ZxingDecoder
{
#if USE_ZXING
    private readonly BarcodeReaderGeneric reader;
    private byte[] pixelBuffer;

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

        reader = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = new DecodingOptions
            {
                TryHarder = true,
                TryInverted = true,
                PureBarcode = false,
                PossibleFormats = formats
            }
        };

        try { reader.Options.CharacterSet = "UTF-8"; } catch { }
    }

    // ✅ API publică STABILĂ: out int (nu out BarcodeFormat)
    public bool TryDecodeBestEffort(Texture2D tex, out string text, out Vector2[] points, out int formatId)
    {
        text = null;
        points = null;
        formatId = 0;

        if (tex == null || tex.width < 32 || tex.height < 32)
            return false;

        // 1) RAW (cel mai rapid)
        if (TryDecodeFromRaw(tex, out text, out points, out formatId))
            return true;

        // 2) Managed fallback
        try
        {
            var px = tex.GetPixels32();
            return TryDecodeFromManaged(px, tex.width, tex.height, out text, out points, out formatId);
        }
        catch
        {
            return false;
        }
    }

    private bool TryDecodeFromRaw(Texture2D tex, out string text, out Vector2[] points, out int formatId)
    {
        text = null; points = null; formatId = 0;

        bool isBGRA = tex.format == TextureFormat.BGRA32;
        bool isRGBA = tex.format == TextureFormat.RGBA32;
        if (!isBGRA && !isRGBA) return false;

        NativeArray<byte> raw;
        try { raw = tex.GetRawTextureData<byte>(); }
        catch { return false; }

        if (!raw.IsCreated || raw.Length < tex.width * tex.height * 4) return false;

        var fmt = isBGRA ? RGBLuminanceSource.BitmapFormat.BGRA32 : RGBLuminanceSource.BitmapFormat.RGBA32;

        // FULL
        if (TryDecodeRawCrop(raw, tex.width, tex.height, 0, 0, tex.width, tex.height, fmt, out text, out points, out formatId))
            return true;

        // CENTER 70%
        int cw = Mathf.RoundToInt(tex.width * 0.70f);
        int ch = Mathf.RoundToInt(tex.height * 0.70f);
        int cx = (tex.width - cw) / 2;
        int cy = (tex.height - ch) / 2;
        if (TryDecodeRawCrop(raw, tex.width, tex.height, cx, cy, cw, ch, fmt, out text, out points, out formatId))
            return true;

        // BANDĂ JOS
        int bw = Mathf.RoundToInt(tex.width * 0.92f);
        int bh = Mathf.RoundToInt(tex.height * 0.36f);
        int bx = (tex.width - bw) / 2;
        int by = Mathf.RoundToInt(tex.height * 0.58f);
        by = Mathf.Clamp(by, 0, tex.height - bh);
        if (TryDecodeRawCrop(raw, tex.width, tex.height, bx, by, bw, bh, fmt, out text, out points, out formatId))
            return true;

        // BANDĂ SUS
        int ty = Mathf.RoundToInt(tex.height * 0.06f);
        if (TryDecodeRawCrop(raw, tex.width, tex.height, bx, ty, bw, bh, fmt, out text, out points, out formatId))
            return true;

        return false;
    }

    private bool TryDecodeFromManaged(Color32[] pixels, int w, int h, out string text, out Vector2[] points, out int formatId)
    {
        text = null; points = null; formatId = 0;
        if (pixels == null || pixels.Length < w * h) return false;

        if (TryDecodeManagedCrop(pixels, w, h, 0, 0, w, h, out text, out points, out formatId))
            return true;

        int cw = Mathf.RoundToInt(w * 0.70f);
        int ch = Mathf.RoundToInt(h * 0.70f);
        int cx = (w - cw) / 2;
        int cy = (h - ch) / 2;
        if (TryDecodeManagedCrop(pixels, w, h, cx, cy, cw, ch, out text, out points, out formatId))
            return true;

        int bw = Mathf.RoundToInt(w * 0.92f);
        int bh = Mathf.RoundToInt(h * 0.36f);
        int bx = (w - bw) / 2;
        int by = Mathf.RoundToInt(h * 0.58f);
        by = Mathf.Clamp(by, 0, h - bh);
        if (TryDecodeManagedCrop(pixels, w, h, bx, by, bw, bh, out text, out points, out formatId))
            return true;

        int ty = Mathf.RoundToInt(h * 0.06f);
        if (TryDecodeManagedCrop(pixels, w, h, bx, ty, bw, bh, out text, out points, out formatId))
            return true;

        return false;
    }

    private void EnsureBuffer(int cropW, int cropH)
    {
        int needed = cropW * cropH * 4;
        if (pixelBuffer == null || pixelBuffer.Length != needed)
            pixelBuffer = new byte[needed];
    }

    private bool TryDecodeRawCrop(NativeArray<byte> src, int srcW, int srcH,
        int x, int y, int cropW, int cropH,
        RGBLuminanceSource.BitmapFormat fmt,
        out string text, out Vector2[] points, out int formatId)
    {
        text = null; points = null; formatId = 0;

        if (cropW < 32 || cropH < 32) return false;
        if (x < 0 || y < 0 || x + cropW > srcW || y + cropH > srcH) return false;

        EnsureBuffer(cropW, cropH);

        int dst = 0;
        for (int yy = 0; yy < cropH; yy++)
        {
            int srcRow = ((y + yy) * srcW + x) * 4;
            int rowBytes = cropW * 4;
            for (int i = 0; i < rowBytes; i++)
                pixelBuffer[dst++] = src[srcRow + i];
        }

        return DecodeBuffer(x, y, cropW, cropH, fmt, out text, out points, out formatId);
    }

    private bool TryDecodeManagedCrop(Color32[] src, int srcW, int srcH,
        int x, int y, int cropW, int cropH,
        out string text, out Vector2[] points, out int formatId)
    {
        text = null; points = null; formatId = 0;

        if (cropW < 32 || cropH < 32) return false;
        if (x < 0 || y < 0 || x + cropW > srcW || y + cropH > srcH) return false;

        EnsureBuffer(cropW, cropH);

        int dst = 0;
        for (int yy = 0; yy < cropH; yy++)
        {
            int row = (y + yy) * srcW + x;
            for (int xx = 0; xx < cropW; xx++)
            {
                var c = src[row + xx];
                pixelBuffer[dst++] = c.r;
                pixelBuffer[dst++] = c.g;
                pixelBuffer[dst++] = c.b;
                pixelBuffer[dst++] = c.a;
            }
        }

        return DecodeBuffer(x, y, cropW, cropH, RGBLuminanceSource.BitmapFormat.RGBA32, out text, out points, out formatId);
    }

    private bool DecodeBuffer(int cropX, int cropY, int w, int h, RGBLuminanceSource.BitmapFormat fmt,
        out string text, out Vector2[] points, out int formatId)
    {
        text = null; points = null; formatId = 0;

        Result res;
        try { res = reader.Decode(pixelBuffer, w, h, fmt); }
        catch { return false; }

        if (res == null || string.IsNullOrWhiteSpace(res.Text)) return false;

        text = res.Text.Trim();
        formatId = (int)res.BarcodeFormat;

        if (res.ResultPoints != null && res.ResultPoints.Length > 0)
        {
            points = new Vector2[res.ResultPoints.Length];
            for (int i = 0; i < res.ResultPoints.Length; i++)
                points[i] = new Vector2(res.ResultPoints[i].X + cropX, res.ResultPoints[i].Y + cropY);
        }

        return true;
    }

#else
    // ✅ Stub IDENTIC: compilează când USE_ZXING e scos
    public ZxingDecoder(bool enableQr, bool enableCode128) { }

    public bool TryDecodeBestEffort(Texture2D tex, out string text, out Vector2[] points, out int formatId)
    {
        text = null;
        points = null;
        formatId = 0;
        return false;
    }
#endif
}