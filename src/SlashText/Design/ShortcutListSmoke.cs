using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SlashText.Models;
using SlashText.Services;

namespace SlashText.Design;

/// <summary>Presented WPF list fixture; generated data only, without hooks, tray or clipboard.</summary>
internal static class ShortcutListSmoke
{
    internal static async Task RunAsync(string output)
    {
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "environment.json"), JsonSerializer.Serialize(new
        {
            utc = DateTimeOffset.UtcNow, output = Path.GetFullPath(output), tempDirectory = Path.GetTempPath(),
            bundleCache = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"),
            processPath = Environment.ProcessPath, framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription
        }, new JsonSerializerOptions { WriteIndented = true }));
        AppPaths.Initialize(new AppDataEnvironment(DistributionMode.Portable,
            Path.Combine(output, "data-fixture"), Path.Combine(output, "unused-installed"), isCapturePilot: true));
        var checks = new List<string>();
        var measurements = new List<object>();
        var previousLanguage = UiText.Language;
        try
        {
            foreach (var language in new[] { "pt-BR", "en", "es" })
            foreach (var theme in new[] { "Light", "Dark" })
            foreach (var size in new[] { new Size(1440, 900), new Size(980, 680) })
            {
                UiText.Apply(language); ThemeService.Apply(theme);
                var fixtures = Enumerable.Range(0, 10_000).Select(index => new Snippet
                {
                    Name = $"Fixture name {index:00000}", Trigger = $"/fixture{index:00000}",
                    Content = index % 137 == 0 ? $"Unique content marker {index:00000}" : $"Body {index:00000}",
                    Category = index % 2 == 0 ? "Geral" : "Trabalho",
                    IsFavorite = index % 31 == 0, IsPinned = index % 79 == 0,
                    Enabled = index % 17 != 0, HasLegacyIncompatibleTrigger = index % 29 == 0
                }).ToArray();
                fixtures[11].Name += " " + new string('L', 240);
                fixtures[6001].Name += " long-name " + new string('W', 180);
                fixtures[6001].Content += "\nLong body " + string.Concat(Enumerable.Repeat("Large fixture line áéíóú.\n", 240));
                fixtures[^2].Category = ShortcutCategories.All[^1].Name;
                var window = new MainWindow(captureEvidence: true);
                var root = (FrameworkElement)window.Content; window.Content = null;
                // Detached-root PNGs do not inherit Window.Background; use the same
                // resource explicitly so transparent workspace gaps render faithfully.
                root.SetResourceReference(Panel.BackgroundProperty, "Lab.bg");
                LabMotion.SetReduced(root, true);
                var owner = new Window { Width = size.Width, Height = size.Height, Content = root, ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
                var stage = $"{language}-{theme}-{size.Width:0}";
                try
                {
                    var watch = Stopwatch.StartNew();
                    window.PrepareShortcutsEvidence(size.Width, fixtures); owner.Show();
                    await LayoutAsync(owner);
                    var initialMs = watch.Elapsed.TotalMilliseconds;
                    var list = (ShortcutListBox)window.FindName("SnippetListPanel");
                    var search = (TextBox)window.FindName("SearchBox");
                    var name = (TextBox)window.FindName("NameBox");
                    var ordered = Ordered(fixtures).ToArray();
                    AssertItems(list, ordered, "Initial ordering and pinned precedence");
                    AssertRealized(list, window, "Initial viewport");
                    var originalContainers = list.RealizedItems().ToHashSet();
                    Require(originalContainers.Count > 0, "Presented viewport realizes controls");
                    var maxRealized = originalContainers.Count;
                    var recycled = false;
                    foreach (var index in new[] { 5000, 9999, 2500, 0 })
                    {
                        await ScrollToAsync(list, ordered[index], owner, stage + ":" + index);
                        AssertRealized(list, window, $"Viewport at {index}");
                        maxRealized = Math.Max(maxRealized, list.RealizedItems().Count());
                        if (index != 0 && list.RealizedItems().Any(originalContainers.Contains)) recycled = true;
                        Require(list.ItemContainerGenerator.ContainerFromItem(ordered[index]) is ShortcutListItem,
                            "ScrollIntoView realizes the requested distant item " + stage + ":" + index);
                    }
                    Require(recycled, "Distant scrolling reuses realized container identities");
                    SaveImage(root, Path.Combine(output, $"list-{stage}.png"));

                    var originalItemsSource = list.ItemsSource;
                    watch.Restart(); search.Text = "Fixture name"; await LayoutAsync(owner);
                    var broadSearchMs = watch.Elapsed.TotalMilliseconds;
                    AssertItems(list, ordered, "Broad name search preserves all IDs and ordering");
                    Require(ReferenceEquals(list.ItemsSource, originalItemsSource), "Search with identical results retains the items source reference");
                    search.Text = "/fixture06001"; await LayoutAsync(owner);
                    AssertItems(list, [fixtures[6001]], "Trigger search");
                    search.Text = "Unique content marker"; await LayoutAsync(owner);
                    AssertItems(list, Ordered(fixtures.Where(item => item.Content.StartsWith("Unique", StringComparison.Ordinal))), "Content search");
                    search.Text = "Trabalho"; await LayoutAsync(owner);
                    AssertItems(list, Ordered(fixtures.Where(item => item.Category == "Trabalho")), "Category text search");
                    window.FilterShortcutForEvidence(true, false); await LayoutAsync(owner);
                    AssertItems(list, Ordered(fixtures.Where(item => item.Category == "Trabalho" && item.IsFavorite)), "Search and favorites intersection");
                    window.FilterShortcutForEvidence(false, true); await LayoutAsync(owner);
                    AssertItems(list, Ordered(fixtures.Where(item => item.Category == "Trabalho" && item.IsPinned)), "Search and pinned intersection");
                    window.FilterShortcutForEvidence(false, false); search.Clear(); await LayoutAsync(owner);
                    AssertItems(list, ordered, "Clearing search and flags restores every ID");
                    var categoriesScroll = (ScrollViewer)window.FindName("ShortcutCategoriesScroll");
                    categoriesScroll.ScrollToEnd(); await LayoutAsync(owner);
                    var categories = (StackPanel)window.FindName("CategoriesPanel");
                    var lastCategory = categories.Children.OfType<Button>().Last();
                    Require(IsInsideViewport(lastCategory, categoriesScroll), "Last category is reachable through its own scroll viewport");
                    lastCategory.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await LayoutAsync(owner);
                    AssertItems(list, Ordered(fixtures.Where(item => item.Category == ShortcutCategories.All[^1].Name)), "Clicking last category filters correct IDs");
                    categoriesScroll.ScrollToHome(); await LayoutAsync(owner);
                    var allCategories = categories.Children.OfType<Button>().First();
                    Require(IsInsideViewport(allCategories, categoriesScroll), "All categories remains reachable after category filtering");
                    allCategories.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await LayoutAsync(owner);
                    AssertItems(list, ordered, "Returning to All after scrolling categories restores every ID");

                    var selected = fixtures[2]; var target = fixtures[6001];
                    await ScrollToAsync(list, selected, owner, stage + ":selected");
                    var selectionItemsSource = list.ItemsSource;
                    window.SelectShortcutThroughListForEvidence(selected, true); await LayoutAsync(owner);
                    Require(window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected), "Selection travels through list selection handler");
                    Require(ReferenceEquals(list.ItemsSource, selectionItemsSource), "Selection refresh retains the items source reference");
                    name.Text = selected.Name + " pending draft"; name.Select(4, 5);
                    var focused = name.Focus();
                    var draft = name.Text; var caret = name.SelectionStart; var selectionLength = name.SelectionLength;
                    await ScrollToAsync(list, target, owner, stage + ":cancel-click");
                    var targetRow = (ShortcutListItem)list.ItemContainerGenerator.ContainerFromItem(target);
                    RecordFocus(output, stage, "after-scroll-before-mouse-focus", owner, name, focused);
                    // Focus obtained before deferred scrolling is not proof of the
                    // event's starting focus. Establish and record the precondition
                    // immediately before the generated mouse event.
                    focused = name.Focus();
                    RecordFocus(output, stage, "immediately-before-mouse", owner, name, focused);
                    KeyboardFocusChangedEventHandler focusTrace = (_, args) => RecordFocus(output, stage,
                        args.RoutedEvent.Name + ":" + FocusIdentity(args.OldFocus) + "->" + FocusIdentity(args.NewFocus), owner, name, focused);
                    SelectionChangedEventHandler selectionTrace = (_, _) => RecordFocus(output, stage,
                        "SelectionChanged:editor=" + window.SelectedShortcutForEvidence + ":list=" + (list.SelectedItem as Snippet)?.Id,
                        owner, name, focused);
                    root.AddHandler(Keyboard.GotKeyboardFocusEvent, focusTrace, true);
                    root.AddHandler(Keyboard.LostKeyboardFocusEvent, focusTrace, true);
                    list.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, selectionTrace, true);
                    try
                    {
                        window.WithShortcutDiscardDecisionForEvidence(false, () =>
                        {
                            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                                { RoutedEvent = Mouse.PreviewMouseDownEvent };
                            targetRow.RaiseEvent(down); down.RoutedEvent = Mouse.MouseDownEvent; targetRow.RaiseEvent(down);
                        });
                        await LayoutAsync(owner);
                    }
                    finally
                    {
                        root.RemoveHandler(Keyboard.GotKeyboardFocusEvent, focusTrace);
                        root.RemoveHandler(Keyboard.LostKeyboardFocusEvent, focusTrace);
                        list.RemoveHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, selectionTrace);
                    }
                    RecordFocus(output, stage, "after-cancelled-mouse", owner, name, focused);
                    Require(window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected)
                        && name.Text == draft && name.SelectionStart == caret && name.SelectionLength == selectionLength,
                        "Cancelled routed mouse selection preserves list, draft and caret");
                    if (focused) Require(name.IsKeyboardFocusWithin, "Cancelled mouse selection restores previous editor keyboard focus");
                    var inputSource = PresentationSource.FromVisual(owner) ?? throw new InvalidOperationException("No fixture presentation source");
                    foreach (var navigationKey in new[] { Key.Home, Key.End })
                    {
                        list.Focus();
                        var keyboardFocusBefore = Keyboard.FocusedElement;
                        var keyboardFocusRowBefore = (keyboardFocusBefore as ShortcutListItem)?.Snippet?.Id;
                        RecordFocus(output, stage, "immediately-before-keyboard-" + navigationKey, owner, name, focused);
                        window.WithShortcutDiscardDecisionForEvidence(false, () =>
                        {
                            var key = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, navigationKey)
                                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                            list.RaiseEvent(key); key.RoutedEvent = Keyboard.KeyDownEvent; list.RaiseEvent(key);
                        });
                        await LayoutAsync(owner);
                        RecordFocus(output, stage, "after-cancelled-keyboard-" + navigationKey, owner, name, focused);
                        Require(window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected)
                            && name.Text == draft && window.ShortcutsDraftDirtyForEvidence,
                            "Cancelled routed keyboard selection preserves committed selection and draft");
                        Require(keyboardFocusRowBefore is not null
                            ? (Keyboard.FocusedElement as ShortcutListItem)?.Snippet?.Id == keyboardFocusRowBefore
                            : ReferenceEquals(Keyboard.FocusedElement, keyboardFocusBefore),
                            "Cancelled keyboard navigation restores its previous focused item or list");
                    }
                    await ScrollToAsync(list, target, owner, stage + ":divergent-focus");
                    Require(window.FocusShortcutWithoutSelectingForEvidence(target)
                        && (Keyboard.FocusedElement as ShortcutListItem)?.Snippet?.Id == target.Id
                        && window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected),
                        "Fixture establishes focused row B while committed selection remains A without global modifiers");
                    RecordFocus(output, stage, "before-keyboard-with-divergent-focus", owner, name, focused);
                    var focusedRow = (ShortcutListItem)Keyboard.FocusedElement;
                    window.WithShortcutDiscardDecisionForEvidence(false, () =>
                    {
                        var key = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.Down)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        focusedRow.RaiseEvent(key); key.RoutedEvent = Keyboard.KeyDownEvent; focusedRow.RaiseEvent(key);
                    });
                    await LayoutAsync(owner);
                    RecordFocus(output, stage, "after-cancelled-keyboard-with-divergent-focus", owner, name, focused);
                    Require((Keyboard.FocusedElement as ShortcutListItem)?.Snippet?.Id == target.Id
                        && window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected)
                        && name.Text == draft && window.ShortcutsDraftDirtyForEvidence,
                        "Cancelling navigation from focus B restores B while keeping selection/editor A and draft");
                    var trigger = (TextBox)window.FindName("TriggerBox");
                    list.Focus();
                    var triggerFocusAcquired = false;
                    window.WithShortcutDiscardDecisionForEvidence(false, () =>
                    {
                        var key = new KeyEventArgs(Keyboard.PrimaryDevice, inputSource, Environment.TickCount, Key.End)
                            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                        list.RaiseEvent(key); key.RoutedEvent = Keyboard.KeyDownEvent; list.RaiseEvent(key);
                        triggerFocusAcquired = trigger.Focus();
                    });
                    await LayoutAsync(owner);
                    RecordFocus(output, stage, "after-cancelled-keyboard-with-new-field-focus", owner, name, focused);
                    Require(triggerFocusAcquired && trigger.IsKeyboardFocusWithin,
                        "Deferred cancellation restoration does not steal focus given to another editor field");
                    Require(window.SelectedShortcutForEvidence == selected.Id && name.Text == draft && window.ShortcutsDraftDirtyForEvidence,
                        "Giving another field focus during cancellation preserves editor identity and draft");
                    if (focused) Require(name.Focus(), "Editor can regain programmatic focus after keyboard cancellation");
                    window.SelectShortcutThroughListForEvidence(target, false); await LayoutAsync(owner);
                    Require(window.SelectedShortcutForEvidence == selected.Id && ReferenceEquals(list.SelectedItem, selected)
                        && name.Text == draft && window.ShortcutsDraftDirtyForEvidence, "Cancelled discard restores list selection and editor draft");
                    search.Text = target.Trigger; await LayoutAsync(owner);
                    Require(window.SelectedShortcutForEvidence == selected.Id && name.Text == draft, "Filtering selected item away preserves editor identity and draft");
                    search.Clear(); await LayoutAsync(owner);
                    await ScrollToAsync(list, target, owner, stage + ":draft-target"); list.RefreshRealizedItems(); await LayoutAsync(owner);
                    Require(name.Text == draft && name.SelectionStart == caret && name.SelectionLength == selectionLength
                        && window.ShortcutsDraftDirtyForEvidence, "Scrolling and realized refresh preserve metadata draft and caret");
                    if (focused) Require(name.IsKeyboardFocusWithin, "List refresh does not steal acquired editor keyboard focus");
                    else checks.Add($"{stage}: programmatic keyboard acquisition unavailable; physical focus not certified");
                    AssertRealized(list, window, "Recycled rows with dirty editor");
                    window.SelectShortcutThroughListForEvidence(target, true); await LayoutAsync(owner);
                    Require(window.SelectedShortcutForEvidence == target.Id && ReferenceEquals(list.SelectedItem, target)
                        && name.Text == target.Name && !window.ShortcutsDraftDirtyForEvidence, "Accepted discard loads requested row and resets dirty state");

                    window.BeginNewShortcutForEvidence(); await LayoutAsync(owner);
                    name.Text = "New unsaved draft";
                    window.SelectShortcutThroughListForEvidence(selected, false); await LayoutAsync(owner);
                    Require(window.SelectedShortcutForEvidence is null && list.SelectedItem is null && name.Text == "New unsaved draft",
                        "Cancelled selection from new draft keeps null selection and editor content");
                    window.SelectShortcutThroughListForEvidence(selected, true); await LayoutAsync(owner);
                    search.Text = "no-fixture-can-match-this"; await LayoutAsync(owner);
                    Require(list.Items.Count == 0, "Empty search has zero data items, not a fake snippet");
                    Require(window.SelectedShortcutForEvidence == selected.Id && name.Text == selected.Name, "Empty list leaves editor intact");
                    SaveImage(root, Path.Combine(output, $"empty-{stage}.png"));
                    search.Clear(); await LayoutAsync(owner);
                    AssertItems(list, ordered, "Empty-search recovery restores every snippet");
                    ((Button)window.FindName("ShortcutSearchClearButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await LayoutAsync(owner);
                    Require(search.Text.Length == 0, "Real clear-search action clears search");
                    if (size.Width == 980)
                    {
                        // Layout regression, not a simulated native drag: the existing
                        // integrity script checks splitter wiring. Exercise a legal
                        // column width and remeasure the real virtualized list.
                        var leftColumn = (ColumnDefinition)window.FindName("ShortcutLeftColumn");
                        var previousWidth = leftColumn.Width;
                        leftColumn.Width = new GridLength((leftColumn.MinWidth + leftColumn.MaxWidth) / 2);
                        await LayoutAsync(owner);
                        Require(leftColumn.ActualWidth >= leftColumn.MinWidth - .5 && leftColumn.ActualWidth <= leftColumn.MaxWidth + .5
                            && list.ActualHeight > 0, "Legal sidebar divider width keeps compact list viewport usable");
                        await ScrollToAsync(list, ordered[^1], owner, stage + ":compact-divider");
                        AssertRealized(list, window, "Compact divider width remains virtualized");
                        leftColumn.Width = previousWidth; await LayoutAsync(owner);
                    }
                    owner.Width = size.Width == 1440 ? 980 : 1440; await LayoutAsync(owner);
                    Require(list.ActualHeight > 0, "Changing owner width preserves a positive list viewport");
                    await ScrollToAsync(list, ordered[^1], owner, stage + ":resized-last");
                    AssertRealized(list, window, "Resized viewport remains bounded");
                    if (language == "pt-BR" && theme == "Light" && size.Width == 1440)
                    {
                        await CheckRoutedInputAsync(window, owner, list, ordered, checks);
                    }
                    measurements.Add(new { language, theme, width = size.Width, items = fixtures.Length,
                        initialMs, broadSearchMs, maxRealized, recycled, programmaticFocusAcquired = focused });
                    checks.Add($"{stage}: 10,000 data items; bounded realized rows, recycled identities, row data/state/accessibility, search/filter/order, list selection/discard, draft/caret, empty recovery and resize PASS");
                    File.WriteAllLines(Path.Combine(output, "progress.txt"), checks);
                }
                finally { owner.Close(); window.DisposeShortcutsEvidence(); window.Close(); }
            }
        }
        finally { UiText.Apply(previousLanguage); }
        checks.Add("Limits: programmatic WPF layout/events/focus and screenshots; no claim about physical mouse/keyboard, screen reader, monitor DPI transitions, FPS or hardware capture.");
        File.WriteAllLines(Path.Combine(output, "result.txt"), checks);
        File.WriteAllText(Path.Combine(output, "measurements.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static IOrderedEnumerable<Snippet> Ordered(IEnumerable<Snippet> items) => items
        .OrderBy(item => item.Trigger, StringComparer.CurrentCultureIgnoreCase).OrderByDescending(item => item.IsPinned);

    private static async Task CheckRoutedInputAsync(MainWindow window, Window owner, ShortcutListBox list,
        Snippet[] ordered, List<string> checks)
    {
        var clicked = ordered[7300];
        await ScrollToAsync(list, clicked, owner, "input-click");
        var row = (ShortcutListItem)list.ItemContainerGenerator.ContainerFromItem(clicked);
        row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonDownEvent });
        await LayoutAsync(owner);
        Require(window.SelectedShortcutForEvidence == clicked.Id && ReferenceEquals(list.SelectedItem, clicked),
            "Routed left mouse event selects the current recycled row");
        var source = PresentationSource.FromVisual(owner) ?? throw new InvalidOperationException("Presented owner has no input source");
        list.Focus();
        foreach (var (key, expected) in new[] { (Key.Home, ordered[0]), (Key.End, ordered[^1]) })
        {
            list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
                { RoutedEvent = Keyboard.KeyDownEvent });
            await LayoutAsync(owner);
            Require(window.SelectedShortcutForEvidence == expected.Id && ReferenceEquals(list.SelectedItem, expected),
                $"Routed {key} keeps list and editor selection synchronized");
        }
        await ScrollToAsync(list, clicked, owner, "input-menu");
        row = (ShortcutListItem)list.ItemContainerGenerator.ContainerFromItem(clicked);
        var editorId = window.SelectedShortcutForEvidence;
        var name = (TextBox)window.FindName("NameBox");
        var committedName = name.Text;
        name.Text += " context-menu pending draft";
        var pendingDraft = name.Text;
        var acquiredEditorFocus = name.Focus();
        var oldFavorite = clicked.IsFavorite;
        var others = list.Items.Cast<Snippet>().Where(item => item.Id != clicked.Id).ToDictionary(item => item.Id, item => item.IsFavorite);
        async Task<Button> OpenMenuAsync()
        {
            var oldSources = PresentationSource.CurrentSources.Cast<PresentationSource>().ToHashSet();
            // Button-specific preview events are direct. Raise the underlying tunnel
            // and bubble events so WPF runs its normal right-button class handlers.
            var down = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Mouse.PreviewMouseDownEvent };
            row.RaiseEvent(down); down.RoutedEvent = Mouse.MouseDownEvent; row.RaiseEvent(down);
            Require(window.SelectedShortcutForEvidence == editorId && name.Text == pendingDraft
                && window.ShortcutsDraftDirtyForEvidence, "Right-button down preserves the other row's editor draft");
            row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
                { RoutedEvent = Mouse.PreviewMouseUpEvent });
            await LayoutAsync(owner);
            var label = UiText.Get(oldFavorite ? "Remover dos favoritos" : "Adicionar aos favoritos");
            var popupButtons = PresentationSource.CurrentSources.Cast<PresentationSource>().Where(item => !oldSources.Contains(item))
                .Select(item => item.RootVisual).OfType<DependencyObject>().SelectMany(Descendants<Button>).ToArray();
            var matches = popupButtons.Where(button => AutomationProperties.GetName(button) == label).ToArray();
            Require(matches.Length == 1, "One real popup favorite action; observed: "
                + string.Join(" | ", popupButtons.Select(AutomationProperties.GetName)));
            Require(window.SelectedShortcutForEvidence == editorId && name.Text == pendingDraft,
                "Opening a different row's menu does not change editor selection or draft");
            return matches[0];
        }
        var action = await OpenMenuAsync();
        action.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(action) ?? source, Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        await LayoutAsync(owner);
        Require(window.SelectedShortcutForEvidence == editorId && name.Text == pendingDraft
            && window.ShortcutsDraftDirtyForEvidence, "Closing another row's menu preserves editor identity and draft");
        if (acquiredEditorFocus) Require(name.IsKeyboardFocusWithin, "Popup dismissal restores prior editor keyboard focus");
        action = await OpenMenuAsync();
        action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var timeout = Stopwatch.StartNew();
        while (list.Items.Cast<Snippet>().Single(item => item.Id == clicked.Id).IsFavorite == oldFavorite
            && timeout.Elapsed < TimeSpan.FromSeconds(15))
        { await Task.Delay(20); await LayoutAsync(owner); }
        Require(list.Items.Cast<Snippet>().Single(item => item.Id == clicked.Id).IsFavorite != oldFavorite,
            "Real recycled-row popup action updates the current item's favorite flag");
        Require(list.Items.Cast<Snippet>().Where(item => item.Id != clicked.Id).All(item => others[item.Id] == item.IsFavorite),
            "Recycled-row action leaves every other ID unchanged");
        var saved = await new SnippetMarkdownRepository().LoadAsync();
        Require(saved.Single(item => item.Id == clicked.Id).IsFavorite != oldFavorite,
            "Recycled-row popup action persists the correct ID");
        Require(window.SelectedShortcutForEvidence == editorId && name.Text == pendingDraft
            && window.ShortcutsDraftDirtyForEvidence && saved.Single(item => item.Id == editorId).Name == committedName,
            "Other row's favorite action preserves pending editor draft without committing it");
        window.SelectShortcutThroughListForEvidence(list.Items.Cast<Snippet>().Single(item => item.Id == clicked.Id), true);
        await LayoutAsync(owner);
        checks.Add("Routed WPF left-click, Home/End and right-click/favorite action PASS; events are generated inside the fixture, not physical input.");
    }

    private static void AssertItems(ShortcutListBox list, IEnumerable<Snippet> expected, string stage)
    {
        Require(list.Items.Cast<Snippet>().Select(item => item.Id).SequenceEqual(expected.Select(item => item.Id)), stage);
    }

    private static void AssertRealized(ShortcutListBox list, MainWindow window, string stage)
    {
        var rows = list.RealizedItems().ToArray();
        // At most ~10 cards fit a 900-DIP owner; 80 allows multiple viewport caches,
        // retained focus/selection and variable-height legacy rows, but rejects O(N) realization.
        Require(rows.Length is > 0 and <= 80, $"{stage}: 1..80 realized rows, actual {rows.Length}");
        foreach (var row in rows)
        {
            var snippet = row.Snippet;
            Require(snippet is not null && ReferenceEquals(list.ItemContainerGenerator.ItemFromContainer(row), snippet), $"{stage}: container has current generated item");
            var texts = Descendants<TextBlock>(row).Select(text => text.Text).ToArray();
            Require(texts.Contains(snippet!.Trigger) && texts.Contains(snippet.Name)
                && texts.Contains(snippet.Content.Replace('\r', ' ').Replace('\n', ' ')), $"{stage}: current trigger, name and excerpt after recycling");
            var icons = Descendants<LabIcon>(row).Select(icon => icon.Kind).ToArray();
            Require(icons.Contains("Star") == snippet.IsFavorite && icons.Contains("Pin") == snippet.IsPinned, $"{stage}: recycled badges correspond to item");
            var state = snippet.HasLegacyIncompatibleTrigger ? UiText.Get("Comando legado · revise para ativar") : !snippet.Enabled ? UiText.Get("Pausado") : null;
            Require(state is null
                ? !texts.Contains(UiText.Get("Comando legado · revise para ativar")) && !texts.Contains(UiText.Get("Pausado"))
                : texts.Contains(state), $"{stage}: recycled paused/legacy state is current");
            var tooltip = row.ToolTip?.ToString() ?? "";
            var accessible = AutomationProperties.GetName(row);
            Require(tooltip.Contains(snippet.Name, StringComparison.Ordinal) && tooltip.Contains(snippet.Trigger, StringComparison.Ordinal)
                && accessible.Contains(snippet.Name, StringComparison.Ordinal) && accessible.Contains(snippet.Trigger, StringComparison.Ordinal), $"{stage}: current tooltip and accessible name");
            Require(row.IsSelected == (snippet.Id == window.SelectedShortcutForEvidence), $"{stage}: visual selection matches editor identity");
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static async Task LayoutAsync(Window owner)
    {
        owner.UpdateLayout();
        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        owner.UpdateLayout();
    }

    private static async Task ScrollToAsync(ShortcutListBox list, Snippet target, Window owner, string stage)
    {
        list.ScrollIntoView(target);
        // Variable-height pixel virtualization can defer BringIndexIntoView and then
        // queue a second measure. Wait for layout completion, never substitute selection.
        for (var turn = 0; turn < 8; turn++)
        {
            await LayoutAsync(owner);
            if (list.ItemContainerGenerator.ContainerFromItem(target) is ShortcutListItem row)
            {
                var viewport = Descendants<ScrollContentPresenter>(list).FirstOrDefault();
                if (viewport is not null && row.ActualWidth > 0 && row.ActualHeight > 0)
                {
                    var bounds = row.TransformToAncestor(viewport).TransformBounds(new Rect(row.RenderSize));
                    var intersection = Rect.Intersect(bounds, new Rect(viewport.RenderSize));
                    if (!intersection.IsEmpty && intersection.Width > 0 && intersection.Height > 0) return;
                }
            }
        }
        throw new InvalidOperationException($"Shortcut list smoke: deferred scroll failed at {stage}; owner={owner.ActualWidth}x{owner.ActualHeight}; list={list.ActualWidth}x{list.ActualHeight}; target={target.Trigger}; realized="
            + string.Join(",", list.RealizedItems().Select(item => item.Snippet?.Trigger ?? "null")));
    }

    private static bool IsInsideViewport(FrameworkElement element, DependencyObject scroll)
    {
        var viewport = Descendants<ScrollContentPresenter>(scroll).FirstOrDefault();
        if (viewport is null || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var bounds = element.TransformToAncestor(viewport).TransformBounds(new Rect(element.RenderSize));
        var intersection = Rect.Intersect(bounds, new Rect(viewport.RenderSize));
        return !intersection.IsEmpty && intersection.Width > 0 && intersection.Height > 0;
    }

    private static void RecordFocus(string output, string stage, string phase, Window owner, TextBox name, bool acquired)
    {
        var focused = Keyboard.FocusedElement;
        // Only fixture control metadata; never editor text, clipboard or document names.
        File.AppendAllText(Path.Combine(output, "focus.jsonl"), JsonSerializer.Serialize(new
        {
            stage, phase, ownerActive = owner.IsActive, ownerFocusWithin = owner.IsKeyboardFocusWithin,
            editorFocusWithin = name.IsKeyboardFocusWithin, focusAcquired = acquired,
            focusedType = focused?.GetType().Name, focusedControl = (focused as FrameworkElement)?.Name,
            fixtureRowId = (focused as ShortcutListItem)?.Snippet?.Id
        }) + Environment.NewLine);
    }

    private static string FocusIdentity(IInputElement? element) => element?.GetType().Name + ":"
        + (element as FrameworkElement)?.Name + ":" + (element as ShortcutListItem)?.Snippet?.Id;

    private static void SaveImage(FrameworkElement root, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth)),
            Math.Max(1, (int)Math.Ceiling(root.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Shortcut list smoke: " + message); }
}
