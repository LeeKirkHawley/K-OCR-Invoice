using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
namespace K_OCR.Services;

public class KocrApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WorkspaceState _state;
    private readonly JsonSerializerOptions _jsonOptions;

    public KocrApiClient(IHttpClientFactory httpClientFactory, WorkspaceState state)
    {
        _httpClientFactory = httpClientFactory;
        _state = state;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("KocrApi");
        if (!string.IsNullOrWhiteSpace(_state.AuthToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _state.AuthToken);
        }
        else
        {
            client.DefaultRequestHeaders.Authorization = null;
        }

        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static async Task<string> FormatErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(body)
            ? $"{(int)response.StatusCode} {response.ReasonPhrase}"
            : $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        var response = await CreateClient().GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await FormatErrorAsync(response));

        return await response.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken)
            ?? throw new HttpRequestException("The API returned an empty response.");
    }

    public async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken cancellationToken = default)
    {
        var response = await CreateClient().PostAsJsonAsync(path, payload, _jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await FormatErrorAsync(response));

        return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken)
            ?? throw new HttpRequestException("The API returned an empty response.");
    }

    public async Task PostAsync<TRequest>(string path, TRequest payload, CancellationToken cancellationToken = default)
    {
        var response = await CreateClient().PostAsJsonAsync(path, payload, _jsonOptions, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(await FormatErrorAsync(response));
    }
}
