using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SlashText.Services;

namespace SlashText.Views;

/// <summary>Only native WPF elements enter the document: restrictive WPF undo can restore them.
/// Raw source is stored as an opaque string on the native Border, independently of rendered runs.</summary>
internal static class ShortcutCodeBlockView
{
    private const string Prefix = "SlashDesk.Code:";
    private sealed record State(string Language, string Code, bool Editable);
    private sealed class Attached { internal string? Theme; internal Action<Border>? Edit; internal Action<string>? CopyForEvidence; }
    private static readonly ConditionalWeakTable<Border, Attached> Attachments = new();
    internal static readonly RoutedEvent EditRequestedEvent = EventManager.RegisterRoutedEvent("ShortcutCodeEditRequested", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Border));

    internal static Border Create(CodeBlockContent content, bool editable = true)
    {
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        var header = new DockPanel { Margin = new Thickness(10, 5, 10, 5), LastChildFill = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        actions.Children.Add(Tool("CodeCollapse", UiText.Get("Recolher código"), "ChevronUp"));
        actions.Children.Add(Tool("CodeCopy", UiText.Get("Copiar código"), "Copy"));
        var edit = Tool("CodeEdit", UiText.Get("Editar bloco de código"), "PenLine"); edit.Visibility = editable ? Visibility.Visible : Visibility.Collapsed; actions.Children.Add(edit);
        var title = new TextBlock { Text = ShortcutCodeEditor.Label(content.Language), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }; title.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        header.Children.Add(title);
        var surface = new Border { Child = header, BorderThickness = new Thickness(0, 0, 0, 1) }; surface.SetResourceReference(Border.BackgroundProperty, "Lab.raised"); surface.SetResourceReference(Border.BorderBrushProperty, "Lab.line"); layout.Children.Add(surface);
        var text = new TextBlock { Name = "CodeSource", FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(10, 8, 10, 8) }; text.SetResourceReference(TextBlock.ForegroundProperty, "Lab.text");
        var numbers = new TextBlock { Text = string.Join("\n", Enumerable.Range(1, content.Code.Count(c => c == '\n') + 1)), FontFamily = new FontFamily("Consolas"), FontSize = 13, Padding = new Thickness(8, 8, 0, 8), TextAlignment = TextAlignment.Right }; numbers.SetResourceReference(TextBlock.ForegroundProperty, "Lab.muted");
        var code = new Grid(); code.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); code.ColumnDefinitions.Add(new ColumnDefinition()); code.Children.Add(numbers); Grid.SetColumn(text, 1); code.Children.Add(text);
        var scroll = new ScrollViewer { Name = "CodeScroll", Content = code, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = Math.Clamp((content.Code.Count(c => c == '\n') + 1) * 19 + 24, 70, 230) };
        Grid.SetRow(scroll, 1); layout.Children.Add(scroll);
        var root = new Border { Child = layout, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1), ClipToBounds = true, Tag = Prefix + JsonSerializer.Serialize(new State(content.Language, content.Code, editable)) };
        root.SetResourceReference(Border.BorderBrushProperty, "Lab.line"); root.SetResourceReference(Border.BackgroundProperty, "Lab.input"); AutomationProperties.SetName(root, UiText.Get("Bloco de código ") + title.Text);
        Attach(root); return root;
    }
    internal static BlockUIContainer CreateBlock(CodeBlockContent content, bool editable = true)
    {
        var view = Create(content, editable);
        return new BlockUIContainer(view) { Tag = view.Tag, Margin = new Thickness(0, 6, 0, 6) };
    }
    internal static Border? GetView(BlockUIContainer block) => FindView(block.Child);
    private static Border? FindView(UIElement? element)
    {
        if (element is Border border && Read(border) is not null) return FindView(border.Child) ?? border;
        if (element is Grid grid)
            foreach (UIElement child in grid.Children) if (FindView(child) is { } found) return found;
        return null;
    }
    internal static bool TryRead(BlockUIContainer block, out CodeBlockContent content)
    {
        var state = ReadTag(block.Tag); content = state is null ? new CodeBlockContent("text", "") : new CodeBlockContent(state.Language, state.Code); return state is not null;
    }
    internal static void AttachBlock(BlockUIContainer block, Action<Border>? edit = null)
    {
        if (GetView(block) is { } view)
        {
            if (Attachments.TryGetValue(view, out var live)) { live.Edit = edit; return; }
            // Undo serializes style/template objects. Restore the inner presentation and
            // live handlers from source, without touching the text undo tree.
            if (ReadTag(block.Tag) is { } restored)
            {
                view.Child = Create(new CodeBlockContent(restored.Language, restored.Code), restored.Editable);
                Attachments.GetValue((Border)view.Child, _ => new Attached()).Edit = edit;
                view.BorderThickness = new Thickness(0); view.Padding = new Thickness(0); view.Background = Brushes.Transparent;
            }
            return;
        }
        // WPF can substitute an empty Grid for UI it cannot deserialize. Block.Tag is a
        // native text-element property restored independently, so source cannot be lost.
        // Populate inside that Grid, without replacing Child or creating extra text undo units.
        if (block.Child is Grid placeholder && ReadTag(block.Tag) is { } state)
        {
            var restored = Create(new CodeBlockContent(state.Language, state.Code), state.Editable);
            Attachments.GetValue(restored, _ => new Attached()).Edit = edit; placeholder.Children.Add(restored);
        }
    }
    internal static bool TryRead(Border border, out CodeBlockContent content)
    {
        var state = Read(border); content = state is null ? new CodeBlockContent("text", "") : new CodeBlockContent(state.Language, state.Code); return state is not null;
    }
    internal static void SetCopySinkForEvidence(Border border, Action<string> copy)
    {
        Attach(border);
        if (!Attachments.TryGetValue(border, out var attached)) throw new InvalidOperationException("Code view is not attached.");
        attached.CopyForEvidence = copy;
    }
    private static State? Read(Border border) => ReadTag(border.Tag);
    private static State? ReadTag(object? value)
    {
        if (value is not string tag || !tag.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<State>(tag[Prefix.Length..]); }
        catch (JsonException) { return null; }
    }
    internal static void Attach(Border border)
    {
        if (Attachments.TryGetValue(border, out _) || Read(border) is not { } state) return;
        var attached = new Attached(); Attachments.Add(border, attached);
        ((Button)Find(border, "CodeCollapse")!).Click += (_, _) => SetExpanded(border, ((ScrollViewer)Find(border, "CodeScroll")!).Visibility != Visibility.Visible);
        ((Button)Find(border, "CodeCopy")!).Click += (_, _) =>
        {
            try
            {
                if (attached.CopyForEvidence is { } copy) copy(state.Code); else Clipboard.SetText(state.Code);
                ((Button)Find(border, "CodeCopy")!).ToolTip = UiText.Get("Código copiado");
            }
            catch (System.Runtime.InteropServices.ExternalException) { ((Button)Find(border, "CodeCopy")!).ToolTip = UiText.Get("Tente copiar novamente"); }
        };
        ((Button)Find(border, "CodeEdit")!).Click += (_, _) => { if (!state.Editable) return;
            var args = new RoutedEventArgs(EditRequestedEvent, border); border.RaiseEvent(args);
            if (!args.Handled) attached.Edit?.Invoke(border); };
        void Highlight()
        {
            var color = (border.Background as SolidColorBrush)?.Color ?? Colors.White; var dark = color.R + color.G + color.B < 384;
            var key = state.Language + dark; if (attached.Theme == key) return; attached.Theme = key;
            var text = (TextBlock)Find(border, "CodeSource")!; text.Inlines.Clear();
            var definition = ShortcutCodeEditor.Definition(state.Language, dark);
            if (definition is null) { text.Inlines.Add(new Run(state.Code)); return; }
            var document = new TextDocument(state.Code); using var highlighter = new DocumentHighlighter(document, definition);
            for (var line = 1; line <= document.LineCount; line++)
            { if (line > 1) text.Inlines.Add(new LineBreak()); foreach (var run in highlighter.HighlightLine(line).ToRichText().CreateRuns()) text.Inlines.Add(run); }
        }
        border.Loaded += (_, _) => Highlight(); border.LayoutUpdated += (_, _) => Highlight(); Highlight();
    }
    internal static void SetExpanded(Border border, bool expanded)
    {
        ((ScrollViewer)Find(border, "CodeScroll")!).Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        var button = (Button)Find(border, "CodeCollapse")!; ((Path)button.Content).Data = (Geometry)Application.Current.FindResource("Lab.Icon." + (expanded ? "ChevronUp" : "ChevronDown"));
        button.ToolTip = expanded ? UiText.Get("Recolher código") : UiText.Get("Expandir código"); AutomationProperties.SetName(button, (string)button.ToolTip);
    }
    private static Button Tool(string name, string label, string kind)
    {
        var path = new Path { Data = (Geometry)Application.Current.FindResource("Lab.Icon." + kind), Stretch = Stretch.Uniform, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 15, Height = 15 };
        path.SetResourceReference(Shape.StrokeProperty, "Lab.text");
        var button = new Button { Name = name, Content = path, ToolTip = label, Width = 30, Height = 30, Padding = new Thickness(5), Margin = new Thickness(2, 0, 0, 0) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Lab.Shortcuts.Ghost"); AutomationProperties.SetName(button, label); return button;
    }
    internal static FrameworkElement? Find(DependencyObject parent, string name)
    {
        if (parent is FrameworkElement element && element.Name == name) return element;
        // Walk authored children even before the control template has been measured.
        IEnumerable<DependencyObject> children = parent switch
        { Border b when b.Child is not null => [b.Child], Panel p => p.Children.Cast<DependencyObject>(), ContentControl c when c.Content is DependencyObject child => [child], _ => [] };
        foreach (var child in children) if (Find(child, name) is { } found) return found;
        return null;
    }
}
