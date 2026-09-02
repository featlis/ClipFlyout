using System.Collections.Generic;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace ClipFlyout.Models;

/// <summary>
/// Result of evaluating clipboard payload with relevant actions and display metadata.
/// </summary>
public record DetectionResult(
    ClipDataType Type,
    object RawData,
    string PreviewTitle,
    string PreviewSubtitle,
    string PreviewBody,
    List<ActionItem> AvailableActions,
    string? HexColorCode = null,
    MediaColor? ColorValue = null,
    BitmapSource? ImagePreview = null,
    string? BadgeText = null
);
