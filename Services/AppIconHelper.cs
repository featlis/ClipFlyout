using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace ClipFlyout.Services;

/// <summary>
/// Provides vector-rendered icons and multi-resolution icon assets based on the
/// signature ClipFlyout blue-to-purple gradient clipboard design.
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

        float scale = size / 32.0f;

        // Background gradient tile (Blue #3B82F6 to Purple #9333EA)
        using var bgBrush = new LinearGradientBrush(
            new DrawingRectangle(0, 0, size, size),
            DrawingColor.FromArgb(59, 130, 246),
            DrawingColor.FromArgb(147, 51, 234),
            45f
        );

        float cornerRadius = 6f * scale;
        float padding = 2f * scale;
        using var path = GetRoundedRect(new RectangleF(padding, padding, size - 2 * padding, size - 2 * padding), cornerRadius);
        g.FillPath(bgBrush, path);

        // Clipboard Board Outline
        float penWidth = Math.Max(1.5f, 2f * scale);
        using var pen = new Pen(DrawingColor.White, penWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawRectangle(pen, 9 * scale, 10 * scale, 14 * scale, 15 * scale);

        // Clipboard Top Clip
        using var clipBrush = new SolidBrush(DrawingColor.White);
        g.FillRectangle(clipBrush, 12 * scale, 7 * scale, 8 * scale, 4 * scale);

        // Document Text Lines
        float linePenWidth = Math.Max(1.2f, 1.5f * scale);
        using var linePen = new Pen(DrawingColor.FromArgb(220, 255, 255, 255), linePenWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        g.DrawLine(linePen, 12 * scale, 14 * scale, 20 * scale, 14 * scale);
        g.DrawLine(linePen, 12 * scale, 18 * scale, 18 * scale, 18 * scale);

        return bmp;
    }

    public static Icon CreateAppIcon(int size = 32)
    {
        using var bmp = CreateAppBitmap(size);
        IntPtr hIcon = bmp.GetHicon();
        return (Icon)Icon.FromHandle(hIcon).Clone();
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

    public static void SaveMultiResolutionIco(string filePath, int[]? sizes = null)
    {
        sizes ??= [16, 24, 32, 48, 64, 128, 256];

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // 1. ICONDIR header
        bw.Write((short)0);      // Reserved
        bw.Write((short)1);      // Type 1 = ICO
        bw.Write((short)sizes.Length); // Image count

        int offset = 6 + (16 * sizes.Length);
        var pngDataList = new (int size, byte[] data)[sizes.Length];

        for (int i = 0; i < sizes.Length; i++)
        {
            int sz = sizes[i];
            using var bmp = CreateAppBitmap(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngDataList[i] = (sz, ms.ToArray());
        }

        // 2. ICONDIRENTRY entries
        for (int i = 0; i < pngDataList.Length; i++)
        {
            var (sz, data) = pngDataList[i];
            byte w = sz >= 256 ? (byte)0 : (byte)sz;
            byte h = sz >= 256 ? (byte)0 : (byte)sz;

            bw.Write(w);               // Width
            bw.Write(h);               // Height
            bw.Write((byte)0);          // ColorCount
            bw.Write((byte)0);          // Reserved
            bw.Write((short)1);         // Planes
            bw.Write((short)32);        // BitCount
            bw.Write((int)data.Length); // BytesInRes
            bw.Write((int)offset);      // ImageOffset

            offset += data.Length;
        }

        // 3. Image data (PNG blocks)
        for (int i = 0; i < pngDataList.Length; i++)
        {
            bw.Write(pngDataList[i].data);
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
