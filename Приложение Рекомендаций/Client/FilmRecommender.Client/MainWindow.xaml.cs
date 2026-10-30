using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace FilmRecommender.Client;

public partial class MainWindow : Window
{
    private const string ApiUrl = "http://localhost:5080";
    private readonly ApiClient _api = new(ApiUrl);
    private bool _loadingUsers;

    public MainWindow()
    {
        InitializeComponent();
    }


    private void SetStatus(string text) => StatusText.Text = text;

    private async Task Run(Func<Task> action)
    {
        try
        {
            SetStatus("Загрузка…");
            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка: " + ex.Message);
            MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private UserDto? RequireUser()
    {
        if (UserCombo.SelectedItem is UserDto user) return user;
        MessageBox.Show("Сначала выберите пользователя.", "Нет пользователя", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private FilmDto? RequireFilm()
    {
        if (FilmsGrid.SelectedItem is FilmDto film) return film;
        MessageBox.Show("Выберите фильм в таблице каталога.", "Нет фильма", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }


    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await Run(async () =>
        {
            await LoadUsersAsync(null);
            var genres = await _api.GetGenresAsync();
            GenreCombo.ItemsSource = new[] { "Все жанры" }.Concat(genres).ToList();
            GenreCombo.SelectedIndex = 0;

            TopGrid.ItemsSource = await _api.GetTopAsync(10);
            HelpGrid.ItemsSource = await _api.GetHelpAsync();
            await SearchFilmsAsync();
            await LoadMyRatingsAsync();
            SetStatus("Готово");
        });
    }

    private async Task LoadUsersAsync(int? selectId)
    {
        _loadingUsers = true;
        try
        {
            var users = await _api.GetUsersAsync();
            UserCombo.ItemsSource = users;
            UserCombo.SelectedItem = users.FirstOrDefault(u => u.Id == selectId) ?? users.FirstOrDefault();
        }
        finally
        {
            _loadingUsers = false;
        }
    }

    private async Task SearchFilmsAsync()
    {
        var genre = GenreCombo.SelectedIndex > 0 ? GenreCombo.SelectedItem as string : null;
        var films = await _api.SearchFilmsAsync(SearchBox.Text.Trim(), genre);
        FilmsGrid.ItemsSource = films;
        SetStatus($"Найдено фильмов: {films.Count} (показано не более 200)");
    }

    private async Task LoadMyRatingsAsync()
    {
        if (UserCombo.SelectedItem is not UserDto user) { MyRatingsGrid.ItemsSource = null; return; }
        MyRatingsGrid.ItemsSource = await _api.GetUserRatingsAsync(user.Id);
    }


    private async void UserCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingUsers || !IsLoaded) return;
        RecGrid.ItemsSource = null;
        RecTitle.Text = "";
        await Run(async () =>
        {
            await LoadMyRatingsAsync();
            SetStatus("Пользователь изменён");
        });
    }

    private async void NewUser_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InputDialog("Новый пользователь", "Введите имя пользователя:") { Owner = this };
        if (dlg.ShowDialog() != true) return;
        await Run(async () =>
        {
            await _api.CreateUserAsync(dlg.Value);
            var users = await _api.GetUsersAsync();
            var created = users.FirstOrDefault(u => u.Username.Equals(dlg.Value, StringComparison.OrdinalIgnoreCase));
            await LoadUsersAsync(created?.Id);
            await LoadMyRatingsAsync();
            SetStatus($"Пользователь «{dlg.Value}» создан");
        });
    }


    private async void RefreshTop_Click(object sender, RoutedEventArgs e) =>
        await Run(async () =>
        {
            TopGrid.ItemsSource = await _api.GetTopAsync(10);
            SetStatus("Топ-10 обновлён");
        });


    private async Task ShowUserRecsAsync(string kind, string title)
    {
        var user = RequireUser();
        if (user == null) return;
        await Run(async () =>
        {
            RecGrid.ItemsSource = await _api.GetRecommendationsAsync(kind, user.Id, 10);
            RecTitle.Text = $"{title} - {user.Username}";
            SetStatus("Рекомендации получены");
        });
    }

    private async void Personal_Click(object sender, RoutedEventArgs e) =>
        await ShowUserRecsAsync("personal", "Персональные рекомендации");

    private async void UserBased_Click(object sender, RoutedEventArgs e) =>
        await ShowUserRecsAsync("user-based", "Коллаборативная фильтрация по пользователям");

    private async void ItemBased_Click(object sender, RoutedEventArgs e) =>
        await ShowUserRecsAsync("item-based", "Коллаборативная фильтрация по фильмам");


    private async void Search_Click(object sender, RoutedEventArgs e) =>
        await Run(SearchFilmsAsync);

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await Run(SearchFilmsAsync);
    }

    private async void Rate_Click(object sender, RoutedEventArgs e)
    {
        var user = RequireUser();
        var film = user == null ? null : RequireFilm();
        if (user == null || film == null) return;
        int rating = RatingCombo.SelectedIndex + 1;
        await Run(async () =>
        {
            await _api.RateAsync(user.Id, film.Id, rating);
            await LoadMyRatingsAsync();
            SetStatus($"Оценка {rating} для «{film.Title}» сохранена в БД");
        });
    }

    private async Task ShowSimilarAsync(string kind, string title)
    {
        var film = RequireFilm();
        if (film == null) return;
        await Run(async () =>
        {
            SimilarGrid.ItemsSource = await _api.GetRecommendationsAsync(kind, film.Id, 10);
            SimilarGroup.Header = $"{title}: «{film.Title}»";
            SetStatus("Список получен");
        });
    }

    private async void SimilarContent_Click(object sender, RoutedEventArgs e) =>
        await ShowSimilarAsync("content", "Похожие по контенту на");

    private async void SimilarRatings_Click(object sender, RoutedEventArgs e) =>
        await ShowSimilarAsync("similar", "Похожие по оценкам зрителей на");


    private async void RefreshMyRatings_Click(object sender, RoutedEventArgs e) =>
        await Run(async () =>
        {
            await LoadMyRatingsAsync();
            SetStatus("Оценки обновлены");
        });
}
