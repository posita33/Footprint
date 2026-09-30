using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Footprint.Core;

namespace Footprint;

public partial class MainWindow
{
    private void RefreshHistory()
    {
        if (HistoryGrid is null || SearchBox is null || FavoritesOnlyBox is null) return;
        HistoryGrid.ItemsSource = _history
            .Where(record => !FavoritesOnlyBox.IsChecked.GetValueOrDefault() || record.IsFavorite)
            .Where(record => record.Matches(SearchBox.Text.Trim()))
            .ToList();
        UpdateHistoryManagementControls();
    }

    private void NotifySharedHistoryChanged() => SharedHistoryChanged?.Invoke(this, EventArgs.Empty);

    private async void OnSharedHistoryChanged(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, this) || !IsLoaded || _cancellation is not null) return;
        try
        {
            (_history, _skipped) = await _store.LoadAsync();
            await LoadBackupDatesAsync();
            RefreshHistory();
        }
        catch { }
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => RefreshHistory();
    private void FavoriteFilter_Changed(object sender, RoutedEventArgs e) => RefreshHistory();

    private async Task LoadBackupDatesAsync()
    {
        var selected = BackupDateBox.SelectedItem as string;
        _backupDates = await _store.LoadBackupDatesAsync();
        BackupDateBox.ItemsSource = _backupDates
            .Select(date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToList();
        BackupDateBox.SelectedItem = BackupDateBox.Items.Cast<string>().FirstOrDefault(date => date == selected)
            ?? BackupDateBox.Items.Cast<string>().FirstOrDefault();
        UpdateHistoryManagementControls();
    }

    private void BackupDate_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateHistoryManagementControls();

    private async void CreateBackup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _store.SaveDailyBackupAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            await LoadBackupDatesAsync();
            StatusText.Text = "今日の履歴をバックアップしました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("バックアップを保存できませんでした。", error); }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSelectedBackupDate(out var date)) return;
        List<CommandRecord> records;
        try { records = await _store.LoadDailyBackupAsync(date); }
        catch (Exception error)
        {
            ShowError("選択したバックアップを読み込めませんでした。", error);
            return;
        }
        if (MessageBox.Show(this,
            $"{date:yyyy-MM-dd} のバックアップ（{records.Count} 件）で現在の履歴を置き換えます。\n現在の履歴は今日のバックアップとして保存されます。",
            "履歴を復元", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _historyManager.RestoreAsync(_history, records, DateOnly.FromDateTime(DateTime.Today));
            _history = records;
            RefreshHistory();
            await LoadBackupDatesAsync();
            StatusText.Text = $"{date:yyyy-MM-dd} のバックアップから {_history.Count} 件を復元しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("履歴を復元できませんでした。", error); }
    }

    private async void ClearNonFavorite_Click(object sender, RoutedEventArgs e)
    {
        var records = _history.Where(record => !record.IsFavorite).ToList();
        if (records.Count == 0)
        {
            StatusText.Text = "削除できる履歴はありません。";
            return;
        }
        if (MessageBox.Show(this,
            $"お気に入り以外の履歴 {records.Count} 件を削除します。\n現在の履歴は今日のバックアップとして保存されます。",
            "履歴を削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            await _historyManager.ClearNonFavoritesAsync(_history, DateOnly.FromDateTime(DateTime.Today));
            RefreshHistory();
            await LoadBackupDatesAsync();
            StatusText.Text = $"お気に入り以外の履歴 {records.Count} 件を削除しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error) { ShowError("履歴を削除できませんでした。", error); }
    }

    private bool TryGetSelectedBackupDate(out DateOnly date) =>
        DateOnly.TryParseExact(BackupDateBox.SelectedItem as string, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    private void UpdateHistoryManagementControls()
    {
        if (CreateBackupButton is null || RestoreBackupButton is null || ClearNonFavoriteButton is null) return;
        var canManage = _isReady && _cancellation is null;
        CreateBackupButton.IsEnabled = canManage;
        RestoreBackupButton.IsEnabled = canManage && TryGetSelectedBackupDate(out _);
        ClearNonFavoriteButton.IsEnabled = canManage && _history.Any(record => !record.IsFavorite);
    }

    private void History_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cancellation is null && HistoryGrid.SelectedItem is CommandRecord record)
            OutputBox.Text = record.Output;
        UpdateFavoriteButton();
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_cancellation is not null || HistoryGrid.SelectedItem is not CommandRecord record) return;
        try
        {
            await _historyManager.ToggleFavoriteAsync(record);
            RefreshHistory();
            StatusText.Text = record.IsFavorite ? "お気に入りに追加しました。" : "お気に入りから外しました。";
            NotifySharedHistoryChanged();
        }
        catch (Exception error)
        {
            RefreshHistory();
            ShowError("お気に入りの変更を保存できませんでした。", error);
        }
        UpdateFavoriteButton();
    }

    private void UpdateFavoriteButton()
    {
        if (FavoriteButton is null || HistoryGrid is null) return;
        if (HistoryGrid.SelectedItem is not CommandRecord record)
        {
            FavoriteButton.IsEnabled = false;
            FavoriteButton.Content = "☆ お気に入り";
            return;
        }
        FavoriteButton.IsEnabled = _cancellation is null;
        FavoriteButton.Content = record.IsFavorite ? "★ お気に入り解除" : "☆ お気に入り";
    }

    private void Reuse_Click(object sender, RoutedEventArgs e) => ReuseSelected();
    private void History_DoubleClick(object sender, MouseButtonEventArgs e) => ReuseSelected();

    private void ReuseSelected()
    {
        if (_cancellation is not null || HistoryGrid.SelectedItem is not CommandRecord record) return;
        CommandBox.Text = record.Command;
        DirectoryBox.Text = record.WorkingDirectory;
        ShellBox.SelectedIndex = record.Shell == ShellKind.CommandPrompt ? 0 : 1;
        CommandBox.Focus();
        StatusText.Text = "コマンドを入力に戻しました。内容を確認して実行してください。";
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryGrid.SelectedItem is not CommandRecord record) return;
        try { Clipboard.SetText(record.Command); StatusText.Text = "コマンドをコピーしました。"; }
        catch (Exception error) { ShowError("コピーできませんでした。", error); }
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_store.StorageDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = _store.StorageDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception error) { ShowError("履歴フォルダーを開けませんでした。", error); }
    }
}
