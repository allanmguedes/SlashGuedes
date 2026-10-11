using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using SlashText.Design;
using SlashText.Services;

namespace SlashText.Views;

public sealed class ShortcutCodeBlockWindow : Window
{
    internal Border Surface { get; }
    internal ComboBox LanguageBox { get; }
    internal TextEditor CodeEditor { get; }
    internal Button SaveButton { get; }
    internal Button CancelButton { get; }
    private readonly TextBlock _error;
    public CodeBlockContent? Result { get; private set; }

    public ShortcutCodeBlockWindow(CodeBlockContent? initial = null)
    {
        Title = initial is null ? UiText.Get("Inserir bloco de código") : UiText.Get("Editar bloco de código");
        Width = 760; Height = 570; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = Brushes.Transparent; AllowsTransparency = true; ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(24) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto }) root.RowDefinitions.Add(new RowDefinition { Height = height });
        var header = new DockPanel();
        var close = ActionButton(UiText.Get("Fechar"), "X", false); close.Content = new LabIcon { Kind = "X", Width = 16, Height = 16 }; close.Width = 32; close.Height = 32; close.Padding = new Thickness(6); close.Click += (_, _) => Close(); DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        header.Children.Add(Label(Title, 21, true)); root.Children.Add(header);
        var metadata = new StackPanel { Margin = new Thickness(0, 20, 0, 14) };
        metadata.Children.Add(Label(UiText.Get("Linguagem"), 12));
        LanguageBox = new ComboBox { Margin = new Thickness(0, 7, 0, 0), MaxDropDownHeight = 320 };
        LanguageBox.SetResourceReference(StyleProperty, "Lab.Combo"); AutomationProperties.SetName(LanguageBox, UiText.Get("Linguagem do código"));
        var id = ShortcutCodeEditor.NormalizeLanguage(initial?.Language ?? "text");
        foreach (var item in ShortcutCodeEditor.Languages) LanguageBox.Items.Add(new ComboBoxItem { Content = item.Label, Tag = item.Id });
        if (!ShortcutCodeEditor.Languages.Any(item => item.Id == id)) LanguageBox.Items.Add(new ComboBoxItem { Content = id + UiText.Get(" (texto sem realce)"), Tag = id });
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>().Single(item => Equals(item.Tag, id));
        metadata.Children.Add(LanguageBox); Grid.SetRow(metadata, 1); root.Children.Add(metadata);
        CodeEditor = ShortcutCodeEditor.Create(readOnly: false); CodeEditor.Text = initial?.Code ?? "";
        AutomationProperties.SetName(CodeEditor, UiText.Get("Conteúdo do bloco de código")); ShortcutCodeEditor.BindLanguage(CodeEditor, () => (string)((ComboBoxItem)LanguageBox.SelectedItem).Tag);
        var editorSurface = new Border { Child = CodeEditor, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), ClipToBounds = true };
        editorSurface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong"); Grid.SetRow(editorSurface, 2); root.Children.Add(editorSurface);
        var hint = new StackPanel { Margin = new Thickness(0, 12, 0, 16) };
        var info = Label(UiText.Get("O código fica literal, incluindo {{variáveis}}. Salve o atalho para gravar o bloco."), 12); info.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted"); hint.Children.Add(info);
        _error = Label("", 12); _error.SetResourceReference(TextBlock.ForegroundProperty, "Lab.error"); _error.Visibility = Visibility.Collapsed; hint.Children.Add(_error); Grid.SetRow(hint, 3); root.Children.Add(hint);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        CancelButton = ActionButton(UiText.Get("Cancelar"), "X", false); CancelButton.Margin = new Thickness(0, 0, 8, 0); CancelButton.Click += (_, _) => Close(); footer.Children.Add(CancelButton);
        SaveButton = ActionButton(UiText.Get("Salvar bloco"), "Check", true); SaveButton.Click += (_, _) => Save(); footer.Children.Add(SaveButton); Grid.SetRow(footer, 4); root.Children.Add(footer);
        Surface = new Border { Child = root, Margin = new Thickness(8), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        Surface.SetResourceReference(Border.BackgroundProperty, "Lab.panel"); Surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line-strong"); Content = Surface;
        LabMotion.SetEntrance(Surface, "Popup"); Loaded += (_, _) => { LabMotion.PlayEntrance(Surface); CodeEditor.Focus(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Close(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { e.Handled = true; Save(); }
        };
    }
    internal void EnableBackdrop() => ModalBackdrop.Attach(this, Surface, Close);
    private void Save()
    {
        if (CodeEditor.Text.Length == 0) { _error.Text = UiText.Get("Insira o código antes de salvar o bloco."); _error.Visibility = Visibility.Visible; CodeEditor.Focus(); return; }
        Result = new CodeBlockContent((string)((ComboBoxItem)LanguageBox.SelectedItem).Tag, CodeBlockMarkdown.Normalize(CodeEditor.Text)); DialogResult = true;
    }
    private static TextBlock Label(string text, double size, bool bold = false)
    {
        var label = new TextBlock { Text = text, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text"); return label;
    }
    private static Button ActionButton(string text, string icon, bool primary)
    {
        var button = new Button(); button.SetResourceReference(StyleProperty, primary ? "Lab.Pilot.PrimaryButton" : "Lab.Button"); AutomationProperties.SetName(button, text);
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        var glyph = new LabIcon { Kind = icon, Width = 16, Height = 16, Margin = new Thickness(0, 0, 7, 0) }; glyph.SetBinding(LabIcon.ForegroundProperty, new Binding("Foreground") { Source = button }); content.Children.Add(glyph);
        var label = Label(text, 13); label.SetBinding(TextBlock.ForegroundProperty, new Binding("Foreground") { Source = button }); content.Children.Add(label); button.Content = content; return button;
    }
}
