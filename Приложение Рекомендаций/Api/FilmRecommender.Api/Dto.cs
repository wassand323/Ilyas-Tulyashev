namespace FilmRecommender.Api;

public class FilmDto
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public int ReleaseYear { get; set; }
    public int? LengthMin { get; set; }
    public string? Genre { get; set; }
    public string? Actor { get; set; }
    public string? Actress { get; set; }
    public string? Director { get; set; }
    public int? Popularity { get; set; }
    public bool HasAwards { get; set; }
    public decimal? AvgRating { get; set; }
    public int RatingsCount { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int RatingsCount { get; set; }
}

public class UserRatingDto
{
    public int FilmId { get; set; }
    public string Title { get; set; } = "";
    public int ReleaseYear { get; set; }
    public string? Genre { get; set; }
    public int Rating { get; set; }
    public DateTime RatedAt { get; set; }
}

public class RatingRow
{
    public int UserId { get; set; }
    public int FilmId { get; set; }
    public int Rating { get; set; }
}

/// <summary>Элемент списка рекомендаций. Score - релевантность 0..100.</summary>
public class RecommendationDto
{
    public int FilmId { get; set; }
    public string Title { get; set; } = "";
    public int ReleaseYear { get; set; }
    public string? Genre { get; set; }
    public string? Director { get; set; }
    public int? Popularity { get; set; }
    public double Score { get; set; }
    public string Reason { get; set; } = "";
}

public record CreateUserRequest(string? Username);
public record RateRequest(int UserId, int FilmId, int Rating);
public record HelpItem(string Method, string Url, string Parameters, string Description);
