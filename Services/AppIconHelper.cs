using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;

namespace ClipFlyout.Services;

/// <summary>
/// Generates refined Windows 11 Fluent 2 layered vector icons for ClipFlyout.
/// </summary>
public static class AppIconHelper
{
    public static Bitmap CreateAppBitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(DrawingColor.Transparent);

        float s = size / 64.0f;

        // 1. Fluent 2 Squircle Base (Deep Azure #0F6CBD -> Refined Indigo #3B5BDB)
        var outerRect = new RectangleF(3f * s, 3f * s, 58f * s, 58f * s);
        using var bgPath = GetRoundedRect(outerRect, 14f * s);
        using var bgBrush = new LinearGradientBrush(
            outerRect,
            DrawingColor.FromArgb(255, 22, 133, 240),
            DrawingColor.FromArgb(255, 59, 91, 219),
            45f);
        g.FillPath(bgBrush, bgPath);

        // Subtle inner top specular gloss
        using var glossPen = new Pen(DrawingColor.FromArgb(72, 255, 255, 255), Math.Max(1f, 1.5f * s));
        using var innerPath = GetRoundedRect(new RectangleF(4f * s, 4f * s, 56f * s, 56f * s), 13f * s);
        g.DrawPath(glossPen, innerPath);

        // 2. Back Flyout Sheet (translucent layered Fluent depth)
        var backCard = new RectangleF(19f * s, 19f * s, 30f * s, 34f * s);
        using var backPath = GetRoundedRect(backCard, 5f * s);
        using var backBrush = new SolidBrush(DrawingColor.FromArgb(85, 255, 255, 255));
        g.FillPath(backBrush, backPath);

        // 3. Front Clipboard Card (crisp frosted white)
        var frontCard = new RectangleF(15f * s, 16f * s, 30f * s, 34f * s);
        using var frontPath = GetRoundedRect(frontCard, 5.5f * s);
        using var frontBrush = new LinearGradientBrush(
            frontCard,
            DrawingColor.FromArgb(252, 255, 255, 255),
            DrawingColor.FromArgb(232, 236, 246, 255),
            90f);
        g.FillPath(frontBrush, frontPath);

        // 4. Rounded Clipboard Top Clip Pill
        var clipRect = new RectangleF(23f * s, 12f * s, 14f * s, 7f * s);
        using var clipPath = GetRoundedRect(clipRect, 3.5f * s);
        using var clipBrush = new SolidBrush(DrawingColor.FromArgb(255, 224, 242, 254));
        g.FillPath(clipBrush, clipPath);
        using var clipStroke = new Pen(DrawingColor.FromArgb(140, 15, 108, 189), Math.Max(1f, 1.2f * s));
        g.DrawPath(clipStroke, clipPath);

        // 5. Fluent Accent Lines inside card
        float lineH = Math.Max(1.8f, 3.2f * s);
        using var accentLineBrush = new SolidBrush(DrawingColor.FromArgb(230, 15, 108, 189));
        using var subtleLineBrush = new SolidBrush(DrawingColor.FromArgb(145, 71, 85, 105));

        using var line1 = GetRoundedRect(new RectangleF(21f * s, 26f * s, 18f * s, lineH), lineH / 2f);
        using var line2 = GetRoundedRect(new RectangleF(21f * s, 33.5f * s, 14f * s, lineH), lineH / 2f);
        using var line3 = GetRoundedRect(new RectangleF(21f * s, 41f * s, 10f * s, lineH), lineH / 2f);
        g.FillPath(accentLineBrush, line1);
        g.FillPath(subtleLineBrush, line2);
        g.FillPath(subtleLineBrush, line3);

        return bmp;
    }

    public static Icon CreateAppIcon(int size = 32)
    {
        using var bmp = CreateAppBitmap(size);
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(hIcon);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            Native.Win32.DestroyIcon(hIcon);
        }
    }

    public static BitmapSource CreateAppBitmapSource(int size = 32)
    {
        using var bmp = CreateAppBitmap(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = ms;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        return bitmapImage;
    }

    public static BitmapSource GetAppIconBitmapSource(int size = 32) => CreateAppBitmapSource(size);

    private static byte[] CreateIcoDibData(Bitmap bmp)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        int xorSize = w * h * 4;
        int andRowBytes = ((w + 31) / 32) * 4;
        int andSize = andRowBytes * h;
        int totalSize = 40 + xorSize + andSize;

        byte[] dib = new byte[totalSize];
        using var ms = new MemoryStream(dib);
        using var bw = new BinaryWriter(ms);

        bw.Write(40);
        bw.Write(w);
        bw.Write(h * 2);
        bw.Write((short)1);
        bw.Write((short)32);
        bw.Write(0);
        bw.Write(xorSize);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);
        bw.Write(0);

        var data = bmp.LockBits(new DrawingRectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] row = new byte[w * 4];
            for (int y = h - 1; y >= 0; y--)
            {
                IntPtr srcRow = data.Scan0 + (y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(srcRow, row, 0, w * 4);
                bw.Write(row);
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }

        bw.Write(new byte[andSize]);
        return dib;
    }

    public static void SaveMultiResolutionIco(string filePath, int[]? sizes = null)
    {
        sizes ??= [16, 24, 32, 48, 64, 128, 256];

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        bw.Write((short)0);
        bw.Write((short)1);
        bw.Write((short)sizes.Length);

        int offset = 6 + (16 * sizes.Length);
        var imgList = new (int size, byte[] data)[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            using var bmp = CreateAppBitmap(sz);
            if (sz >= 256)
            {
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                imgList[i] = (sz, ms.ToArray());
            }
            else
            {
                imgList[i] = (sz, CreateIcoDibData(bmp));
            }
        }

        for (int i = 0; i < imgList.Length; i++)
        {
            var (sz, data) = imgList[i];
            byte w = sz >= 256 ? (byte)0 : (byte)sz;
            byte h = sz >= 256 ? (byte)0 : (byte)sz;

            bw.Write(w);
            bw.Write(h);
            bw.Write((byte)0);
            bw.Write((byte)0);
            bw.Write((short)1);
            bw.Write((short)32);
            bw.Write(data.Length);
            bw.Write(offset);

            offset += data.Length;
        }

        for (int i = 0; i < imgList.Length; i++)
        {
            bw.Write(imgList[i].data);
        }
    }

    private static GraphicsPath GetRoundedRect(RectangleF bounds, float radius)
    {
        float diameter = radius * 2f;
        var size = new SizeF(diameter, diameter);
        var arc = new RectangleF(bounds.Location, size);
        var path = new GraphicsPath();

        if (radius <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
