using System;
using System.Collections.Generic;

namespace FilmRecommender.Api.Models;

public partial class Film
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public int ReleaseYear { get; set; }

    public int? LengthMin { get; set; }

    public int? GenreId { get; set; }

    public int? ActorId { get; set; }

    public int? ActressId { get; set; }

    public int? DirectorId { get; set; }

    public int? Popularity { get; set; }

    public bool HasAwards { get; set; }

    public string? Image { get; set; }

    public virtual Person? Actor { get; set; }

    public virtual Person? Actress { get; set; }

    public virtual Person? Director { get; set; }

    public virtual Genre? Genre { get; set; }

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
