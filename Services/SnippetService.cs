using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using ClipFlyout.Models;

namespace ClipFlyout.Services;

/// <summary>
/// Thread-safe persistent CRUD storage for user-defined permanent snippets (templates).
/// Stored atomically in %LocalAppData%\ClipFlyout\snippets.json.
/// </summary>
public class SnippetService
{
    private const int MaxSnippets = 200;
    private static readonly Lazy<SnippetService> _instance = new(() => new SnippetService());
    public static SnippetService Instance => _instance.Value;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly object _lock = new();
    private readonly string _snippetsFilePath;
    private readonly List<SnippetItem> _items = new();

    public event Action? SnippetsChanged;

    public SnippetService(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _snippetsFilePath = Path.Combine(localAppData, "ClipFlyout", "snippets.json");
        }
        else
        {
            _snippetsFilePath = Path.GetFullPath(filePath);
        }

        LoadLocked();
    }

    public int Count
    {
        get
        {
            lock (_lock) return _items.Count;
        }
    }

    public IReadOnlyList<SnippetItem> GetItems()
    {
        lock (_lock)
        {
            return _items.ToList();
        }
    }

    public IReadOnlyList<SnippetItem> Search(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return GetItems();
        string q = query.Trim();

        lock (_lock)
        {
            return _items
                .Where(s => s.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            s.Content.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public SnippetItem AddOrUpdate(string title, string content, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        string trimmedContent = content;
        if (string.IsNullOrEmpty(trimmedContent))
        {
            throw new ArgumentException("Snippet content cannot be empty.", nameof(content));
        }

        string normalizedTitle = string.IsNullOrWhiteSpace(title)
            ? BuildDefaultTitle(trimmedContent)
            : title.Trim();

        SnippetItem saved;
        lock (_lock)
        {
            string targetId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id.Trim();
            int existingIdx = _items.FindIndex(s => string.Equals(s.Id, targetId, StringComparison.Ordinal));
            saved = new SnippetItem(targetId, normalizedTitle, trimmedContent, DateTime.Now);

            if (existingIdx >= 0)
            {
                _items.RemoveAt(existingIdx);
            }
            _items.Insert(0, saved);

            if (_items.Count > MaxSnippets)
            {
                _items.RemoveRange(MaxSnippets, _items.Count - MaxSnippets);
            }

            PersistLocked();
        }

        SnippetsChanged?.Invoke();
        return saved;
    }

    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;

        bool removed;
        lock (_lock)
        {
            int removedCount = _items.RemoveAll(s => string.Equals(s.Id, id, StringComparison.Ordinal));
            removed = removedCount > 0;
            if (removed)
            {
                PersistLocked();
            }
        }

        if (removed)
        {
            SnippetsChanged?.Invoke();
        }
        return removed;
    }

    public void ReloadFromDisk()
    {
        lock (_lock)
        {
            _items.Clear();
            LoadLocked();
        }
        SnippetsChanged?.Invoke();
    }

    public static string BuildDefaultTitle(string content)
    {
        string single = HistoryService.NormalizeSingleLine(content).Trim();
        return single.Length > 36 ? single[..36] + "…" : single;
    }

    private void LoadLocked()
    {
        try
        {
            if (!File.Exists(_snippetsFilePath)) return;
            string json = File.ReadAllText(_snippetsFilePath);
            var loaded = JsonSerializer.Deserialize<List<SnippetItem>>(json, SerializerOptions);
            if (loaded == null) return;

            _items.AddRange(loaded
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrEmpty(s.Content))
                .OrderByDescending(s => s.UpdatedAt)
                .Take(MaxSnippets));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load snippets: {ex.Message}");
        }
    }

    private void PersistLocked()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_snippetsFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = _snippetsFilePath + $".{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, SerializerOptions));
            File.Move(tmp, _snippetsFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to persist snippets: {ex.Message}");
        }
    }
}
