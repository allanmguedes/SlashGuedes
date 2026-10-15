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

    private void InitializeClipboard()
    {
        _clipboardHistory.Changed += (_, _) =>
            Dispatcher.BeginInvoke(new Action(RefreshClipboardView));
        _clipboardHistory.OpenRequested += (_, _) =>
            Dispatcher.BeginInvoke(new Action(OpenClipboardQuickWindow));
    }

    private async Task StartClipboardAsync()
    {
        await _clipboardHistory.LoadAsync();
        _clipboardHistory.Attach(this);
        _clipboardHistory.MaxEntries = Math.Clamp(_settings.ClipboardMaxEntries, 10, 500);
        if (_settings.ClipboardMonitoringEnabled)
            _clipboardHistory.SetEnabled(true);
        _clipboardReady = true;
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

        ClipboardStatusText.Text = _clipboardHistory.Enabled
            ? $"Histórico ativo · {_clipboardHistory.Items.Count} itens · Win + Shift + V"
            : "Histórico pausado · novas cópias não serão salvas";
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
        if (MessageBox.Show("Excluir permanentemente todos os itens do histórico (incluindo favoritos)?",
                "Limpar área de transferência do SlashGuedes", MessageBoxButton.YesNo,
                MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await _clipboardHistory.ClearAsync();
    }

    private void DisposeClipboard()
    {
        _clipboardHistory.Dispose();
        _clipboardQuickWindow?.Close();
    }
}
