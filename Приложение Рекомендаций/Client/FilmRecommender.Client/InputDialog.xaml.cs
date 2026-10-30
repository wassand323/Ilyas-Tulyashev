using System.Windows;

namespace FilmRecommender.Client;

public partial class InputDialog : Window
{
    public string Value => Input.Text.Trim();

    public InputDialog(string title, string prompt)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Loaded += (_, _) => Input.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
