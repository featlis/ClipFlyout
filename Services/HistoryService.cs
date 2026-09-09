using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;

namespace ClipFlyout.Services;

public class HistoryService
{
    private static readonly Lazy<HistoryService> _instance = new(() => new HistoryService());
    public static HistoryService Instance => _instance.Value;

    private const int MaxItems = 10;
    private readonly List<HistoryItem> _items = [];
    private readonly object _lock = new();

    public event Action? HistoryChanged;

    public void Add(DetectionResult result)
    {
        if (result == null) return;

        lock (_lock)
        {
            string rawStr = result.RawData?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawStr) && result.ImagePreview == null) return;

            // Remove older duplicate if exists
            _items.RemoveAll(i => AreResultsEqual(i.Result, result));

            string snippet = rawStr.Trim();
            if (snippet.Length > 40)
            {
                snippet = snippet[..40] + "…";
            }

            var item = new HistoryItem(
                DateTime.Now,
                result,
                result.PreviewTitle,
                snippet
            );

            _items.Insert(0, item);

            while (_items.Count > MaxItems)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        HistoryChanged?.Invoke();
    }

    public IReadOnlyList<HistoryItem> GetItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
        }
        HistoryChanged?.Invoke();
    }

    private static bool AreResultsEqual(DetectionResult a, DetectionResult b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Type != b.Type) return false;

        if (a.Type == ClipDataType.Image || a.RawData is BitmapSource || b.RawData is BitmapSource)
        {
            var bmpA = (a.RawData as BitmapSource) ?? a.ImagePreview;
            var bmpB = (b.RawData as BitmapSource) ?? b.ImagePreview;
            if (bmpA == null || bmpB == null) return bmpA == bmpB;

            if (bmpA.PixelWidth != bmpB.PixelWidth ||
                bmpA.PixelHeight != bmpB.PixelHeight ||
                bmpA.Format != bmpB.Format)
            {
                return false;
            }

            try
            {
                int stride = (bmpA.PixelWidth * bmpA.Format.BitsPerPixel + 7) / 8;
                int totalBytes = stride * bmpA.PixelHeight;
                byte[] bytesA = new byte[totalBytes];
                byte[] bytesB = new byte[totalBytes];
                bmpA.CopyPixels(bytesA, stride, 0);
                bmpB.CopyPixels(bytesB, stride, 0);
                return bytesA.AsSpan().SequenceEqual(bytesB.AsSpan());
            }
            catch
            {
                return false;
            }
        }

        return Equals(a.RawData, b.RawData);
    }
}
