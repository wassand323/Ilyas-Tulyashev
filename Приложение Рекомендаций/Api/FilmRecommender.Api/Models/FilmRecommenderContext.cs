using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace FilmRecommender.Api.Models;

public partial class FilmRecommenderContext : DbContext
{
    public FilmRecommenderContext()
    {
    }

    public FilmRecommenderContext(DbContextOptions<FilmRecommenderContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Film> Films { get; set; }

    public virtual DbSet<Genre> Genres { get; set; }

    public virtual DbSet<Person> Persons { get; set; }

    public virtual DbSet<Rating> Ratings { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
        => optionsBuilder.UseMySql("server=localhost;password=1111;database=film_recommender;user=root;port=3306", Microsoft.EntityFrameworkCore.ServerVersion.Parse("8.0.46-mysql"));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Film>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("films");

            entity.HasIndex(e => e.ActorId, "fk_films_actor");

            entity.HasIndex(e => e.ActressId, "fk_films_actress");

            entity.HasIndex(e => e.DirectorId, "fk_films_director");

            entity.HasIndex(e => e.GenreId, "fk_films_genre");

            entity.HasIndex(e => e.Popularity, "idx_films_popularity");

            entity.HasIndex(e => e.Title, "idx_films_title");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.ActorId).HasColumnName("actor_id");
            entity.Property(e => e.ActressId).HasColumnName("actress_id");
            entity.Property(e => e.DirectorId).HasColumnName("director_id");
            entity.Property(e => e.GenreId).HasColumnName("genre_id");
            entity.Property(e => e.HasAwards).HasColumnName("has_awards");
            entity.Property(e => e.Image)
                .HasMaxLength(100)
                .HasColumnName("image");
            entity.Property(e => e.LengthMin).HasColumnName("length_min");
            entity.Property(e => e.Popularity).HasColumnName("popularity");
            entity.Property(e => e.ReleaseYear).HasColumnName("release_year");
            entity.Property(e => e.Title).HasColumnName("title");

            entity.HasOne(d => d.Actor).WithMany(p => p.FilmActors)
                .HasForeignKey(d => d.ActorId)
                .HasConstraintName("fk_films_actor");

            entity.HasOne(d => d.Actress).WithMany(p => p.FilmActresses)
                .HasForeignKey(d => d.ActressId)
                .HasConstraintName("fk_films_actress");

            entity.HasOne(d => d.Director).WithMany(p => p.FilmDirectors)
                .HasForeignKey(d => d.DirectorId)
                .HasConstraintName("fk_films_director");

            entity.HasOne(d => d.Genre).WithMany(p => p.Films)
                .HasForeignKey(d => d.GenreId)
                .HasConstraintName("fk_films_genre");
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("genres");

            entity.HasIndex(e => e.Name, "name").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name)
                .HasMaxLength(50)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("persons");

            entity.HasIndex(e => e.FullName, "idx_persons_name");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("full_name");
        });

        modelBuilder.Entity<Rating>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.FilmId })
                .HasName("PRIMARY")
                .HasAnnotation("MySql:IndexPrefixLength", new[] { 0, 0 });

            entity.ToTable("ratings");

            entity.HasIndex(e => e.FilmId, "idx_ratings_film");

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.FilmId).HasColumnName("film_id");
            entity.Property(e => e.RatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("rated_at");
            entity.Property(e => e.Rating1).HasColumnName("rating");

            entity.HasOne(d => d.Film).WithMany(p => p.Ratings)
                .HasForeignKey(d => d.FilmId)
                .HasConstraintName("fk_ratings_film");

            entity.HasOne(d => d.User).WithMany(p => p.Ratings)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("fk_ratings_user");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("users");

            entity.HasIndex(e => e.Username, "username").IsUnique();

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("created_at");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
