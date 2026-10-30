using System;
using System.Collections.Generic;

namespace FilmRecommender.Api.Models;

public partial class Person
{
    public int Id { get; set; }

    public string FullName { get; set; } = null!;

    public virtual ICollection<Film> FilmActors { get; set; } = new List<Film>();

    public virtual ICollection<Film> FilmActresses { get; set; } = new List<Film>();

    public virtual ICollection<Film> FilmDirectors { get; set; } = new List<Film>();
}
