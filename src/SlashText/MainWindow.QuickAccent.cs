using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SlashText.Design;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private bool _updatingQuickAccentControls;
    private string _quickAccentAcceptedDelayText = "200";
    private bool _restoringQuickAccentDelayText;
    private readonly SemaphoreSlim _quickAccentSaveGate = new(1, 1);
    private ScreenHelpHighlighter? _quickAccentHelpHighlight;
    private string _quickAccentPreviewCharacters = string.Empty;
    private DispatcherTimer? _quickAccentStatusTimer;
    private bool _quickAccentStartupCompleted;
    private QuickAccentRuntimeState? _quickAccentRuntimeForEvidence;

    private void LoadQuickAccentControls()
    {
        _updatingQuickAccentControls = true;
        try
        {
            QuickAccentEnabledCheckBox.IsChecked = _settings.QuickAccentEnabled;
            SelectComboByTag(QuickAccentActivationBox, _settings.QuickAccentActivationKey);
            SelectComboByTag(QuickAccentPositionBox, _settings.QuickAccentToolbarPosition);
            QuickAccentUnicodeCheckBox.IsChecked = _settings.QuickAccentShowUnicode;
            QuickAccentSortCheckBox.IsChecked = _settings.QuickAccentSortByUsage;
            QuickAccentDelayBox.Text = _settings.QuickAccentInputDelayMs.ToString();
            QuickAccentDelayErrorText.Visibility = Visibility.Collapsed;
            QuickAccentDelayBox.SetResourceReference(Control.BorderBrushProperty, "Lab.line");
            QuickAccentExcludedAppsBox.Text = _settings.QuickAccentExcludedApps;
            ApplyQuickAccentCharacterSetSelection(_settings.QuickAccentCharacterSets);
        }
        finally { _updatingQuickAccentControls = false; }
        ApplyQuickAccentSettings();
    }

    private async void QuickAccentSettings_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized && !_updatingQuickAccentControls) await SaveQuickAccentSettingsAsync();
    }

    private async void QuickAccentDelayPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value }) return;
        QuickAccentDelayBox.Text = value;
        if (_initialized) await SaveQuickAccentSettingsAsync();
    }

    private async void QuickAccentDelay_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            ShowQuickAccentDelayInputError();
            return;
        }
        if (e.Key != Key.Enter || !_initialized) return;
        e.Handled = true;
        await SaveQuickAccentSettingsAsync();
    }

    private static bool IsQuickAccentDelayDraft(string text) => text.Length == 0 ||
        (text.Length <= 4 && text.All(character => character is >= '0' and <= '9') &&
         int.TryParse(text, out var value) && value <= 2000);

    private bool CanInsertQuickAccentDelay(string text) => text.Length <= 4 && IsQuickAccentDelayDraft(
        QuickAccentDelayBox.Text.Remove(QuickAccentDelayBox.SelectionStart, QuickAccentDelayBox.SelectionLength)
            .Insert(QuickAccentDelayBox.SelectionStart, text));

    private void ShowQuickAccentDelayInputError()
    {
        QuickAccentDelayErrorText.Text = UiText.Get("Informe um número inteiro entre 0 e 2.000 ms.");
        QuickAccentDelayErrorText.Visibility = Visibility.Visible;
        QuickAccentDelayBox.SetResourceReference(Control.BorderBrushProperty, "Lab.error");
    }

    private void QuickAccentDelay_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (CanInsertQuickAccentDelay(e.Text)) return;
        e.Handled = true;
        ShowQuickAccentDelayInputError();
    }

    private void QuickAccentDelay_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text && CanInsertQuickAccentDelay(text)) return;
        // Reject the whole paste: truncating it could silently change the requested delay.
        e.CancelCommand();
        ShowQuickAccentDelayInputError();
    }

    private void QuickAccentDelay_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_restoringQuickAccentDelayText || sender is not TextBox box) return;
        if (IsQuickAccentDelayDraft(box.Text))
        {
            _quickAccentAcceptedDelayText = box.Text;
            QuickAccentDelayErrorText.Visibility = Visibility.Collapsed;
            box.SetResourceReference(Control.BorderBrushProperty, "Lab.line");
            return;
        }
        // Covers drop, undo and programmatic/accessibility edits as well as keyboard/paste.
        var caret = box.CaretIndex;
        _restoringQuickAccentDelayText = true;
        try
        {
            box.Text = _quickAccentAcceptedDelayText;
            box.CaretIndex = Math.Min(caret, box.Text.Length);
        }
        finally { _restoringQuickAccentDelayText = false; }
        ShowQuickAccentDelayInputError();
    }

    private async Task SaveQuickAccentSettingsAsync()
    {
        await _quickAccentSaveGate.WaitAsync();
        try
        {
            // An empty draft stays visible for correction; unrelated switches still save,
            // especially disabling the feature. Never replace a valid delay with a default.
            var validDelay = int.TryParse(QuickAccentDelayBox.Text, out var delay) && delay is >= 0 and <= 2000;
            QuickAccentDelayErrorText.Text = UiText.Get("Informe um número inteiro entre 0 e 2.000 ms.");
            QuickAccentDelayErrorText.Visibility = validDelay ? Visibility.Collapsed : Visibility.Visible;
            QuickAccentDelayBox.SetResourceReference(Control.BorderBrushProperty, validDelay ? "Lab.line" : "Lab.error");
            if (validDelay)
            {
                _settings.QuickAccentInputDelayMs = delay;
            }
            _settings.QuickAccentEnabled = QuickAccentEnabledCheckBox.IsChecked == true;
            _settings.QuickAccentActivationKey = SelectedTag(QuickAccentActivationBox, "Space");
            _settings.QuickAccentToolbarPosition = SelectedTag(QuickAccentPositionBox, "BottomCenter");
            _settings.QuickAccentShowUnicode = QuickAccentUnicodeCheckBox.IsChecked == true;
            _settings.QuickAccentSortByUsage = QuickAccentSortCheckBox.IsChecked == true;
            _settings.QuickAccentExcludedApps = QuickAccentExcludedAppsBox.Text;
            _settings.QuickAccentCharacterSets = SelectedQuickAccentCharacterSets();
            ApplyQuickAccentSettings();
            await _settingsStore.SaveAsync(_settings);
        }
        catch (Exception exception)
        {
            AppDiagnosticLog.Write("quick-accent.settings.save_failed", ("exceptionType", exception.GetType().Name));
            QuickAccentDelayErrorText.Text = UiText.Get("Não foi possível salvar as preferências. Tente novamente.");
            QuickAccentDelayErrorText.Visibility = Visibility.Visible;
        }
        finally { _quickAccentSaveGate.Release(); }
    }

    private Button[] QuickAccentPreviewButtons() =>
    [ QuickAccentPreviewChoice0, QuickAccentPreviewChoice1, QuickAccentPreviewChoice2, QuickAccentPreviewChoice3,
      QuickAccentPreviewChoice4, QuickAccentPreviewChoice5, QuickAccentPreviewChoice6, QuickAccentPreviewChoice7 ];

    private void QuickAccentPreviewLetter_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        _quickAccentPreviewIndex = 0;
        UpdateQuickAccentPreviewSelection();
    }

    private void QuickAccentPreviewChoice_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Kept as a selection-only route for the existing UI inventory. Button.Click
        // handles keyboard and mouse insertion once, entirely inside the local test box.
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index))
        { _quickAccentPreviewIndex = index; UpdateQuickAccentPreviewSelection(); }
    }

    private void QuickAccentPreviewChoice_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string value, IsEnabled: true } || !int.TryParse(value, out var index)
            || index < 0 || index >= _quickAccentPreviewCharacters.Length) return;
        _quickAccentPreviewIndex = index;
        QuickAccentTestBox.SelectedText = _quickAccentPreviewCharacters[index].ToString();
        QuickAccentTestBox.CaretIndex = QuickAccentTestBox.SelectionStart + QuickAccentTestBox.SelectionLength;
        QuickAccentTestBox.SelectionLength = 0;
        UpdateQuickAccentPreviewSelection();
        QuickAccentTestBox.Focus();
    }

    private void QuickAccentClearTest_OnClick(object sender, RoutedEventArgs e)
    { QuickAccentTestBox.Clear(); QuickAccentTestBox.Focus(); }

    private void UpdateQuickAccentPreviewSelection()
    {
        if (QuickAccentPreviewChoice7 is null || QuickAccentTestBox is null) return;
        var letter = SelectedTag(QuickAccentPreviewLetterBox, "A")[0];
        _quickAccentPreviewCharacters = _quickAccentService.GetPreviewChoices(char.ToLowerInvariant(letter));
        _quickAccentPreviewIndex = Math.Clamp(_quickAccentPreviewIndex, 0, Math.Max(0, _quickAccentPreviewCharacters.Length - 1));
        var enabled = _settings.QuickAccentEnabled;
        var activation = _settings.QuickAccentActivationKey switch { "Left" => "←", "Right" => "→", _ => UiText.Get("Espaço") };
        QuickAccentPreviewActivationText.Text = UiText.Format($"Segure {char.ToLowerInvariant(letter)} + toque em {activation} · solte a letra para inserir.");
        QuickAccentPreviewActivationText.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        QuickAccentPreviewDisabledText.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        QuickAccentTestBox.IsEnabled = QuickAccentClearTestButton.IsEnabled = enabled;
        QuickAccentPreviewEmptyText.Visibility = _quickAccentPreviewCharacters.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var buttons = QuickAccentPreviewButtons();
        for (var index = 0; index < buttons.Length; index++)
        {
            var button = buttons[index];
            button.Visibility = index < _quickAccentPreviewCharacters.Length ? Visibility.Visible : Visibility.Collapsed;
            button.IsEnabled = enabled;
            if (index >= _quickAccentPreviewCharacters.Length) continue;
            var character = _quickAccentPreviewCharacters[index];
            var selected = index == _quickAccentPreviewIndex;
            var content = new StackPanel();
            var characterText = new TextBlock { Text = character.ToString(), HorizontalAlignment = HorizontalAlignment.Center };
            characterText.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Lab.accent-text" : "Lab.text");
            content.Children.Add(characterText);
            if (_settings.QuickAccentShowUnicode)
            {
                var unicodeText = new TextBlock { Text = $"U+{(int)character:X4}", FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
                unicodeText.SetResourceReference(TextBlock.ForegroundProperty, selected ? "Lab.accent-text" : "Lab.muted");
                content.Children.Add(unicodeText);
            }
            button.Content = content;
            System.Windows.Automation.AutomationProperties.SetName(button, UiText.Format($"Inserir {character}, U+{(int)character:X4}, no campo de teste"));
            button.ToolTip = UiText.Format($"Inserir {character} no teste · U+{(int)character:X4}");
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Lab.tint" : "Lab.input");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Lab.accent" : "Lab.line");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Lab.accent-text" : "Lab.text");
        }
        UpdateQuickAccentSummaries();
        UpdateQuickAccentStatus();
    }

    private void UpdateQuickAccentLayout(double width)
    {
        if (QuickAccentPreviewBody is null) return;
        var narrow = width < 1100;
        QuickAccentLanguageGrid.Columns = width < 900 ? 2 : 3;
        QuickAccentPreviewBody.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        QuickAccentPreviewBody.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(QuickAccentTestPanel, narrow ? 0 : 1);
        Grid.SetRow(QuickAccentTestPanel, narrow ? 1 : 0);
        QuickAccentTestPanel.Margin = narrow ? new Thickness(0, 6, 0, 0) : new Thickness(14, 0, 0, 6);
    }

    private void UpdateQuickAccentSummaries()
    {
        QuickAccentEnabledCheckBox.Content = _settings.QuickAccentEnabled ? UiText.Get("Ativado") : UiText.Get("Desativado");
        QuickAccentUnicodeCheckBox.Content = _settings.QuickAccentShowUnicode ? UiText.Get("Ativado") : UiText.Get("Desativado");
        QuickAccentSortCheckBox.Content = _settings.QuickAccentSortByUsage ? UiText.Get("Ativado") : UiText.Get("Desativado");
        QuickAccentPositionSummaryText.Text = _settings.QuickAccentToolbarPosition switch
        { "TopCenter" => UiText.Get("Centro superior"), "Center" => UiText.Get("Centro da tela"), _ => UiText.Get("Centro inferior") };
        var count = (_settings.QuickAccentExcludedApps ?? string.Empty).Split(['\r', '\n', ',', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        QuickAccentExcludedSummaryText.Text = count == 0 ? UiText.Get("Nenhum aplicativo") : count == 1 ? UiText.Get("1 aplicativo") : UiText.Format($"{count} aplicativos");
        foreach (var button in new[] { QuickAccentDelayPreset100Button, QuickAccentDelayPreset200Button, QuickAccentDelayPreset500Button })
        {
            var selected = button.Tag?.ToString() == _settings.QuickAccentInputDelayMs.ToString();
            button.SetResourceReference(Control.BackgroundProperty, selected ? "Lab.tint" : "Lab.panel");
            button.SetResourceReference(Control.ForegroundProperty, selected ? "Lab.accent-text" : "Lab.text");
            button.SetResourceReference(Control.BorderBrushProperty, selected ? "Lab.accent" : "Lab.line");
            System.Windows.Automation.AutomationProperties.SetHelpText(button, selected ? UiText.Get("Atraso atual") : UiText.Get("Aplicar este atraso"));
        }
    }

    private void StartQuickAccentStatusMonitor()
    {
        _quickAccentStartupCompleted = true;
        _quickAccentStatusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _quickAccentStatusTimer.Tick += QuickAccentStatus_OnTick;
        _quickAccentStatusTimer.Start();
        UpdateQuickAccentStatus();
    }

    private void QuickAccentStatus_OnTick(object? sender, EventArgs e)
    {
        if (IsVisible && QuickAccentView.Visibility == Visibility.Visible && !_servicesDisposed) UpdateQuickAccentStatus();
    }

    private void StopQuickAccentStatusMonitor()
    {
        if (_quickAccentStatusTimer is null) return;
        _quickAccentStatusTimer.Stop(); _quickAccentStatusTimer.Tick -= QuickAccentStatus_OnTick; _quickAccentStatusTimer = null;
    }

    private void UpdateQuickAccentStatus()
    {
        if (QuickAccentStateText is null) return;
        var state = !_settings.QuickAccentEnabled ? QuickAccentRuntimeState.Disabled :
            _quickAccentRuntimeForEvidence ?? _quickAccentService.GetRuntimeState();
        var activation = _settings.QuickAccentActivationKey switch { "Left" => "←", "Right" => "→", _ => UiText.Get("Espaço") };
        QuickAccentStateText.Text = !_quickAccentStartupCompleted && _settings.QuickAccentEnabled
            ? UiText.Get("Preparando o Acento Rápido…")
            : state switch
            {
                QuickAccentRuntimeState.Disabled => UiText.Get("Desativado · suas preferências estão preservadas."),
                QuickAccentRuntimeState.Ready => UiText.Format($"Pronto para usar · segure a letra e toque em {activation}."),
                QuickAccentRuntimeState.Paused => UiText.Get("Pausado neste aplicativo · está na lista de exclusões."),
                _ => UiText.Get("Indisponível · não foi possível ativar o recurso.")
            };
        QuickAccentStateText.SetResourceReference(TextBlock.ForegroundProperty,
            state == QuickAccentRuntimeState.Ready ? "Lab.accent-text" : state == QuickAccentRuntimeState.Unavailable && _quickAccentStartupCompleted ? "Lab.error" : "Lab.muted");
        System.Windows.Automation.AutomationProperties.SetHelpText(QuickAccentEnabledCheckBox, QuickAccentStateText.Text);
    }

    private void OpenQuickAccentHelp_OnClick(object sender, RoutedEventArgs e)
    {
        _quickAccentHelpHighlight?.Remove();
        var guide = new ScreenHelpWindow(QuickAccentHelpContent.Create()) { Owner = this };
        LabMotion.SetReduced(guide, LabMotion.GetReduced(this));
        guide.EnableBackdrop(); ShowCaptureDialog(guide);
        if (guide.RequestedTarget is not { } name || FindName(name) is not FrameworkElement target) return;
        if (name is "QuickAccentPositionBox" or "QuickAccentUnicodeCheckBox" or "QuickAccentSortCheckBox") QuickAccentBehaviorExpander.IsExpanded = true;
        if (name is "QuickAccentSetsPanel") QuickAccentSetsExpander.IsExpanded = true;
        if (name is "QuickAccentExcludedAppsBox") QuickAccentExcludedExpander.IsExpanded = true;
        QuickAccentPage.UpdateLayout();
        target.BringIntoView();
        Dispatcher.BeginInvoke(new Action(() =>
        { if (target.IsVisible) { target.Focus(); _quickAccentHelpHighlight = ScreenHelpHighlighter.Show(target); } }), DispatcherPriority.Loaded);
    }

    internal void PrepareQuickAccentEvidence(AppSettings settings, double width)
    {
        _settings = settings; _quickAccentStartupCompleted = true; LoadQuickAccentControls();
        ShowView(QuickAccentView, QuickAccentTabButton); UpdateQuickAccentLayout(width);
        _initialized = true; // Exercise real change handlers; this fixture never runs Loaded/startup hooks.
    }
    internal Task SaveQuickAccentForEvidence() => SaveQuickAccentSettingsAsync();
    internal bool QuickAccentHookRunningForEvidence => _quickAccentService.IsRunning;
    internal string QuickAccentChoicesForEvidence => _quickAccentPreviewCharacters;
    internal void SetQuickAccentRuntimeForEvidence(QuickAccentRuntimeState? state)
    { _quickAccentRuntimeForEvidence = state; UpdateQuickAccentStatus(); }
}
