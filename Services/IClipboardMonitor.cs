using System;

namespace ClipFlyout.Services;

public interface IClipboardMonitor : IDisposable
{
    event EventHandler<object>? ClipboardChanged;
    bool IsEnabled { get; set; }
    bool IsPaused => false;
    DateTimeOffset? PauseUntilUtc => null;
    void PauseFor(TimeSpan duration) { }
    void Resume() { }
    event Action? PauseStateChanged { add { } remove { } }
    void Start();
    void Stop();
    IDisposable SuppressNotifications();
}
