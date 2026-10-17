using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Explicit Lab styles for the legacy, programmatically constructed auxiliary windows.</summary>
internal static class AuxiliaryWindowTheme
{
    private static readonly Dictionary<string, string> Brushes = new()
    {
        ["CanvasBrush"] = "Lab.bg", ["SurfaceBrush"] = "Lab.panel", ["PanelBrush"] = "Lab.panel",
        ["ChromeBrush"] = "Lab.shell", ["InputBrush"] = "Lab.input", ["InkBrush"] = "Lab.text",
        ["MutedBrush"] = "Lab.muted", ["DividerBrush"] = "Lab.line", ["AccentBrush"] = "Lab.accent-text",
        ["AccentStrongBrush"] = "Lab.accent-text", ["SelectedBrush"] = "Lab.tint", ["DangerBrush"] = "Lab.error"
    };

    internal static void Apply(Window window)
    {
        ApplyTree(window.Content as DependencyObject);
        // Some windows build or replace their content after Apply() is called.
        // Apply on Loaded too, when the complete logical tree exists.
        window.Loaded += (_, _) => ApplyTree(window.Content as DependencyObject);
        if (!window.AllowsTransparency) window.SetResourceReference(Window.BackgroundProperty, "Lab.bg");
        window.SetResourceReference(Window.ForegroundProperty, "Lab.text");
        window.SetResourceReference(Window.FontFamilyProperty, "Lab.Font");
        window.UseLayoutRounding = window.SnapsToDevicePixels = true;
        window.SourceInitialized += (_, _) => ThemeService.ApplyToWindow(window);
        if (window is not UpdateProgressWindow && window is not RecordingControlWindow && window is not GifPreviewWindow)
            window.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; window.Close(); } };
    }

    internal static void ApplyTree(DependencyObject? root)
    {
        if (root is null) return;
        if (root is FrameworkElement element)
        {
            if (element is Button button && (button.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue ||
                ReferenceEquals(button.Style, Application.Current.TryFindResource("PrimaryButton"))))
                button.SetResourceReference(FrameworkElement.StyleProperty, ReferenceEquals(button.Style, Application.Current.TryFindResource("PrimaryButton")) ? "Lab.PrimaryButton" : "Lab.Button");
            if (element is TextBox box && box.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                box.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Field");
            if (element is ComboBox combo && combo.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                combo.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Combo");
            if (element is ComboBoxItem option && option.ReadLocalValue(FrameworkElement.StyleProperty) == DependencyProperty.UnsetValue)
                option.SetResourceReference(FrameworkElement.StyleProperty, "Lab.ComboItem");
            if (element is PasswordBox password && password.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue)
            {
                password.SetResourceReference(Control.ForegroundProperty, "Lab.text");
                password.SetResourceReference(Control.BackgroundProperty, "Lab.input");
                password.SetResourceReference(Control.BorderBrushProperty, "Lab.line");
                password.SetResourceReference(PasswordBox.CaretBrushProperty, "Lab.text");
            }
            if (element is RichTextBox rich && rich.ReadLocalValue(Control.ForegroundProperty) == DependencyProperty.UnsetValue)
            {
                rich.SetResourceReference(Control.ForegroundProperty, "Lab.text");
                rich.SetResourceReference(RichTextBox.CaretBrushProperty, "Lab.text");
            }
            if (element is TextBlock text) text.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Text");
            if (element is Border border && ReferenceEquals(border.Style, Application.Current.TryFindResource("SettingsCard")))
                border.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Card");
            if (element is Control control) { Rebind(control, Control.BackgroundProperty); Rebind(control, Control.ForegroundProperty); Rebind(control, Control.BorderBrushProperty); }
            if (element is Border surface) { Rebind(surface, Border.BackgroundProperty); Rebind(surface, Border.BorderBrushProperty); }
            if (element is Panel panel) Rebind(panel, Panel.BackgroundProperty);
            if (element is TextBlock label) Rebind(label, TextBlock.ForegroundProperty);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) ApplyTree(child);
    }

    private static void Rebind(FrameworkElement element, DependencyProperty property)
    {
        var current = element.GetValue(property);
        foreach (var pair in Brushes)
            if (ReferenceEquals(current, Application.Current.TryFindResource(pair.Key)))
            { element.SetResourceReference(property, pair.Value); return; }
    }

    // Used by the Windows UI smoke test against actual bound brushes, not
    // screenshots or static hex codes. WCAG contrast >= 4.5 for normal text.
    // Templates provide independent TextBlock glyphs for ordinary TextBoxes.
    // Check exactly what the user sees, not the transparent native foreground.
    internal static TextBlock? GetReadableText(TextBox box)
    {
        box.ApplyTemplate();
        return box.Template.FindName("ReadableGlyphs", box) as TextBlock;
    }

    internal static bool HasReadablePresentation(TextBox box)
        => GetReadableText(box) is { Visibility: Visibility.Visible, IsHitTestVisible: false };

    internal static bool TryGetVisibleText(TextBox box, out string visible)
    {
        if (GetReadableText(box) is { Visibility: Visibility.Visible } presentation)
        {
            visible = presentation.Text;
            return true;
        }
        visible = string.Empty;
        return false;
    }

    internal static void RequireReadable(Control control, string name)
    {
        if (control is TextBox box &&
            GetReadableText(box) is { Visibility: Visibility.Visible } actual)
            RequireReadableBrushes(actual.Foreground, control.Background, name);
        else
            RequireReadableBrushes(control.Foreground, control.Background, name);
    }

    internal static void RequireReadableText(TextBlock text, string name, string backgroundKey) =>
        RequireReadableBrushes(text.Foreground,
            Application.Current.TryFindResource(backgroundKey) as Brush, name);

    internal static void RequireReadableSurface(Brush? foreground, Brush? background, string name) =>
        RequireReadableBrushes(foreground, background, name);

    private static void RequireReadableBrushes(Brush? foreground, Brush? background, string name)
    {
        if (foreground is not SolidColorBrush ink ||
            background is not SolidColorBrush surface)
            throw new InvalidOperationException(name + ": cores de entrada não foram resolvidas.");
        static double Luminance(Color color)
        {
            static double Channel(byte byteValue)
            {
                var value = byteValue / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(color.R) +
                   0.7152 * Channel(color.G) +
                   0.0722 * Channel(color.B);
        }
        var a = Luminance(ink.Color);
        var b = Luminance(surface.Color);
        var ratio = (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
        if (ratio < 4.5)
            throw new InvalidOperationException(name + ": contraste insuficiente para leitura (" +
                ratio.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + ":1).");
    }

    internal static MessageBoxResult ShowMessage(Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.Information)
    {
        var dialog = new SettingsDialogWindow(message, title, buttons, image);
        if (owner?.IsVisible == true) { dialog.Owner = owner; dialog.EnableBackdrop(); }
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (owner is not null) LabMotion.SetReduced(dialog, LabMotion.GetReduced(owner));
        dialog.ShowDialog(); return dialog.Choice;
    }
}
