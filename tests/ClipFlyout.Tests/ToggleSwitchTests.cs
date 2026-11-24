using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using ClipFlyout.Controls;
using Xunit;

namespace ClipFlyout.Tests;

public class ToggleSwitchTests
{
    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { ex = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex != null) throw new Exception("STA test failed", ex);
    }

    [Fact]
    public void ToggleSwitch_MouseDownAndUpOnControl_TogglesAndReleasesCapture()
    {
        RunOnSta(() =>
        {
            var sw = new ToggleSwitch { IsOn = false };
            sw.Measure(new Size(44, 24));
            sw.Arrange(new Rect(0, 0, 44, 24));
            sw.UpdateLayout();

            int toggledCount = 0;
            sw.Toggled += (_, _) => toggledCount++;

            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = sw
            };
            var up = new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = sw
            };

            sw.RaiseEvent(down);
            sw.RaiseEvent(up);

            Assert.True(sw.IsOn);
            Assert.Equal(1, toggledCount);
            Assert.False(sw.IsMouseCaptured);

            // Second click toggles back to false cleanly
            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = sw
            });
            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 3, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = sw
            });

            Assert.False(sw.IsOn);
            Assert.Equal(2, toggledCount);
            Assert.False(sw.IsMouseCaptured);
        });
    }

    [Fact]
    public void ToggleSwitch_LostMouseCapture_ResetsPressedStateWithoutToggling()
    {
        RunOnSta(() =>
        {
            var sw = new ToggleSwitch { IsOn = false };
            sw.Measure(new Size(44, 24));
            sw.Arrange(new Rect(0, 0, 44, 24));
            sw.UpdateLayout();

            int toggledCount = 0;
            sw.Toggled += (_, _) => toggledCount++;

            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = sw
            });
            sw.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 1)
            {
                RoutedEvent = UIElement.LostMouseCaptureEvent,
                Source = sw
            });
            // Subsequent stray MouseUp must NOT toggle
            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 2, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = sw
            });

            Assert.False(sw.IsOn);
            Assert.Equal(0, toggledCount);
        });
    }

    [Fact]
    public void ToggleSwitch_WhenDisabled_IgnoresClicks()
    {
        RunOnSta(() =>
        {
            var sw = new ToggleSwitch { IsOn = true, IsEnabled = false };
            sw.Measure(new Size(44, 24));
            sw.Arrange(new Rect(0, 0, 44, 24));
            sw.UpdateLayout();

            int toggledCount = 0;
            sw.Toggled += (_, _) => toggledCount++;

            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                Source = sw
            });
            sw.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
            {
                RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                Source = sw
            });

            Assert.True(sw.IsOn);
            Assert.Equal(0, toggledCount);
        });
    }
}
