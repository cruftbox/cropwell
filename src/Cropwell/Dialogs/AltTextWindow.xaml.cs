using System.Windows;

namespace Cropwell.Dialogs;

public partial class AltTextWindow : Window
{
    private CancellationTokenSource? _cancellation;
    public AltTextWindow() => InitializeComponent();

    public CancellationTokenSource BeginBusy(string provider, string model)
    {
        _cancellation?.Cancel(); _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        BusyPanel.Visibility = Visibility.Visible; CancelButton.Visibility = Visibility.Visible;
        RegenerateButton.IsEnabled = false; TextBox.IsEnabled = false; StaleText.Text = "";
        ProviderText.Text = $"{provider} · {model}";
        return _cancellation;
    }
    public void ShowResult(string text, string provider, string model, bool stale)
    {
        TextBox.Text = text; TextBox.IsEnabled = true; BusyPanel.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed; RegenerateButton.IsEnabled = true;
        ProviderText.Text = $"{provider} · {model}"; StaleText.Text = stale ? "The working image changed while this text was generated." : "";
    }
    public void ShowError(string message)
    {
        TextBox.Text = message; TextBox.IsEnabled = true; BusyPanel.Visibility = Visibility.Collapsed;
        CancelButton.Visibility = Visibility.Collapsed; RegenerateButton.IsEnabled = true;
    }
    public void ShowCancelled() { BusyPanel.Visibility = Visibility.Collapsed; CancelButton.Visibility = Visibility.Collapsed; RegenerateButton.IsEnabled = true; TextBox.IsEnabled = true; }
    public void MarkPossiblyOutOfDate() { if (!string.IsNullOrWhiteSpace(TextBox.Text)) StaleText.Text = "The working image changed. This text may be out of date."; }
    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancellation?.Cancel();
    private void Copy_Click(object sender, RoutedEventArgs e) { if (!string.IsNullOrEmpty(TextBox.Text)) Clipboard.SetText(TextBox.Text); }
    private async void Regenerate_Click(object sender, RoutedEventArgs e) { if (Owner is MainWindow owner) await owner.GenerateAltTextAsync(); }
    protected override void OnClosed(EventArgs e) { _cancellation?.Cancel(); _cancellation?.Dispose(); base.OnClosed(e); }
}
