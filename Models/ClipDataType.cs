namespace ClipFlyout.Models;

/// <summary>
/// Detected clipboard content classification.
/// </summary>
public enum ClipDataType
{
    PlainText,
    HexColor,
    Json,
    Url,
    Code,
    Image
}
