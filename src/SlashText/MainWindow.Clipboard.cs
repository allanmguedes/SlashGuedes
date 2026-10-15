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

    private void InitializeClipboard()
    {
        _clipboardHistory.Changed += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RefreshClipboardView));
        _clipboardHistory.OpenRequested += (_, _) =>
            Dispatcher.BeginInvoke(new Action(OpenClipboardQuickWindow));
    }

    private async Task StartClipboardAsync()
    {
        var recentLimit = ClipboardRetentionPolicy.NormalizeLimit(_settings.ClipboardMaxEntries);
        await _clipboardHistory.LoadAsync(recentLimit);
        _clipboardHistory.Attach(this, _settings.ClipboardShortcut, out var hotkeyWarning);
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
            ClipboardShortcutBox.Text = _clipboardHistory.Shortcut;
        }
        finally { _suppressClipboardOptions = false; }
        ClipboardShortcutHint.Text = hotkeyWarning is null
            ? "O atalho abre o painel mesmo com o histórico pausado."
            : "Atalho não registrado: " + hotkeyWarning;
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
        => RefreshClipboardView();

    private void RefreshClipboardView()
    {
        if (ClipboardItemsList is null || ClipboardStatusText is null) return;
        var selectedId = (ClipboardItemsList.SelectedItem as ClipboardHistoryItem)?.Id;
        var query = ClipboardSearchBox?.Text?.Trim() ?? string.Empty;
        var filter = (ClipboardFilterBox?.SelectedItem as ComboBoxItem)?.Tag as string ?? "all";
        var list = _clipboardHistory.Items
            .Where(item =>
                (string.IsNullOrEmpty(query) || item.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                 || item.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase)) &&
                (filter == "all" || filter == "favorite" && item.IsFavorite ||
                 filter == "pinned" && item.IsPinned ||
                 filter == "text" && item.Kind == "text" ||
                 filter == "image" && item.Kind == "image"))
            .ToList();
        ClipboardItemsList.ItemsSource = list;
        ClipboardItemsList.SelectedItem =
            list.FirstOrDefault(item => item.Id == selectedId) ?? list.FirstOrDefault();

        var protectedItems = _clipboardHistory.Items.Count(item => item.IsFavorite || item.IsPinned);
        var recentCount = _clipboardHistory.Items.Count - protectedItems;
        ClipboardStatusText.Text = _clipboardHistory.Enabled
            ? $"Histórico ativo · {recentCount}/{_clipboardHistory.MaxEntries} recentes · {protectedItems} protegidos · {_clipboardHistory.Shortcut}"
            : $"Histórico pausado · {recentCount} recentes · {protectedItems} protegidos";
        ClipboardToggleButton.Content = _clipboardHistory.Enabled ? "Pausar histórico" : "Ativar histórico";
        ClipboardQuickWindowButton.IsEnabled = _clipboardReady;
        ClipboardSelected_OnChanged(null!, null!);
        _clipboardQuickWindow?.Refresh();
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
        ClipboardCategoryBox.Text = selected?.Category ?? string.Empty;
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
            ClipboardShortcutHint.Text = error;
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

    private void ClipboardOpenQuick_OnClick(object sender, RoutedEventArgs e) => OpenClipboardQuickWindow();

    private void OpenClipboardQuickWindow()
    {
        if (!_clipboardReady) return;
        _clipboardQuickWindow ??= new ClipboardQuickWindow(
            _clipboardHistory,
            item =>
            {
                var copied = _clipboardHistory.CopyToClipboard(item);
                StatusText.Text = copied
                    ? "Item restaurado para a área de transferência. Use Ctrl + V para colar."
                    : "Não foi possível restaurar este item.";
            },
            () =>
            {
                ShowFromTray();
                RefreshClipboardView();
                ShowView(ClipboardView, ClipboardTabButton);
            },
            () => ClipboardToggle_OnClick(this, new RoutedEventArgs()));
        _clipboardQuickWindow.Open();
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
        await _clipboardHistory.ChangeAsync(item, category: ClipboardCategoryBox.Text);
    }

    private async void ClipboardDelete_OnClick(object sender, RoutedEventArgs e)
    {
        if (ClipboardItemsList.SelectedItem is not ClipboardHistoryItem item) return;
        await _clipboardHistory.DeleteAsync(item);
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
