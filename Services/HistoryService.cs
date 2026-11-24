using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClipFlyout.Models;

namespace ClipFlyout.Services;

public class HistoryService
{
    private static readonly Lazy<HistoryService> _instance = new(() => new HistoryService());
    public static HistoryService Instance => _instance.Value;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private const int MaxUnpinnedItems = 20;
    private const int MaxPinnedItems = 20;
    private const int MaxThumbnailEdge = 512;

    private readonly List<HistoryItem> _items = [];
    private readonly object _lock = new();
    private readonly string _historyFilePath;
    private readonly Func<bool> _persistEnabledProvider;

    public event Action? HistoryChanged;

    public HistoryService(string? historyFilePath = null, Func<bool>? persistEnabledProvider = null)
    {
        _persistEnabledProvider = persistEnabledProvider ?? (() => SettingsService.Instance.Current.PersistHistory);
        if (string.IsNullOrWhiteSpace(historyFilePath))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _historyFilePath = Path.Combine(localAppData, "ClipFlyout", "history.json");
        }
        else
        {
            _historyFilePath = Path.GetFullPath(historyFilePath);
        }
    }

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public void Add(DetectionResult result)
    {
        if (result == null) return;

        bool changed = false;
        lock (_lock)
        {
            string snippet = BuildCleanSnippet(result);
            if (string.IsNullOrWhiteSpace(snippet) && result.ImagePreview == null) return;

            // Preserve pinned flag if updating an existing entry
            bool wasPinned = false;
            int existingIdx = _items.FindIndex(i => AreResultsEqual(i.Result, result));
            if (existingIdx >= 0)
            {
                wasPinned = _items[existingIdx].IsPinned;
                _items.RemoveAt(existingIdx);
            }

            // Cap stored image preview AND RawData thumbnail edge to prevent retaining 10x multi-MB 4K bitmaps
            DetectionResult storedResult = result;
            if (result.Type == ClipDataType.Image &&
                result.ImagePreview != null &&
                (result.ImagePreview.PixelWidth > MaxThumbnailEdge || result.ImagePreview.PixelHeight > MaxThumbnailEdge))
            {
                var thumb = CreateDownscaledThumbnail(result.ImagePreview, MaxThumbnailEdge);
                if (thumb != null)
                {
                    storedResult = result with
                    {
                        RawData = result.RawData is BitmapSource ? thumb : result.RawData,
                        ImagePreview = thumb
                    };
                }
            }

            var item = new HistoryItem(
                DateTime.Now,
                storedResult,
                storedResult.PreviewTitle,
                snippet,
                wasPinned
            );

            _items.Add(item);
            SortItemsLocked();
            TrimCapacityLocked();
            changed = true;
        }

        if (changed)
        {
            PersistIfEnabled();
            HistoryChanged?.Invoke();
        }
    }

    public bool TogglePin(DetectionResult target)
    {
        bool newPinState = false;
        bool changed = false;

        lock (_lock)
        {
            int idx = _items.FindIndex(i => AreResultsEqual(i.Result, target));
            if (idx >= 0)
            {
                var item = _items[idx];
                newPinState = !item.IsPinned;
                _items[idx] = item with { IsPinned = newPinState };
                SortItemsLocked();
                TrimCapacityLocked();
                changed = true;
            }
        }

        if (changed)
        {
            PersistIfEnabled();
            HistoryChanged?.Invoke();
        }
        return newPinState;
    }

    public bool IsPinned(DetectionResult? target)
    {
        if (target == null) return false;
        lock (_lock)
        {
            var found = _items.FirstOrDefault(i => AreResultsEqual(i.Result, target));
            return found?.IsPinned == true;
        }
    }

    public DateTime? GetTimestamp(DetectionResult? target)
    {
        if (target == null) return null;
        lock (_lock)
        {
            var found = _items.FirstOrDefault(i => AreResultsEqual(i.Result, target));
            return found?.Timestamp;
        }
    }

    public IReadOnlyList<HistoryItem> GetItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public IReadOnlyList<HistoryItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return GetItems();
        }

        string q = query.Trim();
        lock (_lock)
        {
            return _items.Where(i =>
                i.DisplayTitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                i.DisplaySnippet.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (i.Result.RawData is string s && s.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (i.Result.BadgeText?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();
        }
    }

    public void Remove(HistoryItem item)
    {
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(i => AreResultsEqual(i.Result, item.Result)) > 0;
        }

        if (removed)
        {
            PersistIfEnabled();
            HistoryChanged?.Invoke();
        }
    }

    public bool Remove(DetectionResult target)
    {
        if (target == null) return false;
        bool removed;
        lock (_lock)
        {
            removed = _items.RemoveAll(i => AreResultsEqual(i.Result, target)) > 0;
        }

        if (removed)
        {
            PersistIfEnabled();
            HistoryChanged?.Invoke();
        }
        return removed;
    }

    public string? GetSecondLatestText(string currentText)
    {
        lock (_lock)
        {
            foreach (var item in _items.OrderByDescending(i => i.Timestamp))
            {
                if (item.Result.RawData is string s &&
                    !string.IsNullOrEmpty(s) &&
                    !string.Equals(s, currentText, StringComparison.Ordinal))
                {
                    return s;
                }
            }
            return null;
        }
    }

    public void Clear(bool includePinned = false)
    {
        lock (_lock)
        {
            if (includePinned)
            {
                _items.Clear();
            }
            else
            {
                _items.RemoveAll(i => !i.IsPinned);
            }
        }

        PersistIfEnabled();
        HistoryChanged?.Invoke();
    }

    public void DeletePersistedFile()
    {
        try
        {
            if (File.Exists(_historyFilePath))
            {
                File.Delete(_historyFilePath);
            }
        }
        catch
        {
        }
    }

    public void FlushPersistence() => PersistIfEnabled();

    public void RefreshLocalization(IDataTypeDetector detector)
    {
        bool changed = false;
        lock (_lock)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var reDetected = detector.Detect(item.Result.RawData);
                if (reDetected != null)
                {
                    _items[i] = item with
                    {
                        Result = reDetected,
                        DisplayTitle = reDetected.PreviewTitle,
                        DisplaySnippet = BuildCleanSnippet(reDetected)
                    };
                    changed = true;
                }
            }
        }

        if (changed)
        {
            HistoryChanged?.Invoke();
        }
    }

    public void LoadPersistedItems(IDataTypeDetector detector)
    {
        if (!_persistEnabledProvider()) return;
        if (!File.Exists(_historyFilePath)) return;

        try
        {
            string json = File.ReadAllText(_historyFilePath);
            var entries = JsonSerializer.Deserialize<List<PersistedHistoryEntry>>(json);
            if (entries == null || entries.Count == 0) return;

            lock (_lock)
            {
                foreach (var entry in entries)
                {
                    object? payload = entry.Files is { Length: > 0 }
                        ? entry.Files
                        : (!string.IsNullOrWhiteSpace(entry.Text) ? entry.Text : null);
                    if (payload == null) continue;

                    var detected = detector.Detect(payload);
                    if (detected == null) continue;

                    string snippet = BuildCleanSnippet(detected);
                    var item = new HistoryItem(entry.Timestamp, detected, detected.PreviewTitle, snippet, entry.IsPinned);
                    if (!_items.Any(i => AreResultsEqual(i.Result, detected)))
                    {
                        _items.Add(item);
                    }
                }
                SortItemsLocked();
                TrimCapacityLocked();
            }

            HistoryChanged?.Invoke();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load persisted history: {ex.Message}");
        }
    }

    private void PersistIfEnabled()
    {
        if (!_persistEnabledProvider()) return;

        try
        {
            List<PersistedHistoryEntry> snapshot;
            lock (_lock)
            {
                snapshot = _items
                    .Where(i => (i.Result.RawData is string s && s.Length <= 64 * 1024) ||
                                (i.Result.RawData is string[] arr && arr.Length > 0))
                    .Select(i => new PersistedHistoryEntry(
                        i.Timestamp,
                        i.Result.RawData as string,
                        i.IsPinned,
                        i.Result.RawData as string[]))
                    .ToList();
            }

            string? dir = Path.GetDirectoryName(_historyFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = _historyFilePath + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, SerializerOptions));
            File.Move(tmp, _historyFilePath, true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to persist history: {ex.Message}");
        }
    }

    private void SortItemsLocked()
    {
        _items.Sort((a, b) =>
        {
            if (a.IsPinned != b.IsPinned) return b.IsPinned.CompareTo(a.IsPinned);
            return b.Timestamp.CompareTo(a.Timestamp);
        });
    }

    private void TrimCapacityLocked()
    {
        int pinnedCount = _items.Count(i => i.IsPinned);
        while (pinnedCount > MaxPinnedItems)
        {
            int lastPinned = _items.FindLastIndex(i => i.IsPinned);
            if (lastPinned < 0) break;
            _items.RemoveAt(lastPinned);
            pinnedCount--;
        }

        int unpinnedCount = _items.Count(i => !i.IsPinned);
        while (unpinnedCount > MaxUnpinnedItems)
        {
            int lastUnpinned = _items.FindLastIndex(i => !i.IsPinned);
            if (lastUnpinned < 0) break;
            _items.RemoveAt(lastUnpinned);
            unpinnedCount--;
        }
    }

    internal static string BuildCleanSnippet(DetectionResult result)
    {
        if (result.Type == ClipDataType.Image || result.RawData is BitmapSource)
        {
            var bmp = (result.RawData as BitmapSource) ?? result.ImagePreview;
            return bmp != null
                ? $"{bmp.PixelWidth} × {bmp.PixelHeight} px"
                : result.PreviewSubtitle;
        }

        if (result.Type == ClipDataType.FileDrop && result.RawData is string[] files && files.Length > 0)
        {
            string first = Path.GetFileName(files[0].TrimEnd('\\', '/'));
            if (string.IsNullOrEmpty(first)) first = files[0];
            return files.Length > 1 ? $"{first} (+{files.Length - 1})" : first;
        }

        string rawStr = result.RawData?.ToString() ?? string.Empty;
        string singleLine = NormalizeSingleLine(rawStr);
        if (singleLine.Length > 48)
        {
            return singleLine[..48] + "…";
        }
        return singleLine;
    }

    internal static string NormalizeSingleLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        // Scan only up to 256 chars so huge multi-MB strings are O(1)
        int limit = Math.Min(text.Length, 256);
        Span<char> buf = stackalloc char[limit];
        int pos = 0;
        bool prevSpace = true;

        for (int i = 0; i < limit; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!prevSpace)
                {
                    buf[pos++] = ' ';
                    prevSpace = true;
                }
            }
            else
            {
                buf[pos++] = c;
                prevSpace = false;
            }
        }

        return new string(buf[..pos]).TrimEnd();
    }

    private static BitmapSource? CreateDownscaledThumbnail(BitmapSource source, int maxEdge)
    {
        try
        {
            double scale = Math.Min((double)maxEdge / source.PixelWidth, (double)maxEdge / source.PixelHeight);
            if (scale >= 1.0) return source;

            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            var frozen = BitmapFrame.Create(transformed);
            if (frozen.CanFreeze) frozen.Freeze();
            return frozen;
        }
        catch
        {
            return source;
        }
    }

    public static bool AreResultsEqual(DetectionResult a, DetectionResult b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Type != b.Type) return false;

        if (a.Type == ClipDataType.FileDrop && a.RawData is string[] filesA && b.RawData is string[] filesB)
        {
            return filesA.SequenceEqual(filesB, StringComparer.OrdinalIgnoreCase);
        }

        if (a.Type == ClipDataType.Image || a.RawData is BitmapSource || b.RawData is BitmapSource)
        {
            var bmpA = (a.RawData as BitmapSource) ?? a.ImagePreview;
            var bmpB = (b.RawData as BitmapSource) ?? b.ImagePreview;
            if (bmpA == null || bmpB == null) return bmpA == bmpB;
            if (ReferenceEquals(bmpA, bmpB)) return true;

            if (bmpA.PixelWidth != bmpB.PixelWidth ||
                bmpA.PixelHeight != bmpB.PixelHeight ||
                bmpA.Format != bmpB.Format)
            {
                return false;
            }

            try
            {
                long strideLong = ((long)bmpA.PixelWidth * bmpA.Format.BitsPerPixel + 7) / 8;
                if (strideLong <= 0 || strideLong > int.MaxValue) return false;
                int stride = (int)strideLong;
                long totalBytes = strideLong * bmpA.PixelHeight;

                if (totalBytes <= 64 * 1024)
                {
                    byte[] bytesA = new byte[totalBytes];
                    byte[] bytesB = new byte[totalBytes];
                    bmpA.CopyPixels(bytesA, stride, 0);
                    bmpB.CopyPixels(bytesB, stride, 0);
                    return bytesA.AsSpan().SequenceEqual(bytesB.AsSpan());
                }

                // Sample 13 evenly distributed rows (odd count guarantees exact center row + quarters)
                int rowCount = Math.Min(13, bmpA.PixelHeight);
                var sampledRows = new HashSet<int>();
                for (int i = 0; i < rowCount; i++)
                {
                    sampledRows.Add((int)((long)i * (bmpA.PixelHeight - 1) / Math.Max(1, rowCount - 1)));
                }
                sampledRows.Add(bmpA.PixelHeight / 2);

                byte[] rowA = new byte[stride];
                byte[] rowB = new byte[stride];

                foreach (int y in sampledRows)
                {
                    var rect = new Int32Rect(0, y, bmpA.PixelWidth, 1);
                    bmpA.CopyPixels(rect, rowA, stride, 0);
                    bmpB.CopyPixels(rect, rowB, stride, 0);
                    if (!rowA.AsSpan().SequenceEqual(rowB.AsSpan()))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        return Equals(a.RawData, b.RawData);
    }

    private sealed record PersistedHistoryEntry(DateTime Timestamp, string? Text, bool IsPinned, string[]? Files = null);
}
