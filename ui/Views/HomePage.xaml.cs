using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SSnetUI.Services;

namespace SSnetUI.Views;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        ProcessService.StatusChanged += (_, _) => Dispatcher.Invoke(UpdateState);
        Loaded += (_, _) =>
        {
            UpdateState();
            VersionText.Text = "Текущая версия: " + Services.UpdateService.CurrentVersionString;
        };
    }

    private void UpdateState()
    {
        var running = ProcessService.IsRunning;
        StateDot.Fill = (SolidColorBrush)FindResource(running ? "SuccessBrush" : "DangerBrush");
        StateText.Text = running ? "Запущен" : "Остановлен";
        BtnStart.IsEnabled = !running;
        BtnStop.IsEnabled = running;
    }

    private async void BtnStart_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try { await ProcessService.StartAsync(); }
        finally { SetBusy(false); UpdateState(); }
    }

    private async void BtnStop_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try { await ProcessService.StopAsync(); }
        finally { SetBusy(false); UpdateState(); }
    }

    private async void BtnRestart_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try { await ProcessService.RestartAsync(); }
        finally { SetBusy(false); UpdateState(); }
    }

    private void SetBusy(bool busy)
    {
        BtnStart.IsEnabled = !busy && !ProcessService.IsRunning;
        BtnStop.IsEnabled = !busy && ProcessService.IsRunning;
        BtnRestart.IsEnabled = !busy;
    }

    private async void BtnWarp_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Сгенерировать новый WARP-аккаунт Cloudflare?\n\n" +
            "Старые warp.conf и config.json будут сохранены в .bak.\n" +
            "После генерации sing-box перезапустится.",
            "Обновить WARP", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        BtnWarp.IsEnabled = false;
        WarpStatus.Text = "Регистрируем новый аккаунт…";
        try
        {
            var result = await Services.WarpService.RegenerateAsync();
            if (result.Ok)
            {
                WarpStatus.Text = $"Готово · {DateTime.Now:HH:mm:ss}. Перезапуск…";
                await ProcessService.RestartAsync();
                WarpStatus.Text = $"WARP обновлён · {DateTime.Now:HH:mm:ss}";
                MessageBox.Show(result.Message, "WARP обновлён",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                WarpStatus.Text = "Не удалось";
                MessageBox.Show(result.Message, "Ошибка WARP",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            WarpStatus.Text = "Ошибка";
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnWarp.IsEnabled = true;
        }
    }

    private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        BtnCheckUpdate.IsEnabled = false;
        UpdateStatus.Text = "Проверяем…";
        try
        {
            var info = await Services.UpdateService.CheckAsync();
            if (!string.IsNullOrEmpty(info.Error))
            {
                UpdateStatus.Text = "Ошибка проверки";
                MessageBox.Show(info.Error, "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!info.Available)
            {
                UpdateStatus.Text = $"Установлена последняя версия ({info.Current})";
                return;
            }

            UpdateStatus.Text = $"Доступна версия {info.Latest}";
            var msg = $"Доступно обновление!\n\nТекущая: {info.Current}\nНовая: {info.Latest}\n\n" +
                      (string.IsNullOrWhiteSpace(info.Notes) ? "" : info.Notes + "\n\n") +
                      "Скачать и установить сейчас? Приложение перезапустится.";
            if (MessageBox.Show(msg, "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;

            UpdateStatus.Text = "Скачиваем обновление…";
            var progress = new Progress<double>(p =>
                Dispatcher.Invoke(() => UpdateStatus.Text = $"Скачиваем… {p * 100:0}%"));
            var (ok, message) = await Services.UpdateService.DownloadAndApplyAsync(info, progress);
            if (ok)
                UpdateStatus.Text = "Запуск установщика…";
            else
            {
                UpdateStatus.Text = "Не удалось обновить";
                MessageBox.Show(message, "Обновление", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "Ошибка";
            MessageBox.Show(ex.Message, "Обновление", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            BtnCheckUpdate.IsEnabled = true;
        }
    }

    private void BtnSupport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://www.donationalerts.com/r/vexorter")
            { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenAppFolder_Click(object s, RoutedEventArgs e) => Open(Paths.AppRoot);
    private void OpenSettingsFile_Click(object s, RoutedEventArgs e) => Open(Paths.SettingsJson);
    private void OpenConfigFile_Click(object s, RoutedEventArgs e) => Open(Paths.ConfigJson);
    private void OpenRulesFile_Click(object s, RoutedEventArgs e) => Open(Paths.RulesJson);

    private void Open(string path)
    {
        try
        {
            if (File.Exists(path) || Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
