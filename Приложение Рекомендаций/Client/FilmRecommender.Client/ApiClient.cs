using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace FilmRecommender.Client;

public class ApiException : Exception
{
    public ApiException(string message) : base(message) { }
}

/// <summary>Обёртка над HTTP-запросами к FilmRecommender.Api.</summary>
public class ApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    private class ErrorDto { public string? Error { get; set; } }

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };
    }

    private async Task<T> GetAsync<T>(string url)
    {
        try
        {
            using var resp = await _http.GetAsync(url);
            await EnsureOk(resp);
            return (await resp.Content.ReadFromJsonAsync<T>(Json))!;
        }
        catch (HttpRequestException)
        {
            throw new ApiException($"Не удалось подключиться к API ({_http.BaseAddress}). Запущен ли проект FilmRecommender.Api?");
        }
    }

    private async Task PostAsync<TBody>(string url, TBody body)
    {
        try
        {
            using var resp = await _http.PostAsJsonAsync(url, body, Json);
            await EnsureOk(resp);
        }
        catch (HttpRequestException)
        {
            throw new ApiException($"Не удалось подключиться к API ({_http.BaseAddress}). Запущен ли проект FilmRecommender.Api?");
        }
    }

    private static async Task EnsureOk(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode) return;
        var msg = $"Ошибка API: {(int)resp.StatusCode} {resp.ReasonPhrase}";
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorDto>(Json);
            if (!string.IsNullOrEmpty(err?.Error)) msg = err.Error;
        }
        catch { /* тело ответа не JSON - оставляем стандартное сообщение */ }
        throw new ApiException(msg);
    }

    public Task<List<FilmDto>> GetTopAsync(int count = 10) => GetAsync<List<FilmDto>>($"api/films/top?count={count}");

    public Task<List<FilmDto>> SearchFilmsAsync(string? search, string? genre, int skip = 0, int take = 200) =>
        GetAsync<List<FilmDto>>(
            $"api/films?search={Uri.EscapeDataString(search ?? "")}&genre={Uri.EscapeDataString(genre ?? "")}&skip={skip}&take={take}");

    public Task<List<string>> GetGenresAsync() => GetAsync<List<string>>("api/genres");
    public Task<List<UserDto>> GetUsersAsync() => GetAsync<List<UserDto>>("api/users");
    public Task CreateUserAsync(string username) => PostAsync("api/users", new { username });
    public Task<List<UserRatingDto>> GetUserRatingsAsync(int userId) => GetAsync<List<UserRatingDto>>($"api/users/{userId}/ratings");
    public Task RateAsync(int userId, int filmId, int rating) => PostAsync("api/ratings", new { userId, filmId, rating });

    /// <param name="kind">personal | user-based | item-based | similar | content</param>
    /// <param name="id">userId (personal, user-based, item-based) или filmId (similar, content)</param>
    public Task<List<RecommendationDto>> GetRecommendationsAsync(string kind, int id, int count = 10) =>
        GetAsync<List<RecommendationDto>>($"api/recommendations/{kind}/{id}?count={count}");

    public Task<List<HelpItem>> GetHelpAsync() => GetAsync<List<HelpItem>>("api/help");
}
