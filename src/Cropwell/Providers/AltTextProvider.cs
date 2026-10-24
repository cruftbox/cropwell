using Cropwell.Models;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Cropwell.Services;

namespace Cropwell.Providers;

public interface IAltTextProvider
{
    string DisplayName { get; }
    string Model { get; }
    Task<string> AltTextAsync(string mediaType, string imageBase64, string instruction, CancellationToken cancellationToken);
}

public sealed class ProviderException(string message, int? statusCode = null, Exception? inner = null) : Exception(message, inner)
{
    public int? StatusCode { get; } = statusCode;
}

public static class ProviderFactory
{
    public static IAltTextProvider Create(string id, ProviderSettings settings, string? key) => settings.Type switch
    {
        "anthropic" => new AnthropicProvider(settings, key),
        "openai_responses" => new OpenAiResponsesProvider(settings, key),
        "gemini" => new GeminiProvider(settings, key),
        "mock" => new MockProvider(settings),
        _ => throw new ProviderException("The selected provider is not supported.")
    };
}

public static class ProviderValidator
{
    private const string Instruction = "Return one short sentence describing the image.";

    public static async Task ValidateAsync(IAltTextProvider provider, CancellationToken cancellationToken)
    {
        var pixel = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 255, 255, 255, 255 }, 4);
        var image = ApiImageEncoder.Encode(pixel);
        _ = await provider.AltTextAsync(image.MediaType, image.Base64, Instruction, cancellationToken);
    }
}
