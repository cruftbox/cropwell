// Ported from waggle_dance/providers/anthropic.py, licensed under the waggle-dance MIT License.
using Cropwell.Models;

namespace Cropwell.Providers;

public sealed class AnthropicProvider(ProviderSettings settings, string? apiKey) : IAltTextProvider
{
    public string DisplayName => settings.DisplayName;
    public string Model => settings.Model;

    public async Task<string> AltTextAsync(string mediaType, string imageBase64, string instruction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ProviderException("No API key is configured for Claude.");
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.anthropic.com" : settings.BaseUrl.TrimEnd('/');
        var body = new
        {
            model = settings.Model,
            max_tokens = settings.MaxOutputTokens,
            system = instruction,
            messages = new[] { new { role = "user", content = new object[] {
                new { type = "image", source = new { type = "base64", media_type = mediaType, data = imageBase64 } },
                new { type = "text", text = "Write alt text for this image." }
            } } }
        };
        using var json = await ProviderHttp.PostAsync(new Uri(baseUrl + "/v1/messages"), body,
            new Dictionary<string, string> { ["x-api-key"] = apiKey, ["anthropic-version"] = "2023-06-01" }, cancellationToken);
        if (json.RootElement.TryGetProperty("stop_reason", out var stopReason))
        {
            if (stopReason.GetString() == "refusal") throw new ProviderException("Claude declined to generate alt text.");
            if (stopReason.GetString() == "max_tokens") throw new ProviderException("Claude reached the output limit before returning alt text.");
        }
        var content = json.RootElement.GetProperty("content");
        var text = content.EnumerateArray().FirstOrDefault(x => x.TryGetProperty("type", out var type) && type.GetString() == "text");
        return ProviderText.Require(text.ValueKind == System.Text.Json.JsonValueKind.Undefined ? null : text.GetProperty("text").GetString());
    }
}
