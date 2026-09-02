using System;

namespace ClipFlyout.Services;

public interface IClipboardMonitor : IDisposable
{
    event EventHandler<object>? ClipboardChanged;
    bool IsEnabled { get; set; }
    void Start();
    void Stop();
    IDisposable SuppressNotifications();
}
