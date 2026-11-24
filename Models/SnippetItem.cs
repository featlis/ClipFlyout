using System;

namespace ClipFlyout.Models;

/// <summary>
/// User-saved permanent text snippet accessible from the flyout and system tray.
/// </summary>
public record SnippetItem(
    string Id,
    string Title,
    string Content,
    DateTime UpdatedAt
);
