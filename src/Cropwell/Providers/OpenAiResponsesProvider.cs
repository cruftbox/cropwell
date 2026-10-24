// Ported from waggle_dance/providers/openai.py, licensed under the waggle-dance MIT License.
using Cropwell.Models;

namespace Cropwell.Providers;

public sealed class OpenAiResponsesProvider(ProviderSettings settings, string? apiKey) : IAltTextProvider
{
    public string DisplayName => settings.DisplayName;
    public string Model => settings.Model;

    public async Task<string> AltTextAsync(string mediaType, string imageBase64, string instruction, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey)) throw new ProviderException($"No API key is configured for {DisplayName}.");
        var baseUrl = string.IsNullOrWhiteSpace(settings.BaseUrl) ? "https://api.openai.com" : settings.BaseUrl.TrimEnd('/');
        var input = new[] { new { role = "user", content = new object[] {
            new { type = "input_image", image_url = $"data:{mediaType};base64,{imageBase64}" },
            new { type = "input_text", text = "Write alt text for this image." }
        } } };
        object body = string.IsNullOrWhiteSpace(settings.ReasoningEffort)
            ? new { model = settings.Model, max_output_tokens = settings.MaxOutputTokens, instructions = instruction, input, store = false }
            : new { model = settings.Model, max_output_tokens = settings.MaxOutputTokens, instructions = instruction, input, store = false, reasoning = new { effort = settings.ReasoningEffort } };
        using var json = await ProviderHttp.PostAsync(BuildResponsesUri(baseUrl), body,
            new Dictionary<string, string> { ["Authorization"] = "Bearer " + apiKey }, cancellationToken);
        if (json.RootElement.TryGetProperty("status", out var status) && status.GetString() == "incomplete")
        {
            var reason = json.RootElement.TryGetProperty("incomplete_details", out var details) && details.TryGetProperty("reason", out var detail)
                ? detail.GetString()
                : null;
            throw new ProviderException(reason == "max_output_tokens"
                ? "The provider reached the output limit before returning alt text."
                : "The provider returned an incomplete response.");
        }
        if (json.RootElement.TryGetProperty("error", out var error) && error.ValueKind is not (System.Text.Json.JsonValueKind.Null or System.Text.Json.JsonValueKind.Undefined))
            throw new ProviderException("The provider returned an error.");
        if (json.RootElement.TryGetProperty("output_text", out var outputText)) return ProviderText.Require(outputText.GetString());
        foreach (var output in json.RootElement.GetProperty("output").EnumerateArray())
            if (output.TryGetProperty("content", out var content))
                foreach (var part in content.EnumerateArray())
                    if (part.TryGetProperty("text", out var text)) return ProviderText.Require(text.GetString());
        throw new ProviderException("The provider returned no alt text.");
    }

    internal static Uri BuildResponsesUri(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        return new Uri(normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? normalized + "/responses"
            : normalized + "/v1/responses");
    }
}
