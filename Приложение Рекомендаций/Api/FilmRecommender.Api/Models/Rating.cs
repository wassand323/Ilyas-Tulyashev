using System;
using System.Collections.Generic;

namespace FilmRecommender.Api.Models;

public partial class Rating
{
    public int UserId { get; set; }

    public int FilmId { get; set; }

    public sbyte Rating1 { get; set; }

    public DateTime RatedAt { get; set; }

    public virtual Film Film { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}
