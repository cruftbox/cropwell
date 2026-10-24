// Behavior ported from waggle_dance/providers/base.py, licensed under the waggle-dance MIT License.
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Cropwell.Providers;

internal static class ProviderHttp
{
    private static readonly HttpClient Client = new();

    public static async Task<JsonDocument> PostAsync(Uri uri, object body, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                foreach (var (name, value) in headers) request.Headers.TryAddWithoutValidation(name, value);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                var payload = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                AppLogStatus(response.StatusCode);
                if (response.IsSuccessStatusCode) return JsonDocument.Parse(payload);
                if (!ShouldRetry(response.StatusCode) || attempt == 3)
                    throw new ProviderException(SafeHttpMessage(response.StatusCode), (int)response.StatusCode);
                last = new HttpRequestException($"HTTP {(int)response.StatusCode}", null, response.StatusCode);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                last = new TimeoutException("The provider request timed out.");
                if (attempt == 3) throw new ProviderException("The provider request timed out.", inner: last);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
                if (ex.StatusCode is not null && !ShouldRetry(ex.StatusCode.Value))
                    throw new ProviderException(SafeHttpMessage(ex.StatusCode.Value), (int)ex.StatusCode.Value, ex);
                if (attempt == 3) throw new ProviderException("Could not connect to the provider.", inner: ex);
            }
            var delay = Math.Pow(2, attempt - 1) + Random.Shared.NextDouble() * .5;
            await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken).ConfigureAwait(false);
        }
        throw new ProviderException("The provider request failed.", inner: last);
    }

    private static bool ShouldRetry(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    private static string SafeHttpMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "The provider rejected the request.",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The provider rejected the API key.",
        HttpStatusCode.TooManyRequests => "The provider rate limit was reached.",
        _ when (int)status >= 500 => "The provider service is unavailable.",
        _ => $"The provider request failed (HTTP {(int)status})."
    };

    private static void AppLogStatus(HttpStatusCode status) => Services.AppLog.Info($"Provider HTTP status {(int)status}");
}
