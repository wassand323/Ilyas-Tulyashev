using System;
using System.Collections.Generic;

namespace FilmRecommender.Api.Models;

public partial class User
{
    public int Id { get; set; }

    public string Username { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Rating> Ratings { get; set; } = new List<Rating>();
}
