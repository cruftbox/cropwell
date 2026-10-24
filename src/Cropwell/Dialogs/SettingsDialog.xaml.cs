using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Cropwell.Models;
using Cropwell.Providers;
using Cropwell.Services;

namespace Cropwell.Dialogs;

public partial class SettingsDialog : Window
{
    private enum KeyAction { Unchanged, Replace }
    private readonly Dictionary<string, KeyAction> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _replacementKeys = new(StringComparer.OrdinalIgnoreCase);
    private string? _currentId;
    private bool _loading;
    private CancellationTokenSource? _validationCancellation;
    public AppSettings Result { get; }

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        Result = new AppSettings
        {
            ActiveProvider = settings.ActiveProvider,
            ExportFolder = settings.ExportFolder,
            Window = new WindowSettings { Left = settings.Window.Left, Top = settings.Window.Top, Width = settings.Window.Width, Height = settings.Window.Height },
            Providers = settings.Providers.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase)
        };
        foreach (var key in Result.Providers.Keys) _actions[key] = KeyAction.Unchanged;
        ExportFolderBox.Text = Result.ExportFolder;
        ProviderBox.ItemsSource = Result.Providers;
        ProviderBox.SelectedValue = Result.ActiveProvider;
    }

    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        SaveFields();
        if (ProviderBox.SelectedValue is not string id) return;
        _currentId = id;
        var provider = Result.Providers[id];
        _loading = true;
        ModelBox.Text = provider.Model;
        BaseUrlBox.Text = provider.BaseUrl;
        KeyBox.Password = _actions[id] switch
        {
            KeyAction.Replace => _replacementKeys[id],
            _ => CredentialStore.Read(id) is null ? "" : "••••••••"
        };
        _loading = false;
        StatusText.Text = "";
        UpdateKeySource();
    }

    private void SaveFields()
    {
        Result.ExportFolder = ExportFolderBox.Text.Trim();
        if (_currentId is null) return;
        Result.Providers[_currentId].Model = ModelBox.Text.Trim();
        Result.Providers[_currentId].BaseUrl = BaseUrlBox.Text.Trim();
    }

    private void BrowseExportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose export folder",
            InitialDirectory = Directory.Exists(ExportFolderBox.Text) ? ExportFolderBox.Text : null
        };
        if (dialog.ShowDialog(this) == true) ExportFolderBox.Text = dialog.FolderName;
    }

    private void ClearExportFolder_Click(object sender, RoutedEventArgs e) => ExportFolderBox.Text = "";

    private void KeyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading || _currentId is null) return;
        _actions[_currentId] = string.IsNullOrEmpty(KeyBox.Password) ? KeyAction.Unchanged : KeyAction.Replace;
        if (_actions[_currentId] == KeyAction.Replace) _replacementKeys[_currentId] = KeyBox.Password;
        UpdateKeySource();
    }

    private void UpdateKeySource()
    {
        if (_currentId is null) return;
        var provider = Result.Providers[_currentId];
        var source = _actions[_currentId] switch
        {
            KeyAction.Replace => "stored key (pending save)",
            _ => CredentialStore.Resolve(_currentId, provider).Source
        };
        KeySourceText.Text = "Key source: " + source;
    }

    private async void ValidateKey_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (_currentId is not string id) return;
        var providerSettings = Result.Providers[id];
        if (string.IsNullOrWhiteSpace(providerSettings.Model)) { ShowStatus("Enter a model name before validating.", false); return; }
        if (providerSettings.Type != "mock" && !Uri.TryCreate(providerSettings.BaseUrl, UriKind.Absolute, out _))
        { ShowStatus("Enter an absolute base URL before validating.", false); return; }

        var key = _actions[id] == KeyAction.Replace
            ? _replacementKeys[id]
            : CredentialStore.Resolve(id, providerSettings).Key;
        IAltTextProvider provider;
        try { provider = ProviderFactory.Create(id, providerSettings, key); }
        catch (ProviderException ex) { ShowStatus(ex.Message, false); return; }

        _validationCancellation?.Cancel();
        _validationCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _validationCancellation = cancellation;
        SetValidationBusy(true);
        ShowStatus($"Validating {provider.DisplayName}…", null);
        try
        {
            await ProviderValidator.ValidateAsync(provider, cancellation.Token);
            if (IsVisible) ShowStatus($"Valid for {provider.DisplayName} using {provider.Model}.", true);
        }
        catch (OperationCanceledException)
        {
            if (IsVisible) ShowStatus("Validation cancelled.", false);
        }
        catch (ProviderException ex)
        {
            AppLog.Error($"Provider validation failed: {provider.DisplayName} {provider.Model}", ex);
            if (IsVisible) ShowStatus(ex.Message, false);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Provider validation failed: {provider.DisplayName} {provider.Model}", ex);
            if (IsVisible) ShowStatus("Cropwell could not validate this provider.", false);
        }
        finally
        {
            if (_validationCancellation == cancellation)
            {
                _validationCancellation = null;
                cancellation.Dispose();
                if (IsVisible) SetValidationBusy(false);
            }
        }
    }

    private void SetValidationBusy(bool busy)
    {
        ProviderBox.IsEnabled = !busy;
        ModelBox.IsEnabled = !busy;
        BaseUrlBox.IsEnabled = !busy;
        KeyBox.IsEnabled = !busy;
        ValidateKeyButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string text, bool? success)
    {
        StatusText.Foreground = success == true ? Brushes.ForestGreen : success == false ? Brushes.Firebrick : Brushes.DimGray;
        StatusText.Text = success == true ? "✓ " + text : success == false ? "! " + text : text;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (ProviderBox.SelectedValue is not string id) return;
        if (!string.IsNullOrWhiteSpace(Result.ExportFolder) && !Directory.Exists(Result.ExportFolder))
        {
            ShowStatus("Choose an existing export folder, or leave the setting empty.", false);
            ExportFolderBox.Focus();
            return;
        }
        foreach (var provider in Result.Providers.Values)
        {
            if (string.IsNullOrWhiteSpace(provider.Model)) { ShowStatus("Every provider needs a model name.", false); return; }
            if (provider.Type != "mock" && !Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out _)) { ShowStatus("Every provider needs an absolute base URL.", false); return; }
        }
        try
        {
            foreach (var (providerId, action) in _actions)
            {
                if (action == KeyAction.Replace) CredentialStore.Write(providerId, _replacementKeys[providerId]);
            }
        }
        catch (Exception ex) { AppLog.Error("Credential Manager update failed", ex); ShowStatus("The API key could not be saved in Windows Credential Manager.", false); return; }
        Result.ActiveProvider = id;
        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _validationCancellation?.Cancel();
        base.OnClosed(e);
    }
}
