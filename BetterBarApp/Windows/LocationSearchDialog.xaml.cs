using System.Windows;
using System.Windows.Input;
using BetterBarApp.Services.Weather;

namespace BetterBarApp.Windows;

/// <summary>
/// Modal pop-up for picking a weather location. The user searches (Open-Meteo geocoding), then
/// accepts a result by double-clicking it or selecting it and pressing OK. <see cref="Result"/> holds
/// the chosen place (null when cancelled).
/// </summary>
public partial class LocationSearchDialog : Window
{
    public GeoResult? Result { get; private set; }

    public LocationSearchDialog(Window? owner, string? initialQuery = null)
    {
        Owner = owner;
        InitializeComponent();
        Loaded += (_, _) =>
        {
            SearchBox.Text = initialQuery ?? "";
            SearchBox.Focus();
            if (!string.IsNullOrWhiteSpace(initialQuery)) _ = RunSearch();
        };
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; _ = RunSearch(); }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = RunSearch();

    private bool _searching;

    private async Task RunSearch()
    {
        if (_searching) return;
        var query = SearchBox.Text?.Trim() ?? "";
        if (query.Length < 2) { StatusText.Text = "Type at least two characters."; return; }

        _searching = true;
        SearchButton.IsEnabled  = false;
        SearchProgress.Visibility = Visibility.Visible;
        StatusText.Text = "Searching…";
        ResultsList.ItemsSource = null;
        try
        {
            var results = await WeatherService.SearchAsync(query);
            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0 ? "No matches found." : $"{results.Count} result(s).";
            if (results.Count > 0) ResultsList.SelectedIndex = 0;
        }
        catch
        {
            StatusText.Text = "Search failed. Check your connection and try again.";
        }
        finally
        {
            _searching = false;
            SearchButton.IsEnabled    = true;
            SearchProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResultsList.SelectedItem is GeoResult) Accept();
    }

    private void OK_Click(object sender, RoutedEventArgs e) => Accept();

    private void Accept()
    {
        if (ResultsList.SelectedItem is not GeoResult g) { StatusText.Text = "Select a location first."; return; }
        Result       = g;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
