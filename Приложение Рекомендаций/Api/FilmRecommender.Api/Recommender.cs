using System.Globalization;

namespace FilmRecommender.Api;

/// <summary>Снимок данных в памяти: фильмы и матрица оценок.</summary>
internal class DataSet
{
    public List<FilmDto> Films { get; } = new();
    public Dictionary<int, FilmDto> FilmById { get; } = new();
    /// <summary>userId -> (filmId -> оценка)</summary>
    public Dictionary<int, Dictionary<int, double>> ByUser { get; } = new();
    /// <summary>filmId -> (userId -> оценка)</summary>
    public Dictionary<int, Dictionary<int, double>> ByFilm { get; } = new();
    public Dictionary<int, double> Mean { get; } = new();
}

/// <summary>
/// Рекомендательная система:
///  - коллаборативная фильтрация по пользователям (user-based, коэффициент Пирсона);
///  - коллаборативная фильтрация по фильмам (item-based, скорректированный косинус);
///  - контентная рекомендация (жанр, режиссёр, актёры, год, награды);
///  - персональные рекомендации (гибрид трёх методов).
/// </summary>
public class Recommender
{
    private readonly Repository _repo;
    public Recommender(Repository repo) => _repo = repo;

    private const int Neighbors = 20;      // число соседей в user-based
    private const int MinCommon = 2;       // минимум общих оценок для расчёта сходства
    private const double Damping = 1.0;    // демпфирование прогноза при малой поддержке

    private async Task<DataSet> LoadAsync()
    {
        var ds = new DataSet();
        foreach (var f in await _repo.GetAllFilmsAsync())
        {
            ds.Films.Add(f);
            ds.FilmById[f.Id] = f;
        }
        foreach (var r in await _repo.GetAllRatingsAsync())
        {
            if (!ds.ByUser.TryGetValue(r.UserId, out var u)) ds.ByUser[r.UserId] = u = new();
            if (!ds.ByFilm.TryGetValue(r.FilmId, out var f)) ds.ByFilm[r.FilmId] = f = new();
            u[r.FilmId] = r.Rating;
            f[r.UserId] = r.Rating;
        }
        foreach (var (uid, ratings) in ds.ByUser) ds.Mean[uid] = ratings.Values.Average();
        return ds;
    }

    // ===================== Публичные методы =====================

    /// <summary>Коллаборативная фильтрация по пользователям.</summary>
    public async Task<List<RecommendationDto>> UserBasedAsync(int userId, int count)
    {
        var ds = await LoadAsync();
        var preds = UserCf(ds, userId);
        var list = preds
            .OrderByDescending(p => p.Value.Pred).ThenByDescending(p => p.Value.Support)
            .Take(count)
            .Select(p => ToRec(ds.FilmById[p.Key], (p.Value.Pred - 1) / 4 * 100,
                $"прогноз оценки {F(p.Value.Pred)}; советуют похожие пользователи: {p.Value.Support}"))
            .ToList();
        FillWithPopular(list, ds, MyFilms(ds, userId), count, "популярный фильм (мало данных для прогноза)");
        return list;
    }

    /// <summary>Коллаборативная фильтрация по фильмам (на основе оценок пользователя).</summary>
    public async Task<List<RecommendationDto>> ItemBasedAsync(int userId, int count)
    {
        var ds = await LoadAsync();
        var preds = ItemCf(ds, userId);
        var list = preds
            .OrderByDescending(p => p.Value.Pred).ThenByDescending(p => p.Value.Support)
            .Take(count)
            .Select(p => ToRec(ds.FilmById[p.Key], (p.Value.Pred - 1) / 4 * 100,
                $"прогноз оценки {F(p.Value.Pred)}; похож на фильмы, которые вы оценили: {p.Value.Support}"))
            .ToList();
        FillWithPopular(list, ds, MyFilms(ds, userId), count, "популярный фильм (мало данных для прогноза)");
        return list;
    }

    /// <summary>Фильмы, которые пользователи оценивают так же, как заданный фильм (item-based).</summary>
    public async Task<List<RecommendationDto>?> SimilarByRatingsAsync(int filmId, int count)
    {
        var ds = await LoadAsync();
        if (!ds.FilmById.TryGetValue(filmId, out var src)) return null;

        var list = new List<RecommendationDto>();
        if (ds.ByFilm.ContainsKey(filmId))
        {
            list = ds.ByFilm.Keys.Where(j => j != filmId)
                .Select(j => (Id: j, Sim: ItemSim(ds, filmId, j)))
                .Where(x => x.Sim > 0)
                .OrderByDescending(x => x.Sim)
                .Take(count)
                .Select(x => ToRec(ds.FilmById[x.Id], x.Sim * 100, $"оценки зрителей схожи (сходство {F(x.Sim)})"))
                .ToList();
        }
        // если оценок мало - дополняем контентно похожими
        var used = new HashSet<int>(list.Select(r => r.FilmId)) { filmId };
        foreach (var f in ds.Films.Where(f => !used.Contains(f.Id))
                     .OrderByDescending(f => ContentSim(src, f)).ThenByDescending(f => f.Popularity ?? 0))
        {
            if (list.Count >= count) break;
            list.Add(ToRec(f, ContentSim(src, f) * 100, "похож по контенту (мало оценок): " + ContentReason(src, f)));
        }
        return list;
    }

    /// <summary>Контентная рекомендация: фильмы, похожие на заданный по описанию.</summary>
    public async Task<List<RecommendationDto>?> ContentAsync(int filmId, int count)
    {
        var ds = await LoadAsync();
        if (!ds.FilmById.TryGetValue(filmId, out var src)) return null;
        return ds.Films.Where(f => f.Id != filmId)
            .Select(f => (Film: f, Sim: ContentSim(src, f)))
            .OrderByDescending(x => x.Sim).ThenByDescending(x => x.Film.Popularity ?? 0)
            .Take(count)
            .Select(x => ToRec(x.Film, x.Sim * 100, ContentReason(src, x.Film)))
            .ToList();
    }

    /// <summary>Персональные рекомендации: гибрид user-based + item-based + контентный профиль.</summary>
    public async Task<List<RecommendationDto>> PersonalAsync(int userId, int count)
    {
        var ds = await LoadAsync();
        var list = new List<RecommendationDto>();

        if (!ds.ByUser.ContainsKey(userId) || ds.ByUser[userId].Count == 0)
        {
            FillWithPopular(list, ds, new HashSet<int>(), count, "популярный фильм (у пользователя пока нет оценок)");
            return list;
        }

        var u = UserCf(ds, userId);
        var i = ItemCf(ds, userId);
        var c = ContentForUser(ds, userId);

        double neutral = (ds.Mean[userId] - 1) / 4;      // нейтральный прогноз = средняя оценка пользователя
        double cmax = c.Count > 0 ? c.Values.Max() : 1;
        if (cmax <= 0) cmax = 1;

        var scored = new List<(FilmDto Film, double Final, string Reason)>();
        foreach (var (filmId, cv) in c)
        {
            double cn = cv / cmax;
            bool hasU = u.TryGetValue(filmId, out var uv);
            bool hasI = i.TryGetValue(filmId, out var iv);
            double un = hasU ? (uv.Pred - 1) / 4 : neutral;
            double inn = hasI ? (iv.Pred - 1) / 4 : neutral;
            double final = 0.40 * un + 0.35 * inn + 0.25 * cn;

            var parts = new List<string>();
            if (hasU) parts.Add($"пользователи {F(uv.Pred)}");
            if (hasI) parts.Add($"фильмы {F(iv.Pred)}");
            parts.Add($"контент {cn * 100:F0}%");
            scored.Add((ds.FilmById[filmId], final, "гибрид: " + string.Join(", ", parts)));
        }

        return scored.OrderByDescending(s => s.Final).ThenByDescending(s => s.Film.Popularity ?? 0)
            .Take(count)
            .Select(s => ToRec(s.Film, s.Final * 100, s.Reason))
            .ToList();
    }

    // ===================== Алгоритмы =====================

    /// <summary>User-based CF: прогноз = средняя оценка + взвешенные отклонения соседей.</summary>
    private static Dictionary<int, (double Pred, int Support)> UserCf(DataSet ds, int userId)
    {
        var result = new Dictionary<int, (double, int)>();
        if (!ds.ByUser.TryGetValue(userId, out var mine) || mine.Count == 0) return result;
        double meanU = ds.Mean[userId];

        var neighbors = new List<(int Id, double Sim)>();
        foreach (var (v, theirs) in ds.ByUser)
        {
            if (v == userId) continue;
            double meanV = ds.Mean[v], dot = 0, na = 0, nb = 0;
            int common = 0;
            foreach (var (film, r) in mine)
            {
                if (!theirs.TryGetValue(film, out var r2)) continue;
                double a = r - meanU, b = r2 - meanV;
                dot += a * b; na += a * a; nb += b * b; common++;
            }
            if (common < MinCommon || na <= 0 || nb <= 0) continue;
            double sim = dot / Math.Sqrt(na * nb) * Math.Min(common, 10) / 10.0;   // Пирсон с поправкой на число общих оценок
            if (sim > 0) neighbors.Add((v, sim));
        }

        var top = neighbors.OrderByDescending(n => n.Sim).Take(Neighbors).ToList();
        var num = new Dictionary<int, double>();
        var den = new Dictionary<int, double>();
        var sup = new Dictionary<int, int>();
        foreach (var (v, sim) in top)
        {
            double meanV = ds.Mean[v];
            foreach (var (film, r) in ds.ByUser[v])
            {
                if (mine.ContainsKey(film)) continue;
                num[film] = num.GetValueOrDefault(film) + sim * (r - meanV);
                den[film] = den.GetValueOrDefault(film) + sim;
                sup[film] = sup.GetValueOrDefault(film) + 1;
            }
        }
        foreach (var film in num.Keys)
            result[film] = (Math.Clamp(meanU + num[film] / (den[film] + Damping), 1, 5), sup[film]);
        return result;
    }

    /// <summary>Item-based CF: прогноз по сходству кандидата с фильмами, которые оценил пользователь.</summary>
    private static Dictionary<int, (double Pred, int Support)> ItemCf(DataSet ds, int userId)
    {
        var result = new Dictionary<int, (double, int)>();
        if (!ds.ByUser.TryGetValue(userId, out var mine) || mine.Count == 0) return result;
        double meanU = ds.Mean[userId];

        foreach (var film in ds.ByFilm.Keys)
        {
            if (mine.ContainsKey(film)) continue;
            double num = 0, den = 0;
            int sup = 0;
            foreach (var (g, r) in mine)
            {
                double s = ItemSim(ds, film, g);
                if (s <= 0) continue;
                num += s * (r - meanU); den += s; sup++;
            }
            if (sup == 0) continue;
            result[film] = (Math.Clamp(meanU + num / (den + Damping), 1, 5), sup);
        }
        return result;
    }

    /// <summary>Скорректированный косинус между фильмами (оценки центрируются по среднему пользователя).</summary>
    private static double ItemSim(DataSet ds, int i, int j)
    {
        if (!ds.ByFilm.TryGetValue(i, out var a) || !ds.ByFilm.TryGetValue(j, out var b)) return 0;
        if (a.Count > b.Count) (a, b) = (b, a);
        double dot = 0, na = 0, nb = 0;
        int common = 0;
        foreach (var (u, ra) in a)
        {
            if (!b.TryGetValue(u, out var rb)) continue;
            double m = ds.Mean[u], x = ra - m, y = rb - m;
            dot += x * y; na += x * x; nb += y * y; common++;
        }
        if (common < MinCommon || na <= 0 || nb <= 0) return 0;
        return dot / Math.Sqrt(na * nb) * Math.Min(common, 10) / 10.0;
    }

    /// <summary>Сходство двух фильмов по описанию, 0..1.</summary>
    private static double ContentSim(FilmDto a, FilmDto b)
    {
        double s = 0;
        if (a.Genre != null && a.Genre == b.Genre) s += 3;
        if (a.Director != null && a.Director == b.Director) s += 3;
        if (a.Actor != null && a.Actor == b.Actor) s += 2;
        if (a.Actress != null && a.Actress == b.Actress) s += 2;
        s += Math.Max(0, 1 - Math.Abs(a.ReleaseYear - b.ReleaseYear) / 30.0);
        if (a.HasAwards && b.HasAwards) s += 0.5;
        return s / 11.5;
    }

    private static string ContentReason(FilmDto a, FilmDto b)
    {
        var p = new List<string>();
        if (a.Genre != null && a.Genre == b.Genre) p.Add("жанр " + a.Genre);
        if (a.Director != null && a.Director == b.Director) p.Add("режиссёр " + a.Director);
        if (a.Actor != null && a.Actor == b.Actor) p.Add("актёр " + a.Actor);
        if (a.Actress != null && a.Actress == b.Actress) p.Add("актриса " + a.Actress);
        if (Math.Abs(a.ReleaseYear - b.ReleaseYear) <= 5) p.Add("близкий год");
        return p.Count > 0 ? "совпадает: " + string.Join(", ", p) : "небольшое сходство по году выпуска";
    }

    /// <summary>Контентный профиль пользователя: сходство кандидатов с любимыми фильмами (оценка >= 4).</summary>
    private static Dictionary<int, double> ContentForUser(DataSet ds, int userId)
    {
        var res = new Dictionary<int, double>();
        if (!ds.ByUser.TryGetValue(userId, out var mine) || mine.Count == 0) return res;

        var liked = mine.Where(kv => kv.Value >= 4).OrderByDescending(kv => kv.Value).Take(30).ToList();
        if (liked.Count == 0) liked = mine.OrderByDescending(kv => kv.Value).Take(10).ToList();
        double wsum = liked.Sum(kv => kv.Value);

        foreach (var f in ds.Films)
        {
            if (mine.ContainsKey(f.Id)) continue;
            double s = 0;
            foreach (var (id, r) in liked) s += r * ContentSim(ds.FilmById[id], f);
            res[f.Id] = s / wsum;
        }
        return res;
    }

    // ===================== Вспомогательное =====================

    private static HashSet<int> MyFilms(DataSet ds, int userId) =>
        ds.ByUser.TryGetValue(userId, out var m) ? new HashSet<int>(m.Keys) : new HashSet<int>();

    private static void FillWithPopular(List<RecommendationDto> list, DataSet ds, HashSet<int> exclude, int count, string reason)
    {
        var used = new HashSet<int>(exclude);
        foreach (var r in list) used.Add(r.FilmId);
        foreach (var f in ds.Films.OrderByDescending(f => f.Popularity ?? 0).ThenByDescending(f => f.RatingsCount))
        {
            if (list.Count >= count) break;
            if (used.Contains(f.Id)) continue;
            list.Add(ToRec(f, f.Popularity ?? 0, reason));
        }
    }

    private static RecommendationDto ToRec(FilmDto f, double score, string reason) => new()
    {
        FilmId = f.Id, Title = f.Title, ReleaseYear = f.ReleaseYear, Genre = f.Genre,
        Director = f.Director, Popularity = f.Popularity,
        Score = Math.Round(score, 1), Reason = reason
    };

    private static string F(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
}
