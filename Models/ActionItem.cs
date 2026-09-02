using System;

namespace ClipFlyout.Models;

/// <summary>
/// Executable contextual action available for the detected clipboard payload.
/// </summary>
public record ActionItem(
    string LabelKey,
    string Label,
    string IconKey,
    string Description,
    Action ExecuteAction
);
