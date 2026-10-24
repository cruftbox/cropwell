// Ported from waggle_dance/providers/gemini.py, licensed under the waggle-dance MIT License.
using Cropwell.Models;

namespace Cropwell.Providers;

public sealed class GeminiProvider(ProviderSettings settings, string? apiKey) : IAltTextProvider
{
    public string DisplayName => settings.DisplayName;
    public string Model => settings.Model;

    public async Task<string> AltTextAsync(string mediaType, string imageBase64, string instruction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ProviderException("No API key is configured for Gemini.");
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://generativelanguage.googleapis.com" : settings.BaseUrl.TrimEnd('/');
        var body = new
        {
            model = settings.Model,
            input = new object[]
            {
                new { type = "image", data = imageBase64, mime_type = mediaType },
                new { type = "text", text = "Write alt text for this image." }
            },
            system_instruction = instruction,
            generation_config = new { max_output_tokens = settings.MaxOutputTokens, thinking_level = settings.ReasoningEffort },
            store = false
        };
        using var json = await ProviderHttp.PostAsync(new Uri(baseUrl + "/v1beta/interactions"), body,
            new Dictionary<string, string> { ["x-goog-api-key"] = apiKey }, cancellationToken);
        if (json.RootElement.TryGetProperty("status", out var status) && status.GetString() is string value && value != "completed")
            throw new ProviderException($"The Gemini request ended with status {value}.");

        string? result = null;
        if (json.RootElement.TryGetProperty("steps", out var steps))
            foreach (var step in steps.EnumerateArray())
                if (step.TryGetProperty("type", out var type) && type.GetString() == "model_output" && step.TryGetProperty("content", out var content))
                {
                    var parts = content.EnumerateArray()
                        .Where(x => x.TryGetProperty("type", out var partType) && partType.GetString() == "text" && x.TryGetProperty("text", out _))
                        .Select(x => x.GetProperty("text").GetString());
                    result = string.Concat(parts);
                }
        return ProviderText.Require(result);
    }
}
