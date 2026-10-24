// Behavior ported from waggle_dance/providers/mock.py, licensed under the waggle-dance MIT License.
using Cropwell.Models;

namespace Cropwell.Providers;

public sealed class MockProvider(ProviderSettings settings) : IAltTextProvider
{
    public string DisplayName => settings.DisplayName;
    public string Model => settings.Model;
    public async Task<string> AltTextAsync(string mediaType, string imageBase64, string instruction, CancellationToken cancellationToken)
    {
        await Task.Delay(700, cancellationToken);
        return "A sample image prepared for sharing on social media.";
    }
}

internal static class ProviderText
{
    public static string Require(string? value)
    {
        var result = value?.Trim();
        return string.IsNullOrWhiteSpace(result) ? throw new ProviderException("The provider returned no alt text.") : result;
    }
}
