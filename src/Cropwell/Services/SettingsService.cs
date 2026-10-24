using System.Text.Json;
using Cropwell.Models;

namespace Cropwell.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string SettingsPath { get; }

    public SettingsService(string? settingsPath = null)
    {
        SettingsPath = settingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cropwell", "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new AppSettings();
            MergeDefaults(settings);
            return settings;
        }
        catch (Exception ex)
        {
            AppLog.Warning("Settings could not be read; defaults will be used.", ex);
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporary, SettingsPath, true);
    }

    private static void MergeDefaults(AppSettings settings)
    {
        if (Environment.GetEnvironmentVariable("CROPWELL_MOCK") != "1") settings.Providers.Remove("mock");
        foreach (var (key, value) in ProviderDefaults.Create())
            if (!settings.Providers.TryGetValue(key, out var existing)) settings.Providers[key] = value;
            else if (string.IsNullOrWhiteSpace(existing.BaseUrl) && !string.IsNullOrWhiteSpace(value.BaseUrl)) existing.BaseUrl = value.BaseUrl;
        if (settings.Providers.TryGetValue("gemini", out var gemini) && gemini.ReasoningEffort == "minimal")
            gemini.ReasoningEffort = "low";
        if (!settings.Providers.ContainsKey(settings.ActiveProvider)) settings.ActiveProvider = "openai";
    }
}
