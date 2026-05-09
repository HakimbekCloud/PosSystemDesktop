using System.Windows;
using System.Windows.Controls;
using PosSystem.Services;

namespace PosSystem.Views;

public partial class NetworkLogWindow : Window
{
    private readonly NetworkLogService _log;

    public NetworkLogWindow(NetworkLogService log)
    {
        _log = log;
        InitializeComponent();

        LogList.ItemsSource = _log.Entries;
        _log.Entries.CollectionChanged += (_, _) =>
            CountLabel.Text = $"{_log.Entries.Count} so'rov";

        // Place beside the main window
        var main = Application.Current.MainWindow;
        if (main is not null)
        {
            Left = main.Left + main.Width + 8;
            Top  = main.Top;
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogList.SelectedItem is not NetworkLogEntry entry)
        {
            ReqBodyBox.Text  = "";
            RespBodyBox.Text = "";
            return;
        }

        ReqBodyBox.Text  = TryFormatJson(entry.RequestBody);
        RespBodyBox.Text = TryFormatJson(entry.ResponseBody);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _log.Clear();
        ReqBodyBox.Text  = "";
        RespBodyBox.Text = "";
    }

    private static string TryFormatJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(raw);
            return System.Text.Json.JsonSerializer.Serialize(
                doc.RootElement,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return raw;
        }
    }
}
