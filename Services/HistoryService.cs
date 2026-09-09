using System;
using System.Collections.Generic;
using System.Linq;
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
            _items.RemoveAll(i => Equals(i.Result.RawData, result.RawData));

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
}
