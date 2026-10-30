using FilmRecommender.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace FilmRecommender.Api;

/// <summary>Доступ к данным через EF Core (контекст создан scaffold-ом из БД).</summary>
public class Repository
{
    private readonly FilmRecommenderContext _db;
    public Repository(FilmRecommenderContext db) => _db = db;

    private static IQueryable<FilmDto> Project(IQueryable<Film> films) =>
        films.Select(f => new FilmDto
        {
            Id = f.Id,
            Title = f.Title,
            ReleaseYear = f.ReleaseYear,
            LengthMin = f.LengthMin,
            Genre = f.Genre != null ? f.Genre.Name : null,
            Actor = f.Actor != null ? f.Actor.FullName : null,
            Actress = f.Actress != null ? f.Actress.FullName : null,
            Director = f.Director != null ? f.Director.FullName : null,
            Popularity = f.Popularity,
            HasAwards = f.HasAwards,
            AvgRating = f.Ratings.Average(r => (decimal?)r.Rating1),
            RatingsCount = f.Ratings.Count()
        });

    public Task<List<FilmDto>> GetAllFilmsAsync() =>
        Project(_db.Films.AsNoTracking()).ToListAsync();

    public Task<List<FilmDto>> GetTopAsync(int count) =>
        Project(_db.Films.AsNoTracking())
            .OrderByDescending(f => f.Popularity ?? 0)
            .ThenByDescending(f => f.RatingsCount)
            .ThenByDescending(f => f.AvgRating)
            .ThenBy(f => f.Title)
            .Take(count)
            .ToListAsync();

    public Task<List<FilmDto>> SearchAsync(string? search, string? genre, int skip, int take)
    {
        IQueryable<Film> q = _db.Films.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(f => f.Title.Contains(s)
                          || (f.Director != null && f.Director.FullName.Contains(s))
                          || (f.Actor != null && f.Actor.FullName.Contains(s))
                          || (f.Actress != null && f.Actress.FullName.Contains(s)));
        }
        if (!string.IsNullOrWhiteSpace(genre))
        {
            var g = genre.Trim();
            q = q.Where(f => f.Genre != null && f.Genre.Name == g);
        }
        return Project(q.OrderBy(f => f.Title)).Skip(skip).Take(take).ToListAsync();
    }

    public Task<FilmDto?> GetFilmAsync(int id) =>
        Project(_db.Films.AsNoTracking().Where(f => f.Id == id)).FirstOrDefaultAsync();

    public Task<List<string>> GetGenresAsync() =>
        _db.Genres.AsNoTracking().OrderBy(g => g.Name).Select(g => g.Name).ToListAsync();

    private IQueryable<UserDto> UserQuery(IQueryable<User> users) =>
        users.Select(u => new UserDto
        {
            Id = u.Id,
            Username = u.Username,
            CreatedAt = u.CreatedAt,
            RatingsCount = u.Ratings.Count()
        });

    public Task<List<UserDto>> GetUsersAsync() =>
        UserQuery(_db.Users.AsNoTracking().OrderBy(u => u.Id)).ToListAsync();

    public Task<UserDto?> GetUserAsync(int id) =>
        UserQuery(_db.Users.AsNoTracking().Where(u => u.Id == id)).FirstOrDefaultAsync();

    public Task<bool> UsernameExistsAsync(string username) =>
        _db.Users.AnyAsync(u => u.Username == username);

    public async Task<int> CreateUserAsync(string username)
    {
        var user = new User { Username = username };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    public Task<List<UserRatingDto>> GetUserRatingsAsync(int userId) =>
        _db.Ratings.AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.RatedAt).ThenBy(r => r.Film.Title)
            .Select(r => new UserRatingDto
            {
                FilmId = r.FilmId,
                Title = r.Film.Title,
                ReleaseYear = r.Film.ReleaseYear,
                Genre = r.Film.Genre != null ? r.Film.Genre.Name : null,
                Rating = r.Rating1,
                RatedAt = r.RatedAt
            })
            .ToListAsync();

    /// <summary>Добавляет оценку или обновляет существующую.</summary>
    public async Task SetRatingAsync(int userId, int filmId, int rating)
    {
        var existing = await _db.Ratings.FindAsync(userId, filmId);
        if (existing == null)
            _db.Ratings.Add(new Rating { UserId = userId, FilmId = filmId, Rating1 = (sbyte)rating, RatedAt = DateTime.Now });
        else
        {
            existing.Rating1 = (sbyte)rating;
            existing.RatedAt = DateTime.Now;
        }
        await _db.SaveChangesAsync();
    }

    public Task<List<RatingRow>> GetAllRatingsAsync() =>
        _db.Ratings.AsNoTracking()
            .Select(r => new RatingRow { UserId = r.UserId, FilmId = r.FilmId, Rating = r.Rating1 })
            .ToListAsync();
}
