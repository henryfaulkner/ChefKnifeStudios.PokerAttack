using Ardalis.Result;
using ChefKnifeStudios.PokerAttack.Shared;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ChefKnifeStudios.PokerAttack.Client.Core.Services;

public interface IHttpService
{
    Task<Result<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default);
    Task<Result<Y>> PostAsync<Y>(string url, FormUrlEncodedContent formContent, CancellationToken cancellationToken = default);
    Task<Result<Y>> PostAsync<X, Y>(string url, X data, CancellationToken cancellationToken = default);
    Task<Result<Y>> PutAsync<X, Y>(string url, X data, CancellationToken cancellationToken = default);
    Task<Result<T>> DeleteAsync<T>(string url, CancellationToken cancellationToken = default);
}

public class HttpService : IHttpService
{
    readonly Func<HttpClient> _httpClientFactory;
    string? _basicAuthHeaderValue;
    string? _bearerAuthHeaderValue;
    readonly Dictionary<string, string> _additionalHeaders = new();

    public HttpService(Func<HttpClient> httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Result<T>> GetAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.GetAsync(url, cancellationToken);

            return await HandleResponse<T>(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Result<T>.Error(ex.Message);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public async Task<Result<Y>> PostAsync<X, Y>(string url, X data, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(data, JsonOptions.Get()),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync(url, jsonContent, cancellationToken);
            return await HandleResponse<Y>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Y>.Error(ex.Message);
        }
    }

    public async Task<Result<Y>> PostAsync<Y>(string url, FormUrlEncodedContent formContent, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.PostAsync(url, formContent, cancellationToken);
            return await HandleResponse<Y>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Y>.Error(ex.Message);
        }
    }

    public async Task<Result<Y>> PutAsync<X, Y>(string url, X data, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var jsonContent = new StringContent(
                JsonSerializer.Serialize(data, JsonOptions.Get()),
                Encoding.UTF8,
                "application/json"
            );

            var response = await client.PutAsync(url, jsonContent, cancellationToken);
            return await HandleResponse<Y>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<Y>.Error(ex.Message);
        }
    }

    public async Task<Result<T>> DeleteAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = CreateClient();
            var response = await client.DeleteAsync(url, cancellationToken);

            return await HandleResponse<T>(response, cancellationToken);
        }
        catch (Exception ex)
        {
            return Result<T>.Error(ex.Message);
        }
    }

    public void SetBasicAuthentication(string username, string password)
    {
        _basicAuthHeaderValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
    }

    public void SetBearerAuthentication(string token)
    {
        _bearerAuthHeaderValue = token;
    }

    public void AddHeader(string key, string value)
    {
        _additionalHeaders[key] = value;
    }

    HttpClient CreateClient()
    {
        var client = _httpClientFactory();
        client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        client.DefaultRequestHeaders.Add("Accept", "application/json");

        if (_basicAuthHeaderValue != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthHeaderValue);
        }

        if (_bearerAuthHeaderValue != null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerAuthHeaderValue);
        }

        foreach (var header in _additionalHeaders)
        {
            client.DefaultRequestHeaders.Add(header.Key, header.Value);
        }

        return client;
    }

    async Task<Result<T>> HandleResponse<T>(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => Result<T>.NotFound(),
                HttpStatusCode.Unauthorized => Result<T>.Unauthorized(),
                HttpStatusCode.BadRequest => Result<T>.Invalid(new List<ValidationError>
                {
                    new("Http", content)
                }),
                _ => Result<T>.Error($"Request failed with {(int)response.StatusCode}: {content}")
            };
        }

        var obj = JsonSerializer.Deserialize<T>(content, JsonOptions.Get());
        return obj is null ? Result<T>.Error("Response deserialization returned null") : Result<T>.Success(obj);
    }
}
