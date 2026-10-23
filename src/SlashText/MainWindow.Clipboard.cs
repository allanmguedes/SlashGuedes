using System.Windows;
using System.Windows.Controls;
using SlashText.Models;
using SlashText.Services;
using SlashText.Views;

namespace SlashText;

public partial class MainWindow
{
    private readonly ClipboardHistoryService _clipboardHistory = new();
    private ClipboardQuickWindow? _clipboardQuickWindow;
    private bool _clipboardReady;
    private bool _suppressClipboardOptions;
    private bool _refreshingClipboardCategories;
    private IntPtr _clipboardPasteTarget;
    private bool _clipboardPasteInProgress;

    private void InitializeClipboard()
    {
        _clipboardHistory.Changed += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RefreshClipboardView));
        _clipboardHistory.OpenRequested += (_, _) =>
        {
            // Capture the foreground HWND synchronously when Windows delivers
            // the global hotkey, before any dispatcher scheduling or popup focus.
            var externalTarget = ClipboardPasteService.CaptureExternalTarget();
            Dispatcher.BeginInvoke(new Action(() => OpenClipboardQuickWindow(externalTarget)));
        };
    }

    private async Task StartClipboardAsync()
    {
        var recentLimit = ClipboardRetentionPolicy.NormalizeLimit(_settings.ClipboardMaxEntries);
        await _clipboardHistory.LoadAsync(recentLimit);
        _clipboardHistory.Attach(this, _settings.ClipboardShortcut, out var hotkeyWarning);
        if (_clipboardHistory.HotkeyRegistered &&
            !_settings.ClipboardShortcut.Equals(_clipboardHistory.Shortcut, StringComparison.OrdinalIgnoreCase) &&
            !_settingsStore.IsProtected)
        {
            // Keep the working fallback across restarts.
            _settings.ClipboardShortcut = _clipboardHistory.Shortcut;
            try { await _settingsStore.SaveAsync(_settings); }
            catch (Exception error)
            {
                AppDiagnosticLog.Write("clipboard.settings.save_failed",
                    ("exceptionType", error.GetType().Name));
            }
        }
        if (_settings.ClipboardMonitoringEnabled)
            _clipboardHistory.SetEnabled(true);
        _clipboardReady = true;
        _suppressClipboardOptions = true;
        try
        {
            foreach (var option in ClipboardRetentionBox.Items.OfType<ComboBoxItem>())
            {
                if (int.TryParse(option.Tag as string, out var count) && count == recentLimit)
                {
                    ClipboardRetentionBox.SelectedItem = option;
                    break;
                }
            }
            ClipboardShortcutBox.Text = _clipboardHistory.HotkeyRegistered ? _clipboardHistory.Shortcut : "";
            ClipboardAutoPasteCheckBox.IsChecked = _settings.ClipboardAutoPasteEnabled;
        }
        finally { _suppressClipboardOptions = false; }
        ClipboardShortcutHint.Text = hotkeyWarning ??
            (_clipboardHistory.HotkeyRegistered
                ? "Clique no campo e pressione Ctrl, Alt ou Win + uma tecla para alterar."
                : "Clique no campo e pressione Ctrl, Alt ou Win + uma tecla.");
        RefreshClipboardView();
    }

    private void ClipboardTab_OnClick(object sender, RoutedEventArgs e)
    {
        RefreshClipboardView();
        ShowView(ClipboardView, ClipboardTabButton);
    }

    private void ClipboardSearch_OnChanged(object sender, TextChangedEventArgs e)
        => RefreshClipboardView();

    private void ClipboardFilter_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshingClipboardCategories) RefreshClipboardView();
    }

    private void RefreshClipboardView()
    {
        if (ClipboardItemsList is null || ClipboardStatusText is null) return;
        var selectedId = (ClipboardItemsList.SelectedItem as ClipboardHistoryItem)?.Id;
        var query = ClipboardSearchBox?.Text?.Trim() ?? string.Empty;
        RefreshClipboardCategories();
        var filter = (ClipboardFilterBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var list = _clipboardHistory.Items
            .Where(item =>
                (string.IsNullOrEmpty(query) || item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                 || item.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (filter == "all" || filter == "favorite" && item.IsFavorite ||
                 filter == "pinned" && item.IsPinned ||
                 filter == "text" && item.Kind == "text" ||
                 filter == "image" && item.Kind == "image" ||
                 filter.StartsWith("category:", StringComparison.Ordinal) &&
                 string.Equals(item.Category, filter["category:".Length..],
                     StringComparison.OrdinalIgnoreCase)))
            .ToList();
        ClipboardItemsList.ItemsSource = list;
        ClipboardItemsList.SelectedItem =
            list.FirstOrDefault(item => item.Id == selectedId) ?? list.FirstOrDefault();

        var protectedItems = _clipboardHistory.Items.Count(item => item.IsFavorite || item.IsPinned);
        var recentCount = _clipboardHistory.Items.Count - protectedItems;
        var hotkeyStatus = _clipboardHistory.HotkeyRegistered
            ? _clipboardHistory.Shortcut : "atalho não registrado";
        ClipboardStatusText.Text = _clipboardHistory.Enabled
            ? $"Histórico ativo · {recentCount}/{_clipboardHistory.MaxEntries} recentes · {protectedItems} protegidos · {hotkeyStatus}"
            : $"Histórico pausado · {recentCount} recentes · {protectedItems} protegidos · {hotkeyStatus}";
        ClipboardToggleButton.Content = _clipboardHistory.Enabled ? "Pausar histórico" : "Ativar histórico";
        ClipboardQuickWindowButton.IsEnabled = _clipboardReady;
        ClipboardSelected_OnChanged(null!, null!);
        // Clipboard copies must not rebuild a hidden popup in the background.
        if (_clipboardQuickWindow?.IsVisible == true) _clipboardQuickWindow.Refresh();
    }

    private void ClipboardSelected_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClipboardSelectionText is null) return;
        var selected = ClipboardItemsList?.SelectedItem as ClipboardHistoryItem;
        ClipboardSelectionText.Text = selected?.Kind == "text" ? selected.Text : string.Empty;
        ClipboardSelectionImage.Source = selected is null ? null : _clipboardHistory.LoadThumbnail(selected);
        ClipboardSelectionImage.Visibility = selected?.Kind == "image" ? Visibility.Visible : Visibility.Collapsed;
        ClipboardSelectionText.Visibility = selected?.Kind == "text" ? Visibility.Visible : Visibility.Collapsed;
        ClipboardSelectionEmpty.Visibility = selected is null ? Visibility.Visible : Visibility.Collapsed;
        ClipboardCopyButton.IsEnabled = selected is not null;
        ClipboardFavoriteButton.IsEnabled = selected is not null;
        ClipboardPinButton.IsEnabled = selected is not null;
        ClipboardDeleteButton.IsEnabled = selected is not null;
        ClipboardCategorySaveButton.IsEnabled = selected is not null;
        ClipboardFavoriteButton.Content = selected?.IsFavorite == true ? "★ Favorito" : "☆ Favoritar";
        ClipboardPinButton.Content = selected?.IsPinned == true ? "Desafixar" : "Fixar";
        ClipboardCategoryBox.SelectedItem = selected is not null &&
            _clipboardHistory.Categories.FirstOrDefault(c =>
                c.Equals(selected.Category, StringComparison.OrdinalIgnoreCase)) is { } selectedCategory
            ? selectedCategory : string.Empty;
    }

    private async void ClipboardToggle_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_clipboardReady) return;
        var enabled = !_clipboardHistory.Enabled;
        if (!_clipboardHistory.SetEnabled(enabled))
        {
            StatusText.Text = "Não foi possível ativar o monitor da área de transferência.";
            return;
        }
        _settings.ClipboardMonitoringEnabled = enabled;
        try { await SavePreferencesAsync(); }
        catch (Exception error) { StatusText.Text = "Histórico alterado nesta sessão, mas não salvo: " + error.Message; }
        RefreshClipboardView();
    }

    private async void ClipboardRetention_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_clipboardReady || _suppressClipboardOptions ||
            ClipboardRetentionBox.SelectedItem is not ComboBoxItem option ||
            !int.TryParse(option.Tag as string, out var next)) return;
        var previous = _settings.ClipboardMaxEntries;
        try
        {
            await _clipboardHistory.SetMaxEntriesAsync(next);
            _settings.ClipboardMaxEntries = next;
            await SavePreferencesAsync();
            StatusText.Text = $"Histórico limitado aos últimos {next} itens comuns. Favoritos e fixados são permanentes.";
        }
        catch (Exception error)
        {
            _settings.ClipboardMaxEntries = previous;
            StatusText.Text = "Não foi possível salvar o limite: " + error.Message;
        }
        RefreshClipboardView();
    }

    private async void ClipboardShortcutSave_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_clipboardReady) return;
        var previous = _clipboardHistory.Shortcut;
        if (!_clipboardHistory.TrySetShortcut(ClipboardShortcutBox.Text, out var error))
        {
            ClipboardShortcutHint.Text = error + " Clique no campo e pressione outra combinação.";
            ClipboardShortcutBox.Text = _clipboardHistory.HotkeyRegistered
                ? _clipboardHistory.Shortcut : string.Empty;
            return;
        }
        try
        {
            _settings.ClipboardShortcut = _clipboardHistory.Shortcut;
            await SavePreferencesAsync();
            ClipboardShortcutBox.Text = _clipboardHistory.Shortcut;
            ClipboardShortcutHint.Text = "Atalho salvo e funcionando: " + _clipboardHistory.Shortcut;
        }
        catch (Exception exception)
        {
            _clipboardHistory.TrySetShortcut(previous, out _);
            ClipboardShortcutBox.Text = previous;
            ClipboardShortcutHint.Text = "Não foi possível salvar o atalho: " + exception.Message;
        }
        RefreshClipboardView();
    }

    private static readonly string[] ClipboardShortcutSuggestions =
    [
        "Ctrl+Alt+V", "Ctrl+Shift+H", "Alt+Shift+V", "Ctrl+Alt+H"
    ];

    private void ClipboardShortcutSuggest_OnClick(object sender, RoutedEventArgs e)
    {
        var current = ClipboardShortcutBox.Text.Trim();
        var idx = Array.FindIndex(ClipboardShortcutSuggestions, value =>
            value.Equals(current, StringComparison.OrdinalIgnoreCase));
        ClipboardShortcutBox.Text =
            ClipboardShortcutSuggestions[(idx + 1) % ClipboardShortcutSuggestions.Length];
        ClipboardShortcutHint.Text = "Sugestão opcional. Você também pode clicar no campo e pressionar qualquer combinação com Ctrl, Alt ou Win.";
    }

    private async void ClipboardAutoPaste_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_clipboardReady || _suppressClipboardOptions) return;
        var enabled = ClipboardAutoPasteCheckBox.IsChecked == true;
        var before = _settings.ClipboardAutoPasteEnabled;
        if (before == enabled) return;
        _settings.ClipboardAutoPasteEnabled = enabled;
        try
        {
            await SavePreferencesAsync();
            StatusText.Text = enabled
                ? "Colagem automática ativada para aplicações externas."
                : "Colagem automática pausada: os itens serão apenas copiados.";
        }
        catch (Exception error)
        {
            _settings.ClipboardAutoPasteEnabled = before;
            _suppressClipboardOptions = true;
            ClipboardAutoPasteCheckBox.IsChecked = before;
            _suppressClipboardOptions = false;
            StatusText.Text = "Não foi possível salvar a preferência: " + error.Message;
        }
    }

    private void ClipboardOpenQuick_OnClick(object sender, RoutedEventArgs e) => OpenClipboardQuickWindow();

    private void OpenClipboardQuickWindow(IntPtr? capturedExternalTarget = null)
    {
        if (_clipboardQuickWindow is { IsVisible: true })
        {
            _clipboardQuickWindow.Hide();
            return;
        }
        if (!_clipboardReady)
        {
            StatusText.Text = "O histórico ainda está carregando. Aguarde um instante.";
            return;
        }
        var popupStage = 0;
        try
        {
        popupStage = 1; // constructing
        // Remember the external target BEFORE we activate the popup.
        // Opening from the main window deliberately uses copy-only behavior.
        _clipboardPasteTarget = capturedExternalTarget ??
            ClipboardPasteService.CaptureExternalTarget();
        _clipboardQuickWindow ??= new ClipboardQuickWindow(
            _clipboardHistory,
            item => _ = UseClipboardQuickItemAsync(item, copyOnly: false),
            item => _ = UseClipboardQuickItemAsync(item, copyOnly: true),
            () =>
            {
                ShowFromTray();
                RefreshClipboardView();
                ShowView(ClipboardView, ClipboardTabButton);
            },
            () => ClipboardToggle_OnClick(this, new RoutedEventArgs()));
        popupStage = 2; // opening / data template rendering
        _clipboardQuickWindow.Open();
        popupStage = 3; // successful
        StatusText.Text = "Painel rápido aberto. Use Esc para fechar.";
        }
        catch (Exception error)
        {
            ClipboardShortcutHint.Text = "Erro ao abrir o painel rápido. Consulte o diagnóstico.";
            StatusText.Text = "Não foi possível abrir o painel rápido.";
            AppDiagnosticLog.Write("clipboard.popup.failed",
                ("exceptionType", error.GetType().Name),
                ("hresult", $"0x{error.HResult:X8}"),
                ("count", popupStage));
        }
    }

    private async Task UseClipboardQuickItemAsync(ClipboardHistoryItem item, bool copyOnly)
    {
        if (_clipboardPasteInProgress) return;
        _clipboardPasteInProgress = true;
        try
        {
            if (!_clipboardHistory.CopyToClipboard(item))
            {
                StatusText.Text = "Falha ao recuperar item do histórico.";
                return;
            }

            var target = _clipboardPasteTarget;
            _clipboardPasteTarget = IntPtr.Zero;
            if (copyOnly || !_settings.ClipboardAutoPasteEnabled ||
                !ClipboardPasteService.IsExternalTarget(target))
            {
                StatusText.Text = "Item copiado para a área de transferência. Use Ctrl + V.";
                return;
            }

            var result = await ClipboardPasteService.TryPasteAsync(target);
            StatusText.Text = result == ClipboardPasteService.Result.Sent
                ? "Ctrl + V enviado para o aplicativo original."
                : "Não foi possível colar automaticamente. Item copiado: use Ctrl + V.";
            if (result != ClipboardPasteService.Result.Sent)
                AppDiagnosticLog.Write("clipboard.paste.fallback",
                    ("reason", result.ToString()));
        }
        catch (Exception error)
        {
            StatusText.Text = "Colagem indisponível. Use Ctrl + V se o item foi copiado.";
            AppDiagnosticLog.Write("clipboard.paste.fallback",
                ("reason", error.GetType().Name));
        }
        finally { _clipboardPasteInProgress = false; }
    }

    private void ClipboardCopy_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        StatusText.Text = _clipboardHistory.CopyToClipboard(item)
            ? "Copiado. Use Ctrl + V para colar."
            : "Não foi possível copiar esse item.";
    }

    private async void ClipboardFavorite_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        await _clipboardHistory.ChangeAsync(item, favorite: !item.IsFavorite);
    }

    private async void ClipboardPin_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        await _clipboardHistory.ChangeAsync(item, pinned: !item.IsPinned);
    }

    private async void ClipboardCategorySave_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        var category = ClipboardCategoryBox.SelectedItem as string ?? string.Empty;
        try
        {
            await _clipboardHistory.ChangeAsync(item, category: category);
            StatusText.Text = string.IsNullOrEmpty(category)
                ? "Item removido da categoria." : "Item movido para a categoria " + category + ".";
        }
        catch (Exception error)
        {
            StatusText.Text = "Não foi possível aplicar a categoria: " + error.Message;
        }
    }

    private async void ClipboardDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        await _clipboardHistory.DeleteAsync(item);
    }

    private void RefreshClipboardCategories()
    {
        if (_refreshingClipboardCategories || ClipboardCategoryManageBox is null ||
            ClipboardCategoryBox is null || ClipboardFilterBox is null) return;
        _refreshingClipboardCategories = true;
        try
        {
            var categories = _clipboardHistory.Categories.ToArray();
            var managerSelected = ClipboardCategoryManageBox.SelectedItem as string;
            var assignmentSelected = ClipboardCategoryBox.SelectedItem as string;
            var filterSelected = (ClipboardFilterBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";

            ClipboardCategoryManageBox.ItemsSource = categories;
            ClipboardCategoryManageBox.SelectedItem = categories.FirstOrDefault(c =>
                c.Equals(managerSelected, StringComparison.OrdinalIgnoreCase));

            ClipboardCategoryBox.ItemsSource = new[] { string.Empty }.Concat(categories).ToArray();
            ClipboardCategoryBox.SelectedItem = categories.FirstOrDefault(c =>
                c.Equals(assignmentSelected, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;

            var missing = ClipboardFilterBox.Items.OfType<ComboBoxItem>()
                .Where(i => i.Tag is string tag &&
                            tag.StartsWith("category:", StringComparison.Ordinal))
                .ToArray();
            foreach (var entry in missing) ClipboardFilterBox.Items.Remove(entry);
            foreach (var category in categories)
                ClipboardFilterBox.Items.Add(new ComboBoxItem
                {
                    Content = "Categoria: " + category,
                    Tag = "category:" + category
                });
            ClipboardFilterBox.SelectedItem = ClipboardFilterBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(entry => string.Equals(entry.Tag as string,
                    filterSelected, StringComparison.OrdinalIgnoreCase))
                ?? ClipboardFilterBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally { _refreshingClipboardCategories = false; }
    }

    private async void ClipboardCategoryCreate_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var name = ClipboardNewCategoryBox.Text;
            await _clipboardHistory.AddCategoryAsync(name);
            ClipboardNewCategoryBox.Clear();
            ClipboardCategoryManageBox.SelectedItem =
                _clipboardHistory.Categories.FirstOrDefault(c =>
                    c.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
            StatusText.Text = "Categoria criada com sucesso.";
        }
        catch (Exception error)
        {
            StatusText.Text = "Não foi possível criar categoria: " + error.Message;
        }
    }

    private async void ClipboardCategoryRename_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardCategoryManageBox.SelectedItem is not string selected)
        {
            StatusText.Text = "Selecione uma categoria para renomear.";
            return;
        }
        try
        {
            var newName = ClipboardNewCategoryBox.Text.Trim();
            await _clipboardHistory.RenameCategoryAsync(selected, newName);
            ClipboardNewCategoryBox.Clear();
            ClipboardCategoryManageBox.SelectedItem =
                _clipboardHistory.Categories.FirstOrDefault(c =>
                    c.Equals(newName, StringComparison.OrdinalIgnoreCase));
            StatusText.Text = "Categoria renomeada. Todos os itens foram atualizados.";
        }
        catch (Exception error)
        {
            StatusText.Text = "Não foi possível renomear categoria: " + error.Message;
        }
    }

    private async void ClipboardCategoryDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardCategoryManageBox.SelectedItem is not string selected)
        {
            StatusText.Text = "Selecione a categoria que deseja excluir.";
            return;
        }
        if (MessageBox.Show(
                "Excluir a categoria \"" + selected + "\"? Os itens serão mantidos sem categoria.",
                "Excluir categoria do SlashGuedes", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await _clipboardHistory.DeleteCategoryAsync(selected);
            StatusText.Text = "Categoria excluída. Nenhum item foi apagado.";
        }
        catch (Exception error)
        {
            StatusText.Text = "Não foi possível excluir categoria: " + error.Message;
        }
    }

    private async void ClipboardClearNonFavorites_OnClick(object sender, RoutedEventArgs e)
    {
        var total = _clipboardHistory.Items.Count(i => !i.IsFavorite);
        if (total == 0)
        {
            StatusText.Text = "Não há itens sem estrela para limpar.";
            return;
        }
        if (MessageBox.Show(
                $"Excluir definitivamente {total} item(ns) sem estrela? Apenas os favoritos serão preservados; itens apenas fixados também serão excluídos.",
                "Limpar não favoritos", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try
        {
            await _clipboardHistory.ClearNonFavoritesAsync();
            StatusText.Text = $"{total} item(ns) não favoritos excluídos. Seus favoritos foram preservados.";
        }
        catch (Exception error)
        {
            StatusText.Text = "Não foi possível limpar histórico: " + error.Message;
        }
    }

    private async void ClipboardClear_OnClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Excluir os itens recentes? Favoritos e fixados serão mantidos.",
                "Limpar histórico recente", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await _clipboardHistory.ClearRecentsAsync();
    }

    private void DisposeClipboard()
    {
        _clipboardHistory.Dispose();
        _clipboardQuickWindow?.Close();
    }
}
