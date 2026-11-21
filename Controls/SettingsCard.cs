using System.Windows;
using System.Windows.Controls;

namespace ClipFlyout.Controls;

/// <summary>
/// PowerToys / WinUI 3 inspired SettingsCard implemented as a lightweight ContentControl
/// so child action controls keep full XAML namescope support in parent windows.
/// </summary>
public class SettingsCard : ContentControl
{
    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsCard),
            new PropertyMetadata(string.Empty));

    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    static SettingsCard()
    {
        FocusableProperty.OverrideMetadata(typeof(SettingsCard), new FrameworkPropertyMetadata(false));
    }
}
