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
    Email,
    Code,
    Image,
    UnixTimestamp,
    Base64,
    TableData
}
