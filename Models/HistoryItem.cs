using System;

namespace ClipFlyout.Models;

public record HistoryItem(
    DateTime Timestamp,
    DetectionResult Result,
    string DisplayTitle,
    string DisplaySnippet
);
