using FilmRecommender.Api;
using FilmRecommender.Api.Models;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:5080");

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default");
builder.Services.AddDbContext<FilmRecommenderContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 36))));
builder.Services.AddScoped<Repository>();
builder.Services.AddScoped<Recommender>();

var app = builder.Build();

app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    ctx.Response.StatusCode = 500;
    await ctx.Response.WriteAsJsonAsync(new { error = "Ошибка сервера: " + ex?.Message });
}));

static int Cnt(int? c) => Math.Clamp(c ?? 10, 1, 50);
static IResult NoUser(int id) => Results.NotFound(new { error = $"Пользователь {id} не найден" });
static IResult NoFilm(int id) => Results.NotFound(new { error = $"Фильм {id} не найден" });

var api = app.MapGroup("/api");

// ---------- Справка ----------
var help = new List<HelpItem>
{
    new("GET",  "/api/help", "-", "Справка по всем командам API"),
    new("GET",  "/api/films/top", "count (необязательно, по умолчанию 10, максимум 50)", "Топ популярных фильмов"),
    new("GET",  "/api/films", "search - часть названия/имени; genre - жанр; skip (0); take (50, максимум 200)", "Поиск по каталогу фильмов"),
    new("GET",  "/api/films/{id}", "id - номер фильма", "Информация об одном фильме"),
    new("GET",  "/api/genres", "-", "Список жанров"),
    new("GET",  "/api/users", "-", "Список пользователей"),
    new("POST", "/api/users", "JSON: { \"username\": \"имя\" }", "Создать пользователя"),
    new("GET",  "/api/users/{id}/ratings", "id - номер пользователя", "Оценки пользователя"),
    new("POST", "/api/ratings", "JSON: { \"userId\": 1, \"filmId\": 10, \"rating\": 1..5 }", "Поставить или изменить оценку фильму"),
    new("GET",  "/api/recommendations/user-based/{userId}", "userId; count (10)", "Коллаборативная фильтрация по пользователям (похожие пользователи)"),
    new("GET",  "/api/recommendations/item-based/{userId}", "userId; count (10)", "Коллаборативная фильтрация по фильмам (на основе ваших оценок)"),
    new("GET",  "/api/recommendations/similar/{filmId}", "filmId; count (10)", "Фильмы, которые зрители оценивают так же, как выбранный (item-based)"),
    new("GET",  "/api/recommendations/content/{filmId}", "filmId; count (10)", "Рекомендации по контенту: жанр, режиссёр, актёры, год, награды"),
    new("GET",  "/api/recommendations/personal/{userId}", "userId; count (10)", "Персональные рекомендации (гибрид всех методов)"),
};
api.MapGet("/help", () => help);

// ---------- Фильмы ----------
api.MapGet("/films/top", async (Repository repo, int? count) => await repo.GetTopAsync(Cnt(count)));

api.MapGet("/films", async (Repository repo, string? search, string? genre, int? skip, int? take) =>
    await repo.SearchAsync(search, genre, Math.Max(0, skip ?? 0), Math.Clamp(take ?? 50, 1, 200)));

api.MapGet("/films/{id:int}", async (Repository repo, int id) =>
    await repo.GetFilmAsync(id) is { } film ? Results.Ok(film) : NoFilm(id));

api.MapGet("/genres", async (Repository repo) => await repo.GetGenresAsync());

// ---------- Пользователи и оценки ----------
api.MapGet("/users", async (Repository repo) => await repo.GetUsersAsync());

api.MapPost("/users", async (Repository repo, CreateUserRequest req) =>
{
    var name = req.Username?.Trim();
    if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 50)
        return Results.BadRequest(new { error = "Имя пользователя: от 2 до 50 символов" });
    if (await repo.UsernameExistsAsync(name))
        return Results.Conflict(new { error = $"Пользователь «{name}» уже существует" });
    var id = await repo.CreateUserAsync(name);
    return Results.Created($"/api/users/{id}", await repo.GetUserAsync(id));
});

api.MapGet("/users/{id:int}/ratings", async (Repository repo, int id) =>
    await repo.GetUserAsync(id) is null ? NoUser(id) : Results.Ok(await repo.GetUserRatingsAsync(id)));

api.MapPost("/ratings", async (Repository repo, RateRequest req) =>
{
    if (req.Rating is < 1 or > 5) return Results.BadRequest(new { error = "Оценка должна быть от 1 до 5" });
    if (await repo.GetUserAsync(req.UserId) is null) return NoUser(req.UserId);
    if (await repo.GetFilmAsync(req.FilmId) is null) return NoFilm(req.FilmId);
    await repo.SetRatingAsync(req.UserId, req.FilmId, req.Rating);
    return Results.Ok(new { message = "Оценка сохранена" });
});

// ---------- Рекомендации ----------
var rec = api.MapGroup("/recommendations");

rec.MapGet("/user-based/{userId:int}", async (Repository repo, Recommender r, int userId, int? count) =>
    await repo.GetUserAsync(userId) is null ? NoUser(userId) : Results.Ok(await r.UserBasedAsync(userId, Cnt(count))));

rec.MapGet("/item-based/{userId:int}", async (Repository repo, Recommender r, int userId, int? count) =>
    await repo.GetUserAsync(userId) is null ? NoUser(userId) : Results.Ok(await r.ItemBasedAsync(userId, Cnt(count))));

rec.MapGet("/personal/{userId:int}", async (Repository repo, Recommender r, int userId, int? count) =>
    await repo.GetUserAsync(userId) is null ? NoUser(userId) : Results.Ok(await r.PersonalAsync(userId, Cnt(count))));

rec.MapGet("/similar/{filmId:int}", async (Recommender r, int filmId, int? count) =>
    await r.SimilarByRatingsAsync(filmId, Cnt(count)) is { } list ? Results.Ok(list) : NoFilm(filmId));

rec.MapGet("/content/{filmId:int}", async (Recommender r, int filmId, int? count) =>
    await r.ContentAsync(filmId, Cnt(count)) is { } list ? Results.Ok(list) : NoFilm(filmId));

app.MapGet("/", () => Results.Redirect("/api/help"));

app.Run();
